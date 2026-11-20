namespace Telltale.Core.Secrets;

/// <summary>
/// The three secrets a collection needs, read from the secret store once, at the start of the command, and
/// without surrounding white space. Whatever runs afterwards takes them from here, so a secret that is missing
/// stops the command before anything is fetched, and the store is not asked twice.
/// </summary>
public sealed record CollectionSecrets(string JiraEmail, string JiraApiToken, string HashingSalt)
{
    /// <summary>
    /// The token of each pull request host that is configured. A host that is not configured has none and is
    /// asked for none (<see cref="WithGitHubToken"/>), so a Jira-only configuration needs no host's credentials.
    /// </summary>
    public string? GitHubToken { get; init; }

    /// <summary>
    /// These secrets with the GitHub token, read now and required: for a command that will read GitHub. A token
    /// that is not set stops the command before anything is fetched, naming the secret and how to set it, so
    /// missing access is never taken for a host without pull requests.
    /// </summary>
    public CollectionSecrets WithGitHubToken(ISecretStore secrets) => this with { GitHubToken = secrets.Require(SecretName.GitHubToken) };

    /// <summary>The Azure DevOps token, when that host is configured (<see cref="WithAdoToken"/>).</summary>
    public string? AdoToken { get; init; }

    /// <summary>These secrets with the Azure DevOps token, read now and required, as <see cref="WithGitHubToken"/> does for GitHub's.</summary>
    public CollectionSecrets WithAdoToken(ISecretStore secrets) => this with { AdoToken = secrets.Require(SecretName.AdoToken) };

    /// <summary>
    /// Reads the secrets in the order they are listed (<see cref="SecretInfo.All"/>) and stops with a message
    /// naming the first that is not set and how to set it. A missing hashing salt is two different situations:
    /// on a store that holds nobody yet, any salt will do and one can be generated; once people are stored
    /// under a salt, only that salt keeps each of them one person, until a backfill under a new one hashes
    /// them all again. <paramref name="storeHoldsHashedPeople"/> says which it is, and is asked only then.
    /// </summary>
    public static async Task<CollectionSecrets> ReadAsync(ISecretStore secrets, Func<CancellationToken, Task<bool>> storeHoldsHashedPeople, CancellationToken cancellationToken = default)
    {
        var jiraEmail = secrets.Require(SecretName.JiraEmail);
        var jiraApiToken = secrets.Require(SecretName.JiraApiToken);

        var salt = secrets.Get(SecretName.HashingSalt);
        if (!string.IsNullOrWhiteSpace(salt))
        {
            return new CollectionSecrets(jiraEmail, jiraApiToken, salt.Trim());
        }

        var howToSet = secrets.HowToSet(SecretName.HashingSalt);
        if (await storeHoldsHashedPeople(cancellationToken))
        {
            throw new UserFacingException(
                $"{secrets.NotSet(SecretName.HashingSalt)}, and the database already holds people hashed with the salt that was there before. "
                + "Restore the original salt, or set a new one and run backfill, which hashes everyone again with it; "
                + $"until that backfill every other command stops, because a second salt would make each person count as two. {howToSet}");
        }

        // Only a store 'secrets set' can write to can be given a generated salt.
        var generate = secrets is IWritableSecretStore ? " Add --generate to have a new random salt made." : string.Empty;
        throw new UserFacingException($"{secrets.NotSet(SecretName.HashingSalt)}. {howToSet}{generate}");
    }
}
