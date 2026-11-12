namespace Telltale.Core.Storage;

/// <summary>
/// The store's three layers (ADR-0002): raw rows written by collectors, derived rows rebuilt from
/// them, and metric views, the only thing reports and ad hoc SQL read.
/// </summary>
public interface ITelltaleStore
{
    /// <summary>Creates or upgrades the raw tables. Safe to call on every start.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a batch of raw rows in one transaction, replacing rows with the same Jira IDs, deleting the
    /// issue row and the change rows of every issue in <see cref="RawData.OutOfScopeIssueIds"/>, and setting the
    /// collector's watermark to <paramref name="reached"/>: the watermark advances only when the whole batch
    /// is written, and no batch is written without saying how far it reaches.
    /// The batch must be hashed with the salt the store's people were hashed with
    /// (<see cref="ReadSaltFingerprintAsync"/>): the rows it does not touch keep their hashes. A batch under
    /// another salt throws <see cref="UserFacingException"/> and nothing is written; only
    /// <see cref="ReplaceRawAsync"/> takes one.
    /// </summary>
    Task WriteRawAsync(RawData batch, Watermark reached, CancellationToken cancellationToken = default);

    /// <summary>
    /// Destructive: removes every issue, change and sprint the store holds and writes <paramref name="everything"/>
    /// in their place, with the watermark, in one transaction. Afterwards the store holds no issue, change or
    /// sprint that is not in <paramref name="everything"/>; if the write fails, it holds what it held before.
    /// Only for a batch that is all the source holds (a backfill; owner decision D4). Statuses are reference
    /// data (an ID's name) and are added and updated, never removed: stored history can name a status the
    /// source no longer lists.
    /// </summary>
    Task ReplaceRawAsync(RawData everything, Watermark reached, CancellationToken cancellationToken = default);

    /// <summary>How many issues, sprints and changes the raw tables hold.</summary>
    Task<RawCounts> CountRawAsync(CancellationToken cancellationToken = default);

    /// <summary>The watermark <paramref name="collector"/> last wrote, or null when it has never written a batch.</summary>
    Task<Watermark?> ReadWatermarkAsync(string collector, CancellationToken cancellationToken = default);

    /// <summary>
    /// The fingerprint of the salt the stored person hashes were made with (<see cref="RawData.SaltFingerprint"/>
    /// of the batches written), or null when the store holds no hashed person. A store that holds people
    /// without saying which salt hashed them gives a value that equals no fingerprint.
    /// </summary>
    Task<string?> ReadSaltFingerprintAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A digest that identifies the content of the raw tables, or null when no batch was ever written. The
    /// store writes it with every batch, so reading it is one row: equal raw data gives an equal digest,
    /// however it was written. A rebuild records it (<see cref="DerivedSource.RawDigest"/>).
    /// </summary>
    Task<string?> ReadRawDigestAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads every raw row.</summary>
    Task<RawData> ReadRawAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the derived tables and the metric views, and rebuilds them around <paramref name="derived"/>.
    /// <paramref name="source"/> says what it was derived from (see <see cref="ReadDerivedSourceAsync"/>).
    /// </summary>
    Task ReplaceDerivedAsync(DerivedData derived, DerivedSource source, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the last <see cref="ReplaceDerivedAsync"/> was given, or null when the derived tables and
    /// metric views have never been built.
    /// </summary>
    Task<DerivedSource?> ReadDerivedSourceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The changelog anomalies the last <see cref="ReplaceDerivedAsync"/> stored, as issue ID and message.
    /// Empty when the derived tables have never held any.
    /// </summary>
    Task<IReadOnlySet<(string IssueId, string Message)>> ReadAnomaliesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a query over metric views, and only over them: a query that reads a raw or derived table, or
    /// anything else in the store, throws <see cref="InvalidOperationException"/>. <typeparamref name="T"/> is a class with <c>init</c> properties
    /// named like the columns (<c>delivered_count</c> maps to <c>DeliveredCount</c>); counts are <c>long</c>.
    /// A figure a view withholds (ADR-0003) is null, so map figures to nullable properties.
    /// </summary>
    Task<IReadOnlyList<T>> QueryViewsAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default);
}
