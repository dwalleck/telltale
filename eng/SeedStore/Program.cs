// Developer tool: writes a synthetic configuration file and store into a working directory, so that a published
// telltale executable can be tried (derive, report, query) without a Jira site. Everything it writes comes from
// the tests' fake Jira: FAKE- keys, fake people, fake statuses. See the README's Development section.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;

const string Usage =
    """
    Usage: seed-store <directory> [--force] [--minimum-group-size <team>,<assignees behind a figure>]
                      [--brief-visit-threshold <status>=<minutes>]...

      <directory>              The working directory to seed. It must be empty or not exist, and outside any
                               checkout of the Telltale repository.
      --force                  Seed a directory that is not empty: config/telltale.json and the store in data/
                               are replaced, and every other file is left as it is.
      --minimum-group-size     Written as minimumGroupSize, for example 0,0. Left out, the tool's defaults apply.
      --brief-visit-threshold  Written under briefVisitThresholds, for example development=5. May be repeated.
                               A status is its ID or its name without "Fake".
    """;

string? directory = null;
var force = false;
JsonObject? minimumGroupSize = null;
JsonObject? briefVisitThresholds = null;

for (var index = 0; index < args.Length; index++)
{
    var arg = args[index];
    if (arg is "--help" or "-h")
    {
        Console.Out.WriteLine(Usage);
        Console.Out.WriteLine();
        Console.Out.WriteLine(StatusList());
        return 0;
    }

    if (arg == "--force")
    {
        force = true;
    }
    else if (arg is "--minimum-group-size" or "--brief-visit-threshold")
    {
        if (++index == args.Length)
        {
            return Refuse($"{arg} needs a value.");
        }

        var value = args[index];
        if (arg == "--minimum-group-size")
        {
            var numbers = value.Split(',');
            if (numbers.Length != 2
                || !int.TryParse(numbers[0], NumberStyles.None, CultureInfo.InvariantCulture, out var team)
                || !int.TryParse(numbers[1], NumberStyles.None, CultureInfo.InvariantCulture, out var behindAFigure))
            {
                return Refuse($"--minimum-group-size takes two whole numbers of zero or more, as in 5,3; got '{value}'.");
            }

            minimumGroupSize = Scenarios.MinimumGroupSize(team, behindAFigure);
        }
        else
        {
            var (name, minutes) = value.Split('=') is [var left, var right] ? (left, right) : (value, "");
            var status = Workflow.BoardOrder.FirstOrDefault(status =>
                status.Id == name || string.Equals(status.DefaultName, $"Fake {name}", StringComparison.OrdinalIgnoreCase));
            if (status is null)
            {
                return Refuse($"--brief-visit-threshold takes <status>=<minutes>, as in development=5; '{name}' is not a status of the synthetic board.\n{StatusList()}");
            }

            // The number is written as it was typed (0.5, 1e-9), so the tool's own rules for a threshold decide.
            if (!IsJsonNumber(minutes, out var threshold))
            {
                return Refuse($"--brief-visit-threshold takes <status>=<minutes>, as in development=5; '{minutes}' is not a number.");
            }

            briefVisitThresholds ??= [];
            if (briefVisitThresholds.ContainsKey(status.Id))
            {
                return Refuse($"--brief-visit-threshold lists {status.DefaultName} twice.");
            }

            briefVisitThresholds[status.Id] = threshold;
        }
    }
    else if (arg.StartsWith('-') || directory is not null)
    {
        return Refuse($"'{arg}' is not an argument seed-store knows.");
    }
    else
    {
        directory = arg;
    }
}

if (directory is null)
{
    return Refuse("Name the directory to seed.");
}

directory = Path.GetFullPath(directory);
if (CheckoutHolding(directory) is { } checkout)
{
    return Refuse($"{directory} is inside a checkout of the Telltale repository ({checkout}). Seed a directory outside it, so that a store cannot be committed by accident.", withUsage: false);
}

if (File.Exists(directory))
{
    return Refuse($"{directory} is a file.", withUsage: false);
}

if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
{
    if (!force)
    {
        return Refuse($"{directory} is not empty. Name an empty or a new directory, or add --force to replace the configuration file and the store in it.", withUsage: false);
    }

    RemoveSeed(directory);
}

var exit = await SeededStore.WriteAsync(directory, minimumGroupSize, briefVisitThresholds, TextWriter.Null, Console.Error);
if (exit != 0)
{
    // Nothing half-seeded is left behind, so the same command line can be corrected and run again.
    RemoveSeed(directory);
    Console.Error.WriteLine("The store was not seeded: telltale refused the settings given, and what was written has been removed.");
    return exit;
}

Console.Out.WriteLine(
    $"""
    Seeded {directory} with synthetic data only: sprints {SeededStore.BaselineSprintId} (the baseline) and {SeededStore.ReportedSprintId}, a team of six fake people.
      config/telltale.json
      data/telltale.db

    Next, with the published telltale executable:
      cd "{directory}"
      telltale derive
      telltale report --sprint {SeededStore.ReportedSprintId}
      telltale query "SELECT * FROM delivered_per_sprint"

    After editing config/telltale.json, run derive again. backfill, sync, run and discover call Jira, and
    there is no Jira site behind this store.
    """);
return 0;

static int Refuse(string message, bool withUsage = true)
{
    Console.Error.WriteLine(message);
    if (withUsage)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(Usage);
    }

    return 1;
}

// Deletes what a seeding writes, and the directories it made if that leaves them empty. Other files stay.
static void RemoveSeed(string directory)
{
    var config = Path.Combine(directory, "config");
    var data = Path.Combine(directory, "data");
    var written = new[] { (In: config, Named: "telltale.json"), (In: data, Named: "telltale.db*") };
    foreach (var file in written.Where(files => Directory.Exists(files.In)).SelectMany(files => Directory.EnumerateFiles(files.In, files.Named)))
    {
        File.Delete(file);
    }

    foreach (var made in new[] { config, data, directory })
    {
        if (Directory.Exists(made) && !Directory.EnumerateFileSystemEntries(made).Any())
        {
            Directory.Delete(made);
        }
    }
}

static string StatusList() =>
    "Statuses: " + string.Join(", ", Workflow.BoardOrder.Select(status => $"{status.DefaultName["Fake ".Length..].ToLowerInvariant()} ({status.Id}, {status.Role})"));

static bool IsJsonNumber(string text, out JsonNode? number)
{
    number = null;
    try
    {
        number = JsonNode.Parse(text);
    }
    catch (JsonException)
    {
        return false;
    }

    return number?.GetValueKind() == JsonValueKind.Number;
}

// The root of the Telltale checkout (the main one or a git worktree of it) that the directory is in, if any:
// the nearest directory at or above it that holds both a .git entry and the solution file.
static string? CheckoutHolding(string directory)
{
    for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
    {
        var git = Path.Combine(current.FullName, ".git");
        if ((Directory.Exists(git) || File.Exists(git)) && File.Exists(Path.Combine(current.FullName, "Telltale.slnx")))
        {
            return current.FullName;
        }
    }

    return null;
}
