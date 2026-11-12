using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>
/// The two sprints nearly every test is set in, defined once. A test that needs one of them to differ says
/// how at the call, by naming the date that differs; anything else about a sprint is <see cref="AddSprint"/>'s.
/// January 2026 for reading the dates: the 5th, 12th, 19th and 26th are Mondays.
/// </summary>
public sealed partial class FakeJira
{
    /// <summary>
    /// Sprint 41, completed: from Monday 5 January 2026, 09:00, planned to end on Monday the 19th at 09:00 and
    /// completed that day at 10:00.
    /// </summary>
    public FakeSprint AddSprint41(DateTimeOffset? start = null, DateTimeOffset? plannedEnd = null, DateTimeOffset? completed = null) =>
        AddSprint(41, "Fake Sprint 41", start ?? Local(2026, 1, 5, 9), plannedEnd ?? Local(2026, 1, 19, 9), completed ?? Local(2026, 1, 19, 10));

    /// <summary>
    /// Sprint 42, completed: from Monday 19 January 2026, 10:30 (half an hour after sprint 41 was completed),
    /// planned to end on Monday 2 February at 09:00 and completed that day at 09:30. With
    /// <paramref name="stillRunning"/> it is the active sprint and has no completion time.
    /// </summary>
    public FakeSprint AddSprint42(DateTimeOffset? start = null, DateTimeOffset? plannedEnd = null, DateTimeOffset? completed = null, bool stillRunning = false) =>
        AddSprint(42, "Fake Sprint 42", start ?? Local(2026, 1, 19, 10, 30), plannedEnd ?? Local(2026, 2, 2, 9), stillRunning ? null : completed ?? Local(2026, 2, 2, 9, 30));

    /// <summary><see cref="AddSprint41"/> and <see cref="AddSprint42"/> as they are, both completed.</summary>
    public void AddSprints41And42()
    {
        AddSprint41();
        AddSprint42();
    }
}
