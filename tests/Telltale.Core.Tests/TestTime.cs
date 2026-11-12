using Telltale.Core.Calendar;
using Telltale.Tests.Shared;

namespace Telltale.Core.Tests;

/// <summary>The team's business calendar, in the zone both test projects share (<see cref="TeamTime"/>).</summary>
internal static class TestTime
{
    public static BusinessCalendar TeamCalendar(params DateOnly[] holidays) => new(TeamTime.Zone, holidays);
}
