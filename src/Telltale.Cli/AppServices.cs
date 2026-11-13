using System.Net;
using Telltale.Core.Secrets;
using Telltale.Storage.Sqlite;

namespace Telltale.Cli;

/// <summary>
/// Everything the commands take from the outside world. Production builds it once in
/// <see cref="ForProduction"/>; each test builds its own, so nothing is static or shared.
/// </summary>
/// <param name="HttpHandler">Carries every HTTP call.</param>
/// <param name="Clock">The source of "now", read by derive as the as-of time.</param>
/// <param name="Secrets">Where the Jira email, the API token and the hashing salt are kept.</param>
/// <param name="Out">Standard output. Commands never write to the console directly.</param>
/// <param name="Error">Standard error.</param>
/// <param name="ConfigPath">The configuration file (no secrets).</param>
/// <param name="DatabasePath">The SQLite store.</param>
/// <param name="ReportDirectory">Where reports are written, one file per sprint.</param>
public sealed record AppServices(
    HttpMessageHandler HttpHandler,
    TimeProvider Clock,
    ISecretStore Secrets,
    TextWriter Out,
    TextWriter Error,
    string ConfigPath,
    string DatabasePath,
    string ReportDirectory)
{
    /// <summary>The keyboard and standard input, where <c>secrets set</c> reads a secret's value from.</summary>
    public IConsoleInput ConsoleInput { get; init; } = new SystemConsoleInput();

    /// <summary>
    /// Whether the store holds at least one hashed person. Once it does, the hashing salt cannot be chosen
    /// freely: every command that asks for, sets or replaces the salt asks this first, here.
    /// </summary>
    public Task<bool> StoreHoldsHashedPeopleAsync(CancellationToken cancellationToken) =>
        SqliteStore.HoldsHashedPeopleAsync(DatabasePath, cancellationToken);

    /// <summary>The real thing, with files under the current directory.</summary>
    public static AppServices ForProduction() => new(
        // SocketsHttpHandler uses the system proxy and the operating system's certificate store by default,
        // which is where the corporate proxy and root certificate are configured. An authenticating proxy
        // is offered the signed-in user's credentials; without them it answers 407 to every request.
        new SocketsHttpHandler { DefaultProxyCredentials = CredentialCache.DefaultCredentials },
        TimeProvider.System,
        SecretStores.ForPlatform(OperatingSystem.IsWindows()),
        Console.Out,
        Console.Error,
        ConfigPath: Path.Combine("config", "telltale.json"),
        DatabasePath: Path.Combine("data", "telltale.db"),
        ReportDirectory: "reports");
}
