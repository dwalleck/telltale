using Telltale.Core;
using Telltale.Core.Storage;

namespace Telltale.Collectors.PullRequests;

/// <summary>
/// Reads the pull requests of one host. A host's collector only fetches and classifies: it gives back a batch
/// and writes nothing, so a fetch that fails part-way stores no row (<see cref="PullRequestCollection"/> writes).
/// </summary>
public interface IPullRequestCollector
{
    /// <summary>The host's name in the store: one of <see cref="PullRequestHosts"/>.</summary>
    string Host { get; }

    /// <summary>
    /// Fetches what the store does not hold yet of the pull requests of the configured repositories, open or
    /// not: the rows that are new or differ from <paramref name="stored"/>, and the classified events of those
    /// whose list entry changed. A pull request the host lists exactly as it is stored has no event the store
    /// lacks, and costs no request of its own. With <paramref name="everything"/> (a backfill) the whole list
    /// is read; without it (a sync) a collector whose host lists the most recently updated first may stop
    /// where nothing has changed. Fails with a <see cref="UserFacingException"/> when the host cannot be read:
    /// an answer that is missing is never an empty one.
    /// </summary>
    Task<PullRequestBatch> FetchAsync(RawData stored, bool everything, CancellationToken cancellationToken);
}

/// <summary>Brings the store's pull requests up to date, host by host, the same way for a sync and a backfill.</summary>
public static class PullRequestCollection
{
    /// <summary>
    /// Reads each host in turn and writes its batch. A host that cannot be read stops the command, and the
    /// store then says so (<see cref="PullRequestHostStates.Failed"/>): its rows of earlier reads stay, and
    /// nothing reads them as a host that was read and found empty.
    /// </summary>
    /// <param name="saltFingerprint">The fingerprint of the salt the collectors hash authors with.</param>
    /// <param name="everything">Read each host's whole list (a backfill), not only as far as something changed (a sync).</param>
    public static async Task BringUpToDateAsync(IEnumerable<IPullRequestCollector> collectors, ITelltaleStore store, bool everything, string saltFingerprint, CancellationToken cancellationToken)
    {
        foreach (var collector in collectors)
        {
            PullRequestBatch batch;
            try
            {
                batch = await collector.FetchAsync(await store.ReadRawAsync(cancellationToken), everything, cancellationToken);
            }
            catch (UserFacingException)
            {
                await store.WritePullRequestsAsync(
                    new PullRequestBatch([], [], new RawPullRequestHost(collector.Host, PullRequestHostStates.Failed, PullRequestRosterStates.NotRead), saltFingerprint),
                    cancellationToken);
                throw;
            }

            await store.WritePullRequestsAsync(batch, cancellationToken);
        }
    }
}
