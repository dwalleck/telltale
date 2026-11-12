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
    private readonly HttpClient _http;

    public JiraClient(HttpMessageHandler handler, Uri siteUrl, string email, string apiToken)
    {
        _http = new HttpClient(handler, disposeHandler: false) { BaseAddress = siteUrl };

        // Jira sends no authentication challenge, so the header goes on every request.
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{apiToken}")));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>GET a path relative to the site, e.g. <c>rest/api/3/status</c>.</summary>
    public async Task<JsonDocument> GetAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(relativeUrl, cancellationToken);
        return await ReadAsync(response, "GET", relativeUrl, cancellationToken);
    }

    /// <summary>POST a JSON body to a path relative to the site.</summary>
    public async Task<JsonDocument> PostAsync(string relativeUrl, JsonObject body, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(body);
        using var response = await _http.PostAsync(relativeUrl, content, cancellationToken);
        return await ReadAsync(response, "POST", relativeUrl, cancellationToken);
    }

    public void Dispose() => _http.Dispose();

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, string method, string relativeUrl, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            // The response body is left out on purpose: it can quote issue data.
            throw new UserFacingException($"Jira answered {(int)response.StatusCode} ({response.StatusCode}) to {method} {relativeUrl}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
