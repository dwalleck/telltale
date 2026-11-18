using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Telltale.Core;

namespace Telltale.Collectors.Jira;

/// <summary>
/// Raw HTTP calls to one Jira Cloud site with basic authentication (ADR-0004: no Jira SDK).
/// The handler is injected, so production uses the system proxy and certificate store and tests use a stub.
/// </summary>
internal sealed class JiraClient : IDisposable
{
    // How often one request is sent again after a rate-limited answer before the run gives up.
    private const int MaxRetries = 5;

    // The wait before a retry is what Jira asks for, within these bounds. A longer one ends the run: the tool
    // would otherwise sit silent for as long as any answer says. A shorter one (zero, a time already past)
    // is raised, so a retry is never sent at once.
    private static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ShortestWait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FirstWaitWithoutRetryAfter = TimeSpan.FromSeconds(5);

    // How often one request is sent again after a failure that can pass by itself: a 408 or a 5xx that Jira or
    // a gateway answers under load, a connection that drops, an answer that does not come in time. A fetch is one
    // batch (owner decision D38), so without any retry one such moment late in a backfill discards the whole
    // fetch. The waits are what the answer's Retry-After asks for or, without one, start at five seconds and
    // double: five retries wait two and a half minutes in all, which outlasts a gateway's restart in an
    // unattended backfill. These retries are counted apart from a request's rate-limit retries.
    private const int MaxTransientRetries = 5;

    // An answer that does not come in time gets fewer of those retries: each try waits out HttpClient's whole timeout first.
    private const int MaxRetriesAfterNoAnswer = 3;

    // The most one client, and so one command, spends on failures in all: on the requests that failed and were
    // sent again, and on the waits before sending them. A timeout can take HttpClient's whole timeout, so
    // counting only the waits would not bound how long a command runs.
    private static readonly TimeSpan LongestTimeOnFailures = TimeSpan.FromMinutes(15);

    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly TextWriter _notices;
    private TimeSpan _spentOnFailures;

    // Whether Jira has answered any request of this client. Until it has, a failure on the way to Jira (no
    // connection, the proxy's tunnel, TLS, a reset, no answer in time) is most likely a setting, and waiting
    // does not change a setting: it is reported at once. After that the route is known to work, and the same
    // failure is a moment's fault worth waiting out (owner decision D111).
    private bool _jiraHasAnswered;

