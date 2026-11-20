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
    /// an answer that is missing is never an empty one. A collector that had read some pull requests whole by
    /// then may fail with a <see cref="PullRequestReadStoppedException"/>, which carries them to be stored.
    /// </summary>
    Task<PullRequestBatch> FetchAsync(RawData stored, bool everything, CancellationToken cancellationToken);
}

/// <summary>
/// A read of a host that stopped part-way, with what it had read whole by then: pull requests each with every
/// one of its events, so that the next read need not ask for them again. A first read of a large repository
/// can take more requests than a host allows in an hour, and would otherwise start from nothing every time.
/// The read still failed: <see cref="Read"/> records the host as <see cref="PullRequestHostStates.Failed"/>,
/// so nothing takes what is stored for the whole of what the host has, and the message says what was kept.
/// </summary>
public sealed class PullRequestReadStoppedException : UserFacingException
{
    public PullRequestReadStoppedException(string message, PullRequestBatch read, Exception innerException)
        : base(message, innerException)
    {
        if (read.HostState.State != PullRequestHostStates.Failed)
        {
            throw new ArgumentException("What a stopped read gives to be stored must record the host as failed.", nameof(read));
        }

        Read = read;
    }

    /// <summary>What to store: the pull requests read whole, their events, and the host recorded as failed.</summary>
    public PullRequestBatch Read { get; }
}

/// <summary>Brings the store's pull requests up to date, host by host, the same way for a sync and a backfill.</summary>
public static class PullRequestCollection
{
    /// <summary>
    /// Reads each host in turn and writes its batch. A host that cannot be read fails the command, and the
    /// store then says so (<see cref="PullRequestHostStates.Failed"/>): its rows of earlier reads stay, with
    /// those the failed read had read whole when its collector gives them (<see cref="PullRequestReadStoppedException"/>),
    /// and nothing reads them as a host that was read and found empty. The hosts after it are still read first,
    /// so that what one host has does not go unread because another could not be reached; the command then
    /// fails with what each host that could not be read said.
    /// </summary>
    /// <param name="saltFingerprint">The fingerprint of the salt the collectors hash authors with.</param>
    /// <param name="everything">Read each host's whole list (a backfill), not only as far as something changed (a sync).</param>
    public static async Task BringUpToDateAsync(IEnumerable<IPullRequestCollector> collectors, ITelltaleStore store, bool everything, string saltFingerprint, CancellationToken cancellationToken)
    {
        var failures = new List<UserFacingException>();
        foreach (var collector in collectors)
        {
            PullRequestBatch batch;
            var stored = await store.ReadRawAsync(cancellationToken);
            try
            {
                batch = await collector.FetchAsync(stored, everything, cancellationToken);
            }
            catch (PullRequestReadStoppedException stopped)
            {
                // The read failed like any other, and what it read whole before that is kept.
                await store.WritePullRequestsAsync(stopped.Read, cancellationToken);
                failures.Add(stopped);
                continue;
            }
            catch (UserFacingException failure)
            {
                // The rows of earlier reads stay, and so does the start date they were collected under.
                await store.WritePullRequestsAsync(
                    new PullRequestBatch(
                        [],
                        [],
                        new RawPullRequestHost(collector.Host, PullRequestHostStates.Failed, PullRequestRosterStates.NotRead) { CollectFrom = CollectedFrom(stored, collector.Host) },
                        saltFingerprint),
                    cancellationToken);
                failures.Add(failure);
                continue;
            }

            await store.WritePullRequestsAsync(batch, cancellationToken);
        }

        ThrowWhatFailed(failures);
    }

    /// <summary>The start date the store records for the host's pull requests (<see cref="RawPullRequestHost.CollectFrom"/>); null when it records none, or holds nothing of the host.</summary>
    public static DateTimeOffset? CollectedFrom(RawData stored, string host) =>
        stored.PullRequestHosts.FirstOrDefault(known => known.Host == host)?.CollectFrom;

    private static void ThrowWhatFailed(List<UserFacingException> failures)
    {
        switch (failures)
        {
            case []:
                return;
            case [var only]:
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(only).Throw();
                return;
            default:
                throw new UserFacingException(string.Join(Environment.NewLine, failures.Select(failure => failure.Message)), new AggregateException(failures));
        }
    }
}
