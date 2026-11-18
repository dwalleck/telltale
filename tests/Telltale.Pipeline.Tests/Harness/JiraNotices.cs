namespace Telltale.Pipeline.Tests.Harness;

/// <summary>What the Jira client writes to standard error while it tries a request again.</summary>
public static class JiraNotices
{
    /// <summary>The waits before the retries of a request that keeps failing in a way that can pass by itself.</summary>
    public static readonly IReadOnlyList<TimeSpan> WaitsOfAPassingFailure = [.. new[] { 5, 10, 20, 40, 80 }.Select(seconds => TimeSpan.FromSeconds(seconds))];

    /// <summary>The same for a request that keeps getting no answer in time, which is tried again fewer times.</summary>
    public static readonly IReadOnlyList<TimeSpan> WaitsAfterNoAnswer = [.. WaitsOfAPassingFailure.Take(3)];

    /// <summary>
    /// The lines before the failure that ends the run, when <paramref name="call"/> kept failing with
    /// <paramref name="failure"/>: one line for each retry, with the seconds waited before it.
    /// </summary>
    public static string EveryRetry(string failure, string call) => Lines(WaitsOfAPassingFailure, failure, call);

    /// <summary>The lines before the failure that ends the run, when <paramref name="call"/> kept getting no answer in time.</summary>
    public static string EveryRetryAfterNoAnswer(string call) => Lines(WaitsAfterNoAnswer, "Jira did not answer in time", call);

    /// <summary>
    /// The line that ends the run when a request still failed after its last retry. <paramref name="failure"/> is
    /// the sentence that says how it failed and names the request, as the line without retries would end.
    /// </summary>
    public static string AfterEveryRetry(string failure, int retries) => $"{failure} again after {retries} retries. Run the command again later.\n";

    /// <summary>The line that ends the run when another retry would take the command past its fifteen minutes on failures.</summary>
    public static string OutOfTime(string failure) =>
        $"{failure}, and waiting again would bring the time this command has spent on failed requests and on waits to more than 15 minutes. Run the command again later.\n";

    private static string Lines(IReadOnlyList<TimeSpan> waits, string failure, string call) =>
        string.Concat(waits.Select(wait => $"{failure}. Waiting {wait.TotalSeconds:0} seconds before asking again ({call}).\n"));
}
