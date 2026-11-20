namespace Telltale.Core.Configuration;

/// <summary>
/// Where pull requests are read from on GitHub: the <c>github</c> setting. The setting may be left out: then
/// no request is made to GitHub, no GitHub token is asked for, and reports have no pull request section.
/// The token is a secret and comes from the secret store, never from this file.
/// </summary>
public sealed record GitHubConfig
{
    /// <summary>
    /// The repositories whose pull requests are read, each as <c>owner/name</c>. An explicit list: nothing is
    /// read from a repository that is not on it. Every pull request of a listed repository is read, whoever
    /// opened it, so the figures are of the configured repositories and not of a team.
    /// </summary>
    public required IReadOnlyList<string> Repositories { get; init; }

    /// <summary>
    /// The team whose members' reviews are not code reviews (the Ops group), as <c>organization/team-slug</c>.
    /// Its members are read at every sync, and a review is classified by the membership at the sync that
    /// first stored it. May be left out: then no review is set apart as the Ops group's.
    /// </summary>
    public string? OpsTeam { get; init; }

    /// <summary>
    /// The address of the REST API, e.g. <c>https://github.example.com/api/v3/</c> for GitHub Enterprise
    /// Server. Left out, it is <see cref="GitHubSettings.PublicApiUrl"/>.
    /// </summary>
    public Uri? ApiUrl { get; init; }
}

/// <summary>The <c>github</c> setting, checked. Made only by <see cref="Checked"/>.</summary>
public sealed record GitHubSettings
{
    /// <summary>The API of github.com.</summary>
    public static readonly Uri PublicApiUrl = new("https://api.github.com/");

    private GitHubSettings()
    {
    }

    /// <summary>The address every request is relative to. It ends in a slash.</summary>
    public required Uri ApiUrl { get; init; }

    /// <summary>The repositories read, each once, in order.</summary>
    public required IReadOnlyList<GitHubRepository> Repositories { get; init; }

    /// <summary>The Ops group's team, or null when none is configured.</summary>
    public required GitHubTeam? OpsTeam { get; init; }

    /// <summary>
    /// Checks the setting and throws <see cref="SettingRefusedException"/> for the first part that is refused.
    /// No message repeats a refused value (owner decision D43).
    /// </summary>
    public static GitHubSettings Checked(GitHubConfig github)
    {
        var apiUrl = github.ApiUrl ?? PublicApiUrl;
        if (SettingRules.SiteUrlProblem(apiUrl) is { } apiUrlProblem)
        {
            throw new SettingRefusedException("github.apiUrl" + apiUrlProblem);
        }

        if (github.Repositories is not { Count: > 0 })
        {
            throw new SettingRefusedException("github.repositories must list at least one repository as owner/name.");
        }

        var repositories = new List<GitHubRepository>();
        foreach (var listed in github.Repositories)
        {
            if (TwoNames(listed) is not (var owner, var name))
            {
                throw new SettingRefusedException("github.repositories: every entry must be a repository written as owner/name, and one is not.");
            }

            // GitHub compares names without regard to case, so the same repository written twice is one.
            if (!repositories.Any(repository => string.Equals(repository.FullName, $"{owner}/{name}", StringComparison.OrdinalIgnoreCase)))
            {
                repositories.Add(new GitHubRepository(owner, name));
            }
        }

        GitHubTeam? opsTeam = null;
        if (github.OpsTeam is not null)
        {
            opsTeam = TwoNames(github.OpsTeam) is (var organization, var slug)
                ? new GitHubTeam(organization, slug)
                : throw new SettingRefusedException("github.opsTeam must be a team written as organization/team-slug.");
        }

        return new GitHubSettings { ApiUrl = SettingRules.WithTrailingSlash(apiUrl), Repositories = repositories, OpsTeam = opsTeam };
    }

    /// <summary>
    /// The two names of <c>first/second</c>, or null when the text is not that. A name holds only what GitHub
    /// allows in an owner, repository or team name (letters, digits, <c>-</c>, <c>_</c>, <c>.</c>), so it can
    /// be written into a request's path as it is.
    /// </summary>
    private static (string First, string Second)? TwoNames(string? written)
    {
        var parts = (written ?? "").Trim().Split('/');
        return parts.Length == 2 && parts.All(IsName) ? (parts[0], parts[1]) : null;
    }

    private static bool IsName(string name) =>
        name.Length > 0 && name is not ("." or "..") && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}

/// <summary>A repository on GitHub.</summary>
public sealed record GitHubRepository(string Owner, string Name)
{
    /// <summary><c>owner/name</c>: how the configuration writes it and the store keeps it.</summary>
    public string FullName => $"{Owner}/{Name}";

    /// <summary>The name the store keeps the repository's rows under: lower case, because GitHub compares names without regard to case.</summary>
    public string Key => FullName.ToLowerInvariant();
}

/// <summary>A team of an organization on GitHub, by its slug.</summary>
public sealed record GitHubTeam(string Organization, string Slug);
