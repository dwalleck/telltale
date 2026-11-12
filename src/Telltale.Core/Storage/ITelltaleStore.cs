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
    /// Writes a batch of raw rows in one transaction, replacing rows with the same Jira IDs, and deletes the
    /// issue row and the change rows of every issue in <see cref="RawData.OutOfScopeIssueIds"/>.
    /// </summary>
    Task WriteRawAsync(RawData batch, CancellationToken cancellationToken = default);

    /// <summary>Reads every raw row.</summary>
    Task<RawData> ReadRawAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops the derived tables and the metric views, and rebuilds them around <paramref name="derived"/>.</summary>
    Task ReplaceDerivedAsync(DerivedData derived, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a query over metric views. <typeparamref name="T"/> is a class with <c>init</c> properties
    /// named like the columns (<c>delivered_count</c> maps to <c>DeliveredCount</c>); counts are <c>long</c>.
    /// </summary>
    Task<IReadOnlyList<T>> QueryViewsAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default);
}
