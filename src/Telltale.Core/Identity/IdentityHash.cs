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

    /// <summary>
    /// A value that identifies the salt without holding it: the HMAC-SHA256 of a fixed text keyed by the salt,
    /// the same construction as a person's hash. The store keeps it beside the hashes, so a salt that is not
    /// the one they were made with is noticed. Getting the salt back from it means inverting the HMAC's key,
    /// which is as hard as getting it back from any person hash already in the store. The fixed text has
    /// spaces, which a Jira account ID never has, so the fingerprint equals no person's hash.
    /// </summary>
    public static string SaltFingerprint(string salt) => Of("telltale salt fingerprint", salt);

    /// <summary>
    /// Stops when the people in a store were hashed with another salt than <paramref name="salt"/>: counted
    /// under two salts, one person is two distinct assignees, and the minimum group size counts distinct
    /// assignees (ADR-0003). <paramref name="storedFingerprint"/> is what the store records (null: it holds nobody yet).
    /// </summary>
    public static void RequireSaltOf(string? storedFingerprint, string salt) => RequireSameSalt(storedFingerprint, SaltFingerprint(salt));

    /// <summary>
    /// <see cref="RequireSaltOf"/> for a salt known by its fingerprint: stops when
    /// <paramref name="fingerprint"/> is not the one the store records. This is the one comparison; the
    /// collector asks it before it fetches, and the store asks it again of every batch it adds to its rows.
    /// </summary>
    public static void RequireSameSalt(string? storedFingerprint, string? fingerprint)
    {
        if (storedFingerprint is not null && storedFingerprint != fingerprint)
        {
            throw new UserFacingException(
                "The hashing salt in the secret store is not the one the people in the database were hashed with, so one person would count as two. "
                + "Restore the original salt, or run backfill to hash everyone again with this one.");
        }
    }
}
