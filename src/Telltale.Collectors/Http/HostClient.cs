using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Telltale.Core;

namespace Telltale.Collectors.Http;

/// <summary>
/// Raw HTTP calls to one host: a Jira Cloud site (ADR-0004: no Jira SDK) or a pull request host. The rules
/// for waiting and giving up were written for Jira and are the same for every host; where a comment below
/// says Jira, it is the host. Messages name the host the client was made for.
/// The handler is injected, so production uses the system proxy and certificate store and tests use a stub.
/// </summary>
internal sealed class HostClient : IDisposable
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
    private readonly string _host;
    private readonly TimeProvider _clock;
    private readonly TextWriter _notices;
    private TimeSpan _spentOnFailures;

    // Whether Jira has answered any request of this client. Until it has, a failure on the way to Jira (no
    // connection, the proxy's tunnel, TLS, a reset, no answer in time) is most likely a setting, and waiting
    // does not change a setting: it is reported at once. After that the route is known to work, and the same
    // failure is a moment's fault worth waiting out (owner decision D111).
    private bool _hostHasAnswered;

    /// <param name="clock">
    /// Times the waits before a request is sent again, and how long a failed request took. Timeouts stay on real time.
    /// </param>
    /// <param name="notices">Where each such wait is announced (standard error), so a waiting command is not a silent one.</param>
    /// <param name="host">The host's name as messages say it: "Jira", "GitHub".</param>
    /// <param name="baseUrl">What every request's path is relative to. It ends in a slash.</param>
    /// <param name="authorization">Sent on every request: no host here sends an authentication challenge.</param>
    public HostClient(HttpMessageHandler handler, string host, Uri baseUrl, AuthenticationHeaderValue authorization, TimeProvider clock, TextWriter notices)
    {
        _host = host;
        _clock = clock;
        _notices = notices;
        _http = new HttpClient(handler, disposeHandler: false) { BaseAddress = baseUrl };
        _http.DefaultRequestHeaders.Authorization = authorization;
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>A client for one Jira Cloud site, with basic authentication.</summary>
    public static HostClient ForJira(HttpMessageHandler handler, Uri siteUrl, string email, string apiToken, TimeProvider clock, TextWriter notices) =>
        new(handler, "Jira", siteUrl, new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{apiToken}"))), clock, notices);

    /// <summary>A header sent on every request besides the authorization, e.g. the API version a host asks to be named.</summary>
    public void AddHeader(string name, string value) => _http.DefaultRequestHeaders.TryAddWithoutValidation(name, value);

    /// <summary>
    /// GET one page of a list whose answer names the next page in a <c>Link</c> header with <c>rel="next"</c>
    /// (GitHub). <see cref="Page.Next"/> is that page's address relative to the base address, to pass back
    /// here, or null when the answer names none: the list has ended. The address is the host's own and is
    /// followed as given, but only on the host itself: one that leads elsewhere would send the token there,
    /// so it stops the command.
    /// </summary>
    public async Task<Page> GetPageAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        var answer = await SendAsync(HttpMethod.Get, relativeUrl, body: null, cancellationToken);
        return new Page(answer.Body, answer.Next);
    }

    /// <summary>One page of a list: its JSON, and the next page's address when the answer names one.</summary>
    public sealed record Page(JsonDocument Body, string? Next) : IDisposable
    {
        public void Dispose() => Body.Dispose();
    }

    /// <summary>
    /// GET one answer of a list that says there is more in an <c>x-ms-continuationtoken</c> header (Azure
    /// DevOps). <see cref="ContinuedPage.Continuation"/> is that token, to send back as the host's
    /// documentation says, or null when the answer carries none: the list has ended.
    /// </summary>
    public async Task<ContinuedPage> GetContinuedPageAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        var answer = await SendAsync(HttpMethod.Get, relativeUrl, body: null, cancellationToken);
        return new ContinuedPage(answer.Body, answer.Continuation);
    }

    /// <summary>One answer of a list: its JSON, and the token to ask for the rest with when the answer carries one.</summary>
    public sealed record ContinuedPage(JsonDocument Body, string? Continuation) : IDisposable
    {
        public void Dispose() => Body.Dispose();
    }

    private sealed record Answer(JsonDocument Body, string? Next, string? Continuation);

    /// <summary>The token in the answer's <c>x-ms-continuationtoken</c> header; null without one.</summary>
    private static string? ContinuationToken(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-ms-continuationtoken", out var values) && string.Concat(values).Trim() is { Length: > 0 } token ? token : null;

    /// <summary>The address of the next page from the answer's <c>Link</c> header, relative to the base address; null without one.</summary>
    private string? NextPage(HttpResponseMessage response, string call)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        // Link: <https://host/path?page=2>; rel="next", <https://host/path?page=9>; rel="last"
        foreach (var link in string.Join(",", values).Split(','))
        {
            var parts = link.Split(';', StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || !parts.Skip(1).Any(part => part.Replace(" ", "", StringComparison.Ordinal) is "rel=\"next\"" or "rel=next"))
            {
                continue;
            }

            var baseUrl = _http.BaseAddress!.AbsoluteUri;
            if (parts[0] is ['<', .., '>']
                && Uri.TryCreate(_http.BaseAddress, parts[0][1..^1], out var next)
                && next.AbsoluteUri.StartsWith(baseUrl, StringComparison.Ordinal))
            {
                return next.AbsoluteUri[baseUrl.Length..];
            }

            throw new UserFacingException($"{_host} answered {call} with a next page that is not under the configured address, so it was not followed. Nothing was stored.");
        }

        return null;
    }

    /// <summary>GET a path relative to the site, e.g. <c>rest/api/3/status</c>.</summary>
    public async Task<JsonDocument> GetAsync(string relativeUrl, CancellationToken cancellationToken) =>
        (await SendAsync(HttpMethod.Get, relativeUrl, body: null, cancellationToken)).Body;

    /// <summary>POST a JSON body to a path relative to the site.</summary>
    public async Task<JsonDocument> PostAsync(string relativeUrl, JsonObject body, CancellationToken cancellationToken) =>
        (await SendAsync(HttpMethod.Post, relativeUrl, body, cancellationToken)).Body;

    /// <summary>
    /// The status of the last answer that was an error from Jira itself, or null when there has been none.
    /// A proxy's 407 is not one: that request never reached Jira. A 429 the client gave up on is one.
    /// </summary>
    public HttpStatusCode? LastRefusal { get; private set; }

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Sends a request and reads the JSON answer. A rate-limited answer (429, or a used-up limit that names
    /// its reset, see <see cref="RateLimitReset"/>) is waited out and the request sent again, unless the wait
    /// asked for is longer than <see cref="LongestWait"/>; so is, a few times, a failure that can pass by itself (<see cref="MaxTransientRetries"/>).
    /// Every other way this can fail for reasons outside the tool becomes a
    /// <see cref="UserFacingException"/> that names the request and never quotes the response, which can hold
    /// issue or person data.
    /// </summary>
    private async Task<Answer> SendAsync(HttpMethod method, string relativeUrl, JsonObject? body, CancellationToken cancellationToken)
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
    private async Task<Answer?> SendOnceAsync(HttpMethod method, string relativeUrl, string call, JsonObject? body, Retries retries, CancellationToken cancellationToken)
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
            if (_hostHasAnswered && CanPass(exception))
            {
                var failed = $"The connection to {_host} failed on {call}";
                if (await RetryAsync($"The connection to {_host} failed", failed, call, started, PassingWait(retries), cancellationToken,
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
                $"Could not reach {_host} ({call}): {reason}. On the corporate network this usually means the proxy or the certificate: "
                + "check that the system proxy is set and that the corporate root certificate is trusted.",
                exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Not the user's Ctrl+C: HttpClient's own timeout.
            var failed = $"{_host} did not answer {call} in time";
            if (!_hostHasAnswered)
            {
                throw new UserFacingException($"{failed}.", exception);
            }

            if (await RetryAsync($"{_host} did not answer in time", failed, call, started, PassingWait(retries), cancellationToken,
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
                    $"The proxy asked for authentication (407) on {call}, so the request never reached {_host}. "
                    + "Check the system proxy settings and that this account may use the proxy.");
            }

            // An answer came back through the proxy, so the route to Jira works.
            _hostHasAnswered = true;

            if (RateLimitReset(response) is { } reset)
            {
                // The host says its limit is used up and when it takes requests again (GitHub). A reset within
                // the longest wait is waited out like any rate-limited answer. A later one ends the run at
                // once, naming the time: the tool does not sit silent for the rest of an hour.
                LastRefusal = response.StatusCode;
                var usedUp = $"{_host}'s rate limit is used up: it answered {(int)response.StatusCode} ({response.StatusCode}) to {call}";
                var wait = reset - _clock.GetUtcNow();
                if (wait > LongestWait)
                {
                    throw new UserFacingException(
                        $"{usedUp} and takes requests again at {reset.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} UTC. Run the command again after that.");
                }

                if (await RetryAsync($"{_host}'s rate limit is used up", usedUp, call, started, () => wait > ShortestWait ? wait : ShortestWait, cancellationToken,
                        (retries.RateLimited, MaxRetries)) is { } gaveUp)
                {
                    throw new UserFacingException(gaveUp);
                }

                retries.RateLimited++;
                return null;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Whatever ends the run below, the request reached Jira and Jira refused it.
                LastRefusal = response.StatusCode;
                var rateLimited = RateLimited(call);
                if (await RetryAsync($"{_host} is limiting how often it can be asked", rateLimited, call, started,
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
                var answered = $"{_host} answered {(int)response.StatusCode} ({response.StatusCode})";
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
                return new Answer(await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken), NextPage(response, call), ContinuationToken(response));
            }
            catch (JsonException exception)
            {
                throw new UserFacingException($"The answer to {call} was not JSON. A proxy or sign-in page may have been served in place of {_host}'s answer.", exception);
            }
        }
    }

    /// <summary>
    /// When the host takes requests again, for an answer that says its rate limit is used up the way GitHub
    /// says it: 403 or 429 with <c>x-ratelimit-remaining: 0</c> and the time in <c>x-ratelimit-reset</c>, in
    /// seconds since the Unix epoch. Null for every other answer, a 403 with requests left among them: that
    /// one is a refusal, and waiting does not change it. Null too when the time cannot be read.
    /// </summary>
    private static DateTimeOffset? RateLimitReset(HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            || !response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) || string.Concat(remaining).Trim() != "0"
            || !response.Headers.TryGetValues("x-ratelimit-reset", out var resets)
            || !long.TryParse(string.Concat(resets).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
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

    private string RateLimited(string call) =>
        $"{_host} is limiting how often it can be asked: it answered 429 (too many requests) to {call}";
}
