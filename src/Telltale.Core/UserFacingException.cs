namespace Telltale.Core;

/// <summary>
/// A failure the person running the tool can act on: a missing secret, a bad setting, Jira refusing
/// a request. The command line prints the message alone, with no stack trace. The message must hold
/// no person or issue data, because it goes to the console.
/// </summary>
public sealed class UserFacingException : Exception
{
    public UserFacingException(string message)
        : base(message)
    {
    }

    public UserFacingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
