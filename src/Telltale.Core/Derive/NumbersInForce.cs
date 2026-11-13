using System.Globalization;
using Telltale.Core.Configuration;
using Telltale.Core.Storage;

namespace Telltale.Core.Derive;

/// <summary>
/// The minimum group size's numbers the derived data was built under, and how a sentence states one of them
/// (owner decision D67). The report and the console read them here, from the metric view every other view
/// reads them from, and never from the configuration: a sentence then describes the figures beside it.
/// </summary>
public static class NumbersInForce
{
    private static readonly string[] Words = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];

    /// <summary>The one row of the metric view <c>minimum_group_size</c>.</summary>
    public static async Task<MinimumGroupSize> ReadAsync(ITelltaleStore store, CancellationToken cancellationToken = default) =>
        (await store.QueryViewsAsync<MinimumGroupSize>("SELECT team, assignees_behind_a_figure FROM minimum_group_size;", cancellationToken: cancellationToken)).Single();

    /// <summary>A number as a sentence writes it: in words up to twelve ("five"), in digits above.</summary>
    public static string Number(int number) =>
        number >= 0 && number < Words.Length ? Words[number] : number.ToString(CultureInfo.InvariantCulture);

    /// <summary>"three assignees", "one assignee"; with <paramref name="kind"/>, "three distinct assignees".</summary>
    public static string Assignees(int number, string kind = "") =>
        $"{Number(number)} {kind}{(number == 1 ? "assignee" : "assignees")}";
}
