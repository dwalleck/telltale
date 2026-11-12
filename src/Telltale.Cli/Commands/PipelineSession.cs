using Telltale.Core;
using Telltale.Core.Secrets;
using Telltale.Pipeline;
using Telltale.Storage.Sqlite;

namespace Telltale.Cli.Commands;

/// <summary>
/// One command's use of the pipeline: the wiring between the command line's services and
/// <see cref="PipelineSteps"/>. It loads the configuration file, reads the secrets, takes the store for this
/// command alone (<see cref="StoreLock"/>) and opens it. Every rule about what a step may do is the pipeline's.
/// Dispose it to release the store.
/// </summary>
internal sealed class PipelineSession : IDisposable
{
    private readonly IDisposable _storeLock;

    private PipelineSession(PipelineSteps steps, IDisposable storeLock)
    {
        Steps = steps;
        _storeLock = storeLock;
    }

    public PipelineSteps Steps { get; }

    /// <summary>
    /// With <paramref name="needsJira"/> (the command will fetch), the secrets are read first, so a command
    /// that cannot start leaves no empty database behind, and the database is created if it is missing.
    /// Without it the command only works on what the store holds, so a missing database is refused, not created.
    /// </summary>
    public static async Task<PipelineSession> StartAsync(AppServices services, bool needsJira, CancellationToken cancellationToken)
    {
        var settings = await ConfigFile.LoadAsync(services.ConfigPath, cancellationToken);

        CollectionSecrets? secrets = null;
        if (needsJira)
        {
            secrets = await CollectionSecrets.ReadAsync(services.Secrets, services.StoreHoldsHashedPeopleAsync, cancellationToken);
        }
        else if (!File.Exists(services.DatabasePath))
        {
            throw new UserFacingException($"There is no database at {services.DatabasePath} yet. Run backfill first.");
        }

        var storeLock = StoreLock.Acquire(services.DatabasePath);
        try
        {
            var store = new SqliteStore(services.DatabasePath);
            await store.InitializeAsync(cancellationToken);
            var environment = new PipelineEnvironment(services.HttpHandler, services.Clock, services.Secrets, services.Out, services.Error, services.ReportDirectory);
            return new PipelineSession(new PipelineSteps(environment, settings, store, secrets), storeLock);
        }
        catch
        {
            storeLock.Dispose();
            throw;
        }
    }

    public void Dispose() => _storeLock.Dispose();
}
