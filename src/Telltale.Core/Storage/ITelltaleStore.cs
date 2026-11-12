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
    /// A batch hashed with another salt than the store's people (<see cref="RawData.SaltFingerprint"/>) must
    /// carry every stored issue that has an assignee; otherwise nothing is written and
    /// <see cref="UserFacingException"/> is thrown, since the rows left behind could not be hashed again.
    /// </summary>
    Task WriteRawAsync(RawData batch, Watermark reached, CancellationToken cancellationToken = default);

    /// <summary>The watermark <paramref name="collector"/> last wrote, or null when it has never written a batch.</summary>
    Task<Watermark?> ReadWatermarkAsync(string collector, CancellationToken cancellationToken = default);

    /// <summary>
    /// The fingerprint of the salt the stored person hashes were made with (<see cref="RawData.SaltFingerprint"/>
    /// of the batches written), or null when the store holds no hashed person. A store that holds people
    /// without saying which salt hashed them gives a value that equals no fingerprint.
    /// </summary>
    Task<string?> ReadSaltFingerprintAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads every raw row.</summary>
    Task<RawData> ReadRawAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the derived tables and the metric views, and rebuilds them around <paramref name="derived"/>.
    /// <paramref name="rawDigest"/> identifies the raw data it was derived from (see <see cref="ReadDerivedSourceAsync"/>).
    /// </summary>
    Task ReplaceDerivedAsync(DerivedData derived, string rawDigest, CancellationToken cancellationToken = default);

    /// <summary>
    /// The digest the last <see cref="ReplaceDerivedAsync"/> was given, or null when the derived tables and
    /// metric views have never been built.
    /// </summary>
    Task<string?> ReadDerivedSourceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a query over metric views, and only over them: a query that reads a raw or derived table, or
    /// anything else in the store, throws <see cref="InvalidOperationException"/>. <typeparamref name="T"/> is a class with <c>init</c> properties
    /// named like the columns (<c>delivered_count</c> maps to <c>DeliveredCount</c>); counts are <c>long</c>.
    /// A figure a view withholds (ADR-0003) is null, so map figures to nullable properties.
    /// </summary>
    Task<IReadOnlyList<T>> QueryViewsAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default);
}
