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

    // Added to a wait for a reset that is counted from the host's own time: the reset and that time are both
    // given in whole seconds, and the answer was some moments on its way.
    private static readonly TimeSpan MarginAfterAReset = TimeSpan.FromSeconds(5);

    // GitHub asks for at least a minute after an answer of its secondary rate limit that names no wait. The
    // wait doubles with every such answer to one request, up to the longest wait.
    private static readonly TimeSpan FirstWaitOfGitHubWithoutRetryAfter = TimeSpan.FromMinutes(1);

    // How often one request is sent again after a failure that can pass by itself: a 408 or a 5xx that Jira or
    // a gateway answers under load, a connection that drops, an answer that does not come in time. A fetch is one
    // batch (owner decision D38), so without any retry one such moment late in a backfill discards the whole
    // fetch. The waits are what the answer's Retry-After asks for or, without one, start at five seconds and
    // double: five retries wait two and a half minutes in all, which outlasts a gateway's restart in an
    // unattended backfill. These retries are counted apart from a request's rate-limit retries.
    private const int MaxTransientRetries = 5;

    // An answer that does not come in time gets fewer of those retries: each try waits out HttpClient's whole timeout first.
    private const int MaxRetriesAfterNoAnswer = 3;

    // The most one client spends on failures in all: on the requests that failed and were sent again, and on
    // the waits before sending them. A timeout can take HttpClient's whole timeout, so counting only the waits
    // would not bound how long a read runs. The bound is per client, and so per host: a command makes one
    // client for Jira and one for each configured pull request host, each with this much of its own, so a
    // command that reads three hosts can spend three times this.
    private static readonly TimeSpan LongestTimeOnFailures = TimeSpan.FromMinutes(15);

    private readonly HttpClient _http;
    private readonly string _host;
    private readonly TimeProvider _clock;
    private readonly TextWriter _notices;
    private readonly bool _namesTheResetOfAUsedUpLimit;
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
    /// <param name="namesTheResetOfAUsedUpLimit">
    /// Whether the host says a used-up rate limit the way GitHub does (see <see cref="RateLimitReset"/>). Only
    /// GitHub's client says so: Azure DevOps sends headers of the same names with another meaning (its reset
    /// is when the usage it tracks is back to nothing, not when requests are taken again) and asks its
    /// clients to wait what <c>Retry-After</c> says, which is what every other host's answers are read by.
    /// The rest of how GitHub limits its clients goes with it: a 403 that carries a readable
    /// <c>Retry-After</c> is a rate-limited answer too (its secondary rate limit), and a 429 that names no
    /// wait is waited on for a minute first, not five seconds.
    /// </param>
    public HostClient(HttpMessageHandler handler, string host, Uri baseUrl, AuthenticationHeaderValue authorization, TimeProvider clock, TextWriter notices, bool namesTheResetOfAUsedUpLimit = false)
    {
        _host = host;
        _namesTheResetOfAUsedUpLimit = namesTheResetOfAUsedUpLimit;
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
        // The Link header is read here and nowhere else: an answer to a request that is not paged by it can
        // carry one too, and that one is never followed, so it must not be able to stop the request.
        var answer = await SendAsync(HttpMethod.Get, relativeUrl, body: null, cancellationToken);
        try
        {
            return new Page(answer.Body, answer.Link is null ? null : NextPage(answer.Link, $"{HttpMethod.Get} {relativeUrl}"));
        }
        catch
        {
            answer.Body.Dispose();
            throw;
        }
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

    /// <summary>An answer's JSON, with the two headers a list can be paged by as they came: neither is interpreted until a caller pages by it.</summary>
    private sealed record Answer(JsonDocument Body, string? Link, string? Continuation);

    /// <summary>The token in the answer's <c>x-ms-continuationtoken</c> header; null without one.</summary>
    private static string? ContinuationToken(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-ms-continuationtoken", out var values) && string.Concat(values).Trim() is { Length: > 0 } token ? token : null;

    /// <summary>The answer's <c>Link</c> header as it came, its values joined; null without one.</summary>
    private static string? LinkHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Link", out var values) ? string.Join(",", values) : null;

    /// <summary>The address of the next page from an answer's <c>Link</c> header, relative to the base address; null when it names none.</summary>
    private string? NextPage(string links, string call)
    {
        // Link: <https://host/path?page=2>; rel="next", <https://host/path?page=9>; rel="last"
        foreach (var link in links.Split(','))
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

            throw new HostGaveUpException($"{_host} answered {call} with a next page that is not under the configured address, so it was not followed. Nothing was stored.");
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
    /// Sends a request and reads the JSON answer. A rate-limited answer (429, a used-up limit that names
    /// its reset, see <see cref="RateLimitReset"/>, or of GitHub a 403 that asks for a wait) is waited out and the request sent again, unless the wait
    /// asked for is longer than <see cref="LongestWait"/>; so is, a few times, a failure that can pass by itself (<see cref="MaxTransientRetries"/>).
    /// Every other way this can fail for reasons outside the tool becomes a
    /// <see cref="UserFacingException"/> that names the request and never quotes the response, which can hold
    /// issue or person data. It is a <see cref="HostGaveUpException"/> unless the host refused the one thing
    /// asked for (a 403 or a 404), which a caller that reads many things may pass over.
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
                    throw new HostGaveUpException(gaveUp, exception);
                }

                retries.Passing++;
                return null;
            }

            // DNS, connection, proxy tunnel and TLS failures. The category is enough; the message can name hosts.
            var reason = exception.HttpRequestError == HttpRequestError.Unknown ? "the connection failed" : exception.HttpRequestError.ToString();
            throw new HostGaveUpException(
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
                throw new HostGaveUpException($"{failed}.", exception);
            }

            if (await RetryAsync($"{_host} did not answer in time", failed, call, started, PassingWait(retries), cancellationToken,
                    (retries.Passing, MaxTransientRetries), (retries.NoAnswer, MaxRetriesAfterNoAnswer)) is { } gaveUp)
            {
                throw new HostGaveUpException(gaveUp, exception);
            }

            retries.Passing++;
            retries.NoAnswer++;
            return null;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.ProxyAuthenticationRequired)
            {
                throw new HostGaveUpException(
                    $"The proxy asked for authentication (407) on {call}, so the request never reached {_host}. "
                    + "Check the system proxy settings and that this account may use the proxy.");
            }

            // An answer came back through the proxy, so the route to Jira works.
            _hostHasAnswered = true;

            if (_namesTheResetOfAUsedUpLimit && RateLimitReset(response) is { } reset)
            {
                // The host says its limit is used up and when it takes requests again (GitHub). A reset within
                // the longest wait is waited out like any rate-limited answer. A later one ends the run at
                // once, naming the time: the tool does not sit silent for the rest of an hour. An answer that
                // also asks for a wait of its own (a readable Retry-After) is taken at that word: the wait is
                // what the host asked for, and the time named is the end of it.
                LastRefusal = response.StatusCode;
                var usedUp = $"{_host}'s rate limit is used up: it answered {(int)response.StatusCode} ({response.StatusCode}) to {call}";
                //
                // The reset is a time on the host's clock, and this machine's can run ahead of it: counted
                // from this machine's clock the wait would end before the limit is lifted. So it is counted
                // from the time the answer itself gives (its Date header), with a few seconds added because
                // both times are whole seconds, and from this machine's clock only when the answer gives none.
                var now = _clock.GetUtcNow();
                TimeSpan wait;
                if (RetryAfter(response) is { } asked)
                {
                    (reset, wait) = (now + asked, asked);
                }
                else if (response.Headers.Date is { } hostTime)
                {
                    wait = reset - hostTime;
                    if (wait <= LongestWait)
                    {
                        wait += MarginAfterAReset;
                    }
                }
                else
                {
                    wait = reset - now;
                }

                if (wait > LongestWait)
                {
                    throw new HostGaveUpException(
                        $"{usedUp} and takes requests again at {reset.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} UTC. Run the command again after that.");
                }

                // A limit the host repeats after it was waited on is not asked about again a second later,
                // five times over: whatever the answer says, the wait is then at least five seconds, doubled
                // with every further such answer, so the retries that are left span a clock that is off.
                var shortest = retries.RateLimited == 0 ? ShortestWait : FirstWaitWithoutRetryAfter * Math.Pow(2, retries.RateLimited - 1);
                if (await RetryAsync($"{_host}'s rate limit is used up", usedUp, call, started, () => wait > shortest ? wait : shortest, cancellationToken,
                        (retries.RateLimited, MaxRetries)) is { } gaveUp)
                {
                    throw new HostGaveUpException(gaveUp);
                }

                retries.RateLimited++;
                return null;
            }

            // GitHub's secondary rate limit is a 403 or a 429 with requests left in the limit the headers
            // count, so the branch above does not take it. One that carries a readable Retry-After asks for a
            // wait, whatever its status, and is waited out like any rate-limited answer. A 403 without one
            // stays a refusal: the answer's body is not read to guess what it meant.
            var asksForAWaitAsGitHubDoes = _namesTheResetOfAUsedUpLimit && response.StatusCode == HttpStatusCode.Forbidden && RetryAfter(response) is not null;
            if (response.StatusCode == HttpStatusCode.TooManyRequests || asksForAWaitAsGitHubDoes)
            {
                // Whatever ends the run below, the request reached Jira and Jira refused it.
                LastRefusal = response.StatusCode;
                var rateLimited = RateLimited(call, response.StatusCode);
                if (await RetryAsync($"{_host} is limiting how often it can be asked", rateLimited, call, started,
                        () => RetryWait(response, rateLimited, WaitWithoutRetryAfter(retries)), cancellationToken,
                        (retries.RateLimited, MaxRetries)) is { } gaveUp)
                {
                    throw new HostGaveUpException(gaveUp);
                }

                retries.RateLimited++;
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                LastRefusal = response.StatusCode;
                var answered = $"{_host} answered {(int)response.StatusCode} ({response.StatusCode})";
                var failed = $"{answered} to {call}";
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                {
                    // The host refused what was asked for, or does not have it: that is about this request's
                    // subject. Every other failure here is the client giving up (HostGaveUpException).
                    throw new UserFacingException($"{failed}.");
                }

                if (!CanPass(response.StatusCode))
                {
                    throw new HostGaveUpException($"{failed}.");
                }

                if (await RetryAsync(answered, failed, call, started, () => RetryWait(response, failed, PassingWait(retries)()), cancellationToken,
                        (retries.Passing, MaxTransientRetries)) is { } gaveUp)
                {
                    throw new HostGaveUpException(gaveUp);
                }

                retries.Passing++;
                return null;
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return new Answer(await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken), LinkHeader(response), ContinuationToken(response));
            }
            catch (JsonException exception)
            {
                throw new HostGaveUpException($"The answer to {call} was not JSON. A proxy or sign-in page may have been served in place of {_host}'s answer.", exception);
            }
        }
    }

    /// <summary>
    /// When the host takes requests again, for an answer that says its rate limit is used up the way GitHub
    /// says it (asked only of a client made for such a host): 403 or 429 with <c>x-ratelimit-remaining: 0</c> and the time in <c>x-ratelimit-reset</c>, in
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
        TimeSpan? wait = response.Headers.Contains("Retry-After") ? RetryAfter(response) : withoutHeader;

        // The message does not quote the header.
        if (wait is not { } readable || readable > LongestWait)
        {
            throw new HostGaveUpException(
                $"{failed} and asked for a wait of more than {LongestWait.TotalMinutes:0} minutes, or one this tool cannot read. Run the command again later.");
        }

        return readable > ShortestWait ? readable : ShortestWait;
    }

    /// <summary>The wait an answer's <c>Retry-After</c> header asks for, as a number of seconds or as a time to come back at; null without the header, and for one that cannot be read.</summary>
    private TimeSpan? RetryAfter(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Retry-After", out var values) && RetryConditionHeaderValue.TryParse(string.Join(",", values), out var asked)
            ? asked.Delta ?? asked.Date - _clock.GetUtcNow()
            : null;

    /// <summary>
    /// The wait before the next retry for a rate-limited answer that names no wait: five seconds, doubled with
    /// every such retry. Of GitHub a minute, doubled, and never more than the longest wait: GitHub asks for at
    /// least a minute after a secondary rate limit, and a wait the tool chose itself must not end the run as
    /// one the host asked for that was too long.
    /// </summary>
    private TimeSpan WaitWithoutRetryAfter(Retries retries)
    {
        if (!_namesTheResetOfAUsedUpLimit)
        {
            return FirstWaitWithoutRetryAfter * Math.Pow(2, retries.RateLimited);
        }

        var wait = FirstWaitOfGitHubWithoutRetryAfter * Math.Pow(2, retries.RateLimited);
        return wait < LongestWait ? wait : LongestWait;
    }

    private string RateLimited(string call, HttpStatusCode status) =>
        $"{_host} is limiting how often it can be asked: it answered {(status == HttpStatusCode.TooManyRequests ? "429 (too many requests)" : $"{(int)status} ({status})")} to {call}";
}
