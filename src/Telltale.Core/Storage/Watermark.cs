namespace Telltale.Core.Storage;

/// <summary>
/// How far one collector has got: everything the source changed up to <see cref="UpdatedThrough"/> is in
/// the raw tables. The time is the source's own (for Jira, an issue's <c>updated</c>), never this machine's clock.
/// </summary>
public sealed record Watermark(string Collector, DateTimeOffset UpdatedThrough);
