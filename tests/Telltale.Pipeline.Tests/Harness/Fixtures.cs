namespace Telltale.Pipeline.Tests.Harness;

/// <summary>A status of the synthetic board. Tests refer to statuses by these values, never by name.</summary>
public sealed record FakeStatus(string Id, string DefaultName, string CategoryKey, string Role);

/// <summary>
/// The synthetic board's workflow, in board order, with the role each status has in the default
/// configuration. It mirrors the team's real workflow shape (system-design.md) with made-up IDs.
/// </summary>
public static class Workflow
{
    public static readonly FakeStatus New = new("600001", "Fake New", "new", "not started");
    public static readonly FakeStatus Approved = new("600002", "Fake Approved", "new", "not started");
    public static readonly FakeStatus Committed = new("600003", "Fake Committed", "new", "not started");
    public static readonly FakeStatus Development = new("600004", "Fake Development", "indeterminate", "work");
    public static readonly FakeStatus Review = new("600005", "Fake Review", "indeterminate", "work");
    public static readonly FakeStatus QE = new("600006", "Fake QE", "indeterminate", "work");
    public static readonly FakeStatus Blocked = new("600007", "Fake Blocked", "indeterminate", "blocked");
    public static readonly FakeStatus Done = new("600008", "Fake Done", "done", "done");
    public static readonly FakeStatus Shipped = new("600009", "Fake Shipped", "done", "done");

    public static IReadOnlyList<FakeStatus> BoardOrder { get; } = [New, Approved, Committed, Development, Review, QE, Blocked, Done, Shipped];
}

/// <summary>A synthetic Jira user. Nothing here may ever reach the database, a report or a log.</summary>
public sealed record FakePerson(string AccountId, string DisplayName, string Email);

public static class People
{
    public static readonly FakePerson One = new("fake-account-0001", "Fake Person One", "fake.one@example.invalid");
    public static readonly FakePerson Two = new("fake-account-0002", "Fake Person Two", "fake.two@example.invalid");
    public static readonly FakePerson Three = new("fake-account-0003", "Fake Person Three", "fake.three@example.invalid");
    public static readonly FakePerson Four = new("fake-account-0004", "Fake Person Four", "fake.four@example.invalid");
    public static readonly FakePerson Five = new("fake-account-0005", "Fake Person Five", "fake.five@example.invalid");
    public static readonly FakePerson Six = new("fake-account-0006", "Fake Person Six", "fake.six@example.invalid");

    /// <summary>Makes every status move unless a test names someone else, so a changelog author is never just the assignee.</summary>
    public static readonly FakePerson BoardAdmin = new("fake-account-0099", "Fake Board Admin", "fake.admin@example.invalid");

    /// <summary>Five people: the smallest team whose figures are shown (GLOSSARY: Minimum group size).</summary>
    public static IReadOnlyList<FakePerson> Team { get; } = [One, Two, Three, Four, Five];

    public static IReadOnlyList<FakePerson> All { get; } = [One, Two, Three, Four, Five, Six, BoardAdmin];
}
