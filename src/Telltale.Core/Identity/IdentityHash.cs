using System.Security.Cryptography;
using System.Text;

namespace Telltale.Core.Identity;

/// <summary>
/// Pseudonymizes a person at import (ADR-0003). The salt is the HMAC key and lives only in the
/// secret store, so a hash cannot be traced back to an account without it.
/// </summary>
public static class IdentityHash
{
    /// <summary>HMAC-SHA256 of the UTF-8 account ID keyed by the UTF-8 salt, as 64 lower-case hex characters.</summary>
    public static string Of(string accountId, string salt)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(salt);

        return Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(salt), Encoding.UTF8.GetBytes(accountId)));
    }
}
