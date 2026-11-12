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
    /// Writes a batch in one transaction: its rows, the deletions it asks for (<see cref="RawBatch.OutOfScopeIssueIds"/>,
    /// or everything, with <see cref="RawBatch.ReplacesEverything"/>), the collector's watermark and the salt's
    /// fingerprint. The watermark advances only when the whole batch is written; if the write fails, the store
    /// holds what it held before. A batch that is added to the stored rows must be hashed with the salt they
    /// were hashed with (<see cref="ReadSaltFingerprintAsync"/>): one under another salt throws
    /// <see cref="UserFacingException"/> and nothing is written. Only a batch that replaces everything may
    /// bring another salt.
    /// </summary>
    Task WriteRawAsync(RawBatch batch, CancellationToken cancellationToken = default);

    /// <summary>How many issues, sprints and changes the raw tables hold.</summary>
    Task<RawCounts> CountRawAsync(CancellationToken cancellationToken = default);

    /// <summary>The watermark <paramref name="collector"/> last wrote, or null when it has never written a batch.</summary>
    Task<Watermark?> ReadWatermarkAsync(string collector, CancellationToken cancellationToken = default);

    /// <summary>
    /// The fingerprint of the salt the stored person hashes were made with (<see cref="RawBatch.SaltFingerprint"/>
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

    /// <summary>
    /// Runs one query of any shape over metric views, under the same guard as
    /// <see cref="QueryViewsAsync{T}"/>, and returns its column names and rows as they are. For SQL the owner
    /// wrote (owner decision D48). A query that reads anything else throws <see cref="InvalidOperationException"/>;
    /// SQL that cannot be run, or more than one statement, throws <see cref="UserFacingException"/>.
    /// </summary>
    Task<ViewRows> QueryViewRowsAsync(string sql, CancellationToken cancellationToken = default);
}

/// <summary>
/// The answer to a query over the metric views: the column names, and each row's values in column order. A
/// value is a <c>long</c>, a <c>double</c>, a <c>string</c>, or null where the view gives none, which is
/// where it withholds a figure.
/// </summary>
public sealed record ViewRows(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);
