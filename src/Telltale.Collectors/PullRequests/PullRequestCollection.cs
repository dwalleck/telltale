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
    /// Fetches every pull request of the configured repositories, open or not, and the events that can end a
    /// wait, classified. <paramref name="stored"/> is what the store holds, for a collector that can tell from
    /// it what need not be asked again. Fails with a <see cref="UserFacingException"/> when the host cannot be
    /// read in full: an answer that is missing is never an empty one.
    /// </summary>
    Task<PullRequestBatch> FetchAsync(RawData stored, CancellationToken cancellationToken);
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
    public static async Task BringUpToDateAsync(IEnumerable<IPullRequestCollector> collectors, ITelltaleStore store, string saltFingerprint, CancellationToken cancellationToken)
    {
        foreach (var collector in collectors)
        {
            PullRequestBatch batch;
            try
            {
                batch = await collector.FetchAsync(await store.ReadRawAsync(cancellationToken), cancellationToken);
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
