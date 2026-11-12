using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Web;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>What a route sees: method, path, parsed query and the body already read as text.</summary>
public sealed record StubRequest(HttpMethod Method, string Path, IReadOnlyDictionary<string, string> Query, string Body, HttpRequestHeaders Headers);

/// <summary>
/// Fake HTTP server. Routes match on method and path, plus an optional predicate over the query and
/// body. Each route holds a sequence of responses: one per call, the last one repeating. The first
/// matching route wins. A request no route matches fails the test with the URL in the message.
/// </summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Lock _gate = new();
    private readonly List<Route> _routes = [];
    private readonly List<StubRequest> _requests = [];

    /// <summary>Every request received so far, for assertions on what was asked (never on call order).</summary>
    public IReadOnlyList<StubRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>Adds a route after the existing ones.</summary>
    public StubHttpHandler On(HttpMethod method, string path, params Func<StubRequest, HttpResponseMessage>[] responses) =>
        On(method, path, _ => true, responses);

    /// <summary>Adds a route after the existing ones, matching only when <paramref name="when"/> holds.</summary>
    public StubHttpHandler On(HttpMethod method, string path, Func<StubRequest, bool> when, params Func<StubRequest, HttpResponseMessage>[] responses)
    {
        lock (_gate)
        {
            _routes.Add(new Route(method, path, when, responses));
        }

        return this;
    }

    /// <summary>
    /// Adds a route ahead of the existing ones, so a test can replace what the fake Jira would answer
    /// (a rate-limited response, a malformed body) for the requests <paramref name="when"/> selects.
    /// </summary>
    public StubHttpHandler Override(HttpMethod method, string path, Func<StubRequest, bool> when, params Func<StubRequest, HttpResponseMessage>[] responses)
    {
        lock (_gate)
        {
            _routes.Insert(0, new Route(method, path, when, responses));
        }

        return this;
    }

    public static Func<StubRequest, HttpResponseMessage> Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        _ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>
    /// A rate-limited answer (429) as research note 05 describes it. <paramref name="retryAfterSeconds"/> is the
    /// <c>Retry-After</c> header in its seconds form; null leaves the header out. The body is one a careless
    /// tool might quote.
    /// </summary>
    public static Func<StubRequest, HttpResponseMessage> RateLimited(int? retryAfterSeconds) => _ =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"errorMessages":["Synthetic rate limit message quoting Fake Person One"],"errors":{}}""", Encoding.UTF8, "application/json"),
        };
        if (retryAfterSeconds is { } seconds)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        }

        return response;
    };

    /// <summary>A rate-limited answer (429) whose <c>Retry-After</c> header is in its other form, an HTTP date.</summary>
    public static Func<StubRequest, HttpResponseMessage> RateLimitedUntil(DateTimeOffset retryAfter) => _ =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
        return response;
    };

    /// <summary>
    /// Answers the next requests to <paramref name="path"/> with <paramref name="answers"/>, one each, ahead of
    /// every existing route; requests after those go to the routes that were there before.
    /// </summary>
    public StubHttpHandler AnswerNext(HttpMethod method, string path, params Func<StubRequest, HttpResponseMessage>[] answers)
    {
        // The route is matched under the handler's lock, once per request, so the count needs no lock of its own.
        var left = answers.Length;
        return Override(method, path, _ => left-- > 0, answers);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var parsed = HttpUtility.ParseQueryString(request.RequestUri!.Query);
        var query = parsed.AllKeys.Where(key => key is not null).ToDictionary(key => key!, key => parsed[key]!);
        var stub = new StubRequest(request.Method, request.RequestUri.AbsolutePath, query, body, request.Headers);

        Func<StubRequest, HttpResponseMessage> respond;
        lock (_gate)
        {
            _requests.Add(stub);
            var route = _routes.FirstOrDefault(r => r.Method == stub.Method && r.Path == stub.Path && r.When(stub))
                ?? throw new InvalidOperationException($"StubHttpHandler: no route for {stub.Method} {request.RequestUri.PathAndQuery} body={body}");
            respond = route.Next();
        }

        var response = respond(stub);
        response.RequestMessage = request;
        return response;
    }

    private sealed class Route(HttpMethod method, string path, Func<StubRequest, bool> when, Func<StubRequest, HttpResponseMessage>[] responses)
    {
        private int _calls;

        public HttpMethod Method => method;

        public string Path => path;

        public Func<StubRequest, bool> When => when;

        public Func<StubRequest, HttpResponseMessage> Next() => responses[Math.Min(_calls++, responses.Length - 1)];
    }
}
