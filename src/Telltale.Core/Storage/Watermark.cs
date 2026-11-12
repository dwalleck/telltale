namespace Telltale.Core.Storage;

/// <summary>
/// How far one collector has got. <see cref="Covers"/> describes what it was asked to fetch (for Jira: the
/// board, the projects and the start date); the watermark says nothing about anything else, so a collector
/// whose settings no longer match must not continue from it. <see cref="UpdatedThrough"/> is the newest
/// change fetched, in the source's own time (for Jira, an issue's <c>updated</c>), never this machine's
/// clock; null when nothing has been fetched yet.
/// </summary>
public sealed record Watermark(string Collector, string Covers, DateTimeOffset? UpdatedThrough);
