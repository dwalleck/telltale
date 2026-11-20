using Telltale.Core;

namespace Telltale.Collectors.Http;

/// <summary>
/// The client gave up on the host, or never got through to it: no connection, the proxy, TLS, no answer in
/// time, the retries or the time for failures used up, a rate limit, credentials the host does not take, an
/// answer that is not the host's JSON, a next page that leads off the host. None of these is about the one
/// thing that was asked for, so asking for the next thing would fail the same way: a read ends here.
/// <para>
/// A failure that is not of this type is about the request's own subject, and a caller may go on to the next
/// one: the host refused that one thing (<see cref="HostClient"/> gives a 403 or a 404 as a plain
/// <see cref="UserFacingException"/>), or the caller could not read the answer's shape or come to a list's end.
/// </para>
/// </summary>
internal sealed class HostGaveUpException : UserFacingException
{
    public HostGaveUpException(string message)
        : base(message)
    {
    }

    public HostGaveUpException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