    /// <param name="clock">
    /// Times the waits before a request is sent again, and how long a failed request took. Timeouts stay on real time.
    /// </param>
    /// <param name="notices">Where each such wait is announced (standard error), so a waiting command is not a silent one.</param>
    public JiraClient(HttpMessageHandler handler, Uri siteUrl, string email, string apiToken, TimeProvider clock, TextWriter notices)
    {
        _clock = clock;
        _notices = notices;
        _http = new HttpClient(handler, disposeHandler: false) { BaseAddress = siteUrl };

        // Jira sends no authentication challenge, so the header goes on every request.
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{apiToken}")));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>GET a path relative to the site, e.g. <c>rest/api/3/status</c>.</summary>
    public Task<JsonDocument> GetAsync(string relativeUrl, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, relativeUrl, body: null, cancellationToken);

    /// <summary>POST a JSON body to a path relative to the site.</summary>
    public Task<JsonDocument> PostAsync(string relativeUrl, JsonObject body, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, relativeUrl, body, cancellationToken);

    /// <summary>
    /// The status of the last answer that was an error from Jira itself, or null when there has been none.
    /// A proxy's 407 is not one: that request never reached Jira. A 429 the client gave up on is one.
    /// </summary>
    public HttpStatusCode? LastRefusal { get; private set; }

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Sends a request and reads the JSON answer. A rate-limited answer (429) is waited out and the request
    /// sent again; so is, a few times, a failure that can pass by itself (<see cref="MaxTransientRetries"/>).
    /// Every other way this can fail for reasons outside the tool becomes a
    /// <see cref="UserFacingException"/> that names the request and never quotes the response, which can hold
    /// issue or person data.
    /// </summary>
    private async Task<JsonDocument> SendAsync(HttpMethod method, string relativeUrl, JsonObject? body, CancellationToken cancellationToken)
    {
        var call = $"{method} {relativeUrl}";
        var retries = new Retries();
        while (true)
        {
            // Every call the tool makes only reads, the POSTs too, so sending one again is safe.
            if (await SendOnceAsync(method, relativeUrl, call, body, retries, cancellationToken) is { } answer)
            {
                return answer;
            }
        }
    }

    /// <summary>How often one request was already sent again, for each kind of reason.</summary>
    private sealed class Retries
    {
        public int RateLimited;
        public int Passing;
        public int NoAnswer;
    }

    /// <summary>
    /// Sends the request once. Null when the request is to be sent again and the wait before that is over:
    /// Jira answered that it is rate limited, or the request failed in a way that can pass by itself.
    /// </summary>
    private async Task<JsonDocument?> SendOnceAsync(HttpMethod method, string relativeUrl, string call, JsonObject? body, Retries retries, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var started = _clock.GetTimestamp();
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            if (_jiraHasAnswered && CanPass(exception))
            {
                var failed = $"The connection to Jira failed on {call}";
                if (await RetryAsync("The connection to Jira failed", failed, call, started, PassingWait(retries), cancellationToken,
                        (retries.Passing, MaxTransientRetries)) is { } gaveUp)
                {
                    throw new UserFacingException(gaveUp, exception);
                }

                retries.Passing++;
                return null;
            }

            // DNS, connection, proxy tunnel and TLS failures. The category is enough; the message can name hosts.
            var reason = exception.HttpRequestError == HttpRequestError.Unknown ? "the connection failed" : exception.HttpRequestError.ToString();
            throw new UserFacingException(
                $"Could not reach Jira ({call}): {reason}. On the corporate network this usually means the proxy or the certificate: "
                + "check that the system proxy is set and that the corporate root certificate is trusted.",
                exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Not the user's Ctrl+C: HttpClient's own timeout.
            var failed = $"Jira did not answer {call} in time";
            if (!_jiraHasAnswered)
            {
                throw new UserFacingException($"{failed}.", exception);
            }

            if (await RetryAsync("Jira did not answer in time", failed, call, started, PassingWait(retries), cancellationToken,
                    (retries.Passing, MaxTransientRetries), (retries.NoAnswer, MaxRetriesAfterNoAnswer)) is { } gaveUp)
            {
                throw new UserFacingException(gaveUp, exception);
            }

            retries.Passing++;
            retries.NoAnswer++;
            return null;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.ProxyAuthenticationRequired)
            {
                throw new UserFacingException(
                    $"The proxy asked for authentication (407) on {call}, so the request never reached Jira. "
                    + "Check the system proxy settings and that this account may use the proxy.");
            }

            // An answer came back through the proxy, so the route to Jira works.
            _jiraHasAnswered = true;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Whatever ends the run below, the request reached Jira and Jira refused it.
                LastRefusal = response.StatusCode;
                var rateLimited = RateLimited(call);
                if (await RetryAsync("Jira is limiting how often it can be asked", rateLimited, call, started,
                        () => RetryWait(response, rateLimited, FirstWaitWithoutRetryAfter * Math.Pow(2, retries.RateLimited)), cancellationToken,
                        (retries.RateLimited, MaxRetries)) is { } gaveUp)
                {
                    throw new UserFacingException(gaveUp);
                }

                retries.RateLimited++;
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                LastRefusal = response.StatusCode;
                var answered = $"Jira answered {(int)response.StatusCode} ({response.StatusCode})";
                var failed = $"{answered} to {call}";
                if (!CanPass(response.StatusCode))
                {
                    throw new UserFacingException($"{failed}.");
                }

                if (await RetryAsync(answered, failed, call, started, () => RetryWait(response, failed, PassingWait(retries)()), cancellationToken,
                        (retries.Passing, MaxTransientRetries)) is { } gaveUp)
                {
                    throw new UserFacingException(gaveUp);
                }

                retries.Passing++;
                return null;
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new UserFacingException($"The answer to {call} was not JSON. A proxy or sign-in page may have been served in place of Jira's answer.", exception);
            }
        }
    }

    /// <summary>Answers that say Jira, or a gateway in front of it, could not serve the request now. Any other error is about the request.</summary>
    private static bool CanPass(HttpStatusCode status) => status is HttpStatusCode.RequestTimeout
        or HttpStatusCode.InternalServerError
        or HttpStatusCode.BadGateway
        or HttpStatusCode.ServiceUnavailable
        or HttpStatusCode.GatewayTimeout;

    /// <summary>
    /// Failures on the way to Jira that a moment can cause, once the route is known to work: no connection, a
    /// name that did not resolve, the proxy's tunnel refused (a proxy answers CONNECT with 502 to 504 under
    /// load), a TLS handshake cut off, an answer that ended early, and a reset. .NET reports a reset as
    /// <see cref="HttpRequestError.Unknown"/> with an <see cref="IOException"/> inside, not as a connection error.
    /// </summary>
    private static bool CanPass(HttpRequestException exception) => exception.HttpRequestError switch
    {
        HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.ProxyTunnelError
            or HttpRequestError.SecureConnectionError or HttpRequestError.ResponseEnded => true,
        HttpRequestError.Unknown => exception.InnerException is IOException,
        _ => false,
    };

    /// <summary>The wait before the next retry for a passing failure that names no wait: five seconds, doubled with every such retry.</summary>
    private static Func<TimeSpan> PassingWait(Retries retries) => () => FirstWaitWithoutRetryAfter * Math.Pow(2, retries.Passing);

    /// <summary>
    /// Waits before a request that failed is sent again, and says so with <paramref name="notice"/>, which holds
    /// nothing of the response. Returns null after the wait. Without waiting, returns the message that ends the
    /// run when any of <paramref name="limits"/> (retries so far, most allowed) is reached, or when the wait would
    /// take the command past <see cref="LongestTimeOnFailures"/>: <paramref name="failed"/>, which names the
    /// request, and why it is not sent again.
    /// </summary>
    private async Task<string?> RetryAsync(string notice, string failed, string call, long started, Func<TimeSpan> waitFor, CancellationToken cancellationToken,
        params (int Retries, int Max)[] limits)
    {
        _spentOnFailures += _clock.GetElapsedTime(started);
        if (limits.FirstOrDefault(limit => limit.Retries >= limit.Max) is { Max: > 0 } reached)
        {
            return $"{failed} again after {reached.Max} retries. Run the command again later.";
        }

        var wait = waitFor();
        if (_spentOnFailures + wait > LongestTimeOnFailures)
        {
            return $"{failed}, and waiting again would bring the time this command has spent on failed requests and on waits to more than {LongestTimeOnFailures.TotalMinutes:0} minutes. Run the command again later.";
        }

        await _notices.WriteLineAsync($"{notice}. Waiting {wait.TotalSeconds:0} seconds before asking again ({call}).");
        _spentOnFailures += wait;
        await Task.Delay(wait, _clock, cancellationToken);
        return null;
    }

    /// <summary>
    /// How long to wait before sending a request again: what the answer's <c>Retry-After</c> header asks for,
    /// as a number of seconds or as a time to come back at, and <paramref name="withoutHeader"/> when there is no
    /// such header. A header that is there but cannot be read (a number too large for a wait, a fraction, text)
    /// is not taken for a missing one: it ends the run like a wait that is too long. <paramref name="failed"/>
    /// begins that message.
    /// </summary>
    private TimeSpan RetryWait(HttpResponseMessage response, string failed, TimeSpan withoutHeader)
    {
        TimeSpan? wait = !response.Headers.TryGetValues("Retry-After", out var values)
            ? withoutHeader
            : RetryConditionHeaderValue.TryParse(string.Join(",", values), out var asked)
                ? asked.Delta ?? asked.Date - _clock.GetUtcNow()
                : null;

        // The message does not quote the header.
        if (wait is not { } readable || readable > LongestWait)
        {
            throw new UserFacingException(
                $"{failed} and asked for a wait of more than {LongestWait.TotalMinutes:0} minutes, or one this tool cannot read. Run the command again later.");
        }

        return readable > ShortestWait ? readable : ShortestWait;
    }

    private static string RateLimited(string call) =>
        $"Jira is limiting how often it can be asked: it answered 429 (too many requests) to {call}";
}
