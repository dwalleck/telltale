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

    // The most one client, and so one command, waits on rate limits in all.
    private static readonly TimeSpan LongestTotalWait = TimeSpan.FromMinutes(15);

    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly TextWriter _notices;
    private TimeSpan _waited;

    /// <param name="clock">Times the waits between retries of a rate-limited request. Nothing else: timeouts stay on real time.</param>
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
    /// sent again. Every way this can fail for reasons outside the tool becomes a
    /// <see cref="UserFacingException"/> that names the request and never quotes the response, which can hold
    /// issue or person data.
    /// </summary>
    private async Task<JsonDocument> SendAsync(HttpMethod method, string relativeUrl, JsonObject? body, CancellationToken cancellationToken)
    {
        var call = $"{method} {relativeUrl}";
        for (var retries = 0; ; retries++)
        {
            // Every call the tool makes only reads, the POSTs too, so sending one again is safe.
            if (await SendOnceAsync(method, relativeUrl, call, body, retries, cancellationToken) is { } answer)
            {
                return answer;
            }
        }
    }

    /// <summary>
    /// Sends the request once. Null when Jira answered that it is rate limited and the wait before the next
    /// try is over. <paramref name="retries"/> is how often this request was already sent again.
    /// </summary>
    private async Task<JsonDocument?> SendOnceAsync(HttpMethod method, string relativeUrl, string call, JsonObject? body, int retries, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
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
            throw new UserFacingException($"Jira did not answer {call} in time.", exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.ProxyAuthenticationRequired)
            {
                throw new UserFacingException(
                    $"The proxy asked for authentication (407) on {call}, so the request never reached Jira. "
                    + "Check the system proxy settings and that this account may use the proxy.");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Whatever ends the run below, the request reached Jira and Jira refused it.
                LastRefusal = response.StatusCode;
                if (retries == MaxRetries)
                {
                    throw new UserFacingException($"{RateLimited(call)} again after {MaxRetries} retries. Run the command again later.");
                }

                var wait = RetryWait(response, call, retries);
                if (_waited + wait > LongestTotalWait)
                {
                    throw new UserFacingException(
                        $"{RateLimited(call)}, and waiting again would bring this command's waits to more than {LongestTotalWait.TotalMinutes:0} minutes in all. Run the command again later.");
                }

                // The number of seconds is the only thing of Jira's answer that is repeated.
                await _notices.WriteLineAsync($"Jira is limiting how often it can be asked. Waiting {wait.TotalSeconds:0} seconds before asking again ({call}).");
                _waited += wait;
                await Task.Delay(wait, _clock, cancellationToken);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                LastRefusal = response.StatusCode;
                throw new UserFacingException($"Jira answered {(int)response.StatusCode} ({response.StatusCode}) to {call}.");
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

    /// <summary>
    /// How long to wait before sending a rate-limited request again: what the answer's <c>Retry-After</c>
    /// header asks for, as a number of seconds or as a time to come back at. With no such header, the wait
    /// starts at five seconds and doubles with every retry of the request. A header that is there but cannot
    /// be read (a number too large for a wait, a fraction, text) is not taken for a missing one: it ends the
    /// run like a wait that is too long.
    /// </summary>
    private TimeSpan RetryWait(HttpResponseMessage response, string call, int retries)
    {
        TimeSpan? wait = !response.Headers.TryGetValues("Retry-After", out var values)
            ? FirstWaitWithoutRetryAfter * Math.Pow(2, retries)
            : RetryConditionHeaderValue.TryParse(string.Join(",", values), out var asked)
                ? asked.Delta ?? asked.Date - _clock.GetUtcNow()
                : null;

        // The message does not quote the header.
        if (wait is not { } readable || readable > LongestWait)
        {
            throw new UserFacingException(
                $"{RateLimited(call)} and asked for a wait of more than {LongestWait.TotalMinutes:0} minutes, or one this tool cannot read. Run the command again later.");
        }

        return readable > ShortestWait ? readable : ShortestWait;
    }

    private static string RateLimited(string call) =>
        $"Jira is limiting how often it can be asked: it answered 429 (too many requests) to {call}";
}
