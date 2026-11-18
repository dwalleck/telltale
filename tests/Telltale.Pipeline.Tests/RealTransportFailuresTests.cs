using Telltale.Pipeline.Tests.Harness;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// What .NET's HTTP handler raises for a connection that fails on the way, from real sockets. The Jira client
/// sorts failures by these categories, so a runtime that changed them would change which failures are tried again.
/// </summary>
public class RealTransportFailuresTests
{
    [Test]
    public async Task A_connection_reset_is_an_unknown_error_caused_by_an_io_failure()
    {
        // Not ConnectionError: that is a connection that could not be made.
        var failure = await RealTransportFailures.ConnectionResetAsync();

        await Assert.That(failure.HttpRequestError).IsEqualTo(HttpRequestError.Unknown);
        await Assert.That(failure.InnerException).IsAssignableTo<IOException>();
    }

    [Test]
    public async Task An_answer_cut_off_halfway_is_an_answer_that_ended_early()
    {
        var failure = await RealTransportFailures.ClosedHalfwayThroughTheAnswerAsync();

        await Assert.That(failure.HttpRequestError).IsEqualTo(HttpRequestError.ResponseEnded);
    }
}
