namespace Telltale.Core.Configuration;

/// <summary>
/// Where pull requests are read from on Azure DevOps: the <c>ado</c> setting. The setting may be left out:
/// then no request is made to Azure DevOps and no Azure DevOps token is asked for. It stands by itself: an
/// installation with this setting and no <c>github</c> setting reads Azure DevOps only.
/// The token is a secret and comes from the secret store, never from this file.
/// </summary>
public sealed record AdoConfig
{
    /// <summary>The organization (on Azure DevOps Server, the collection) the projects belong to.</summary>
    public required string Organization { get; init; }

    /// <summary>
    /// The repositories whose pull requests are read, each as <c>project/repository</c>. An explicit list:
    /// nothing is read from a repository that is not on it. Every pull request of a listed repository is read,
    /// whoever opened it, so the figures are of the configured repositories and not of a team.
    /// </summary>
    public required IReadOnlyList<string> Repositories { get; init; }

    /// <summary>
    /// The identity IDs (GUIDs) of accounts that act only as automation, e.g. a code scanner's account: every
    /// comment and vote of such an account is automation, whatever it says. An ID and not a name, because a
    /// name can change and an ID does not. A comment or vote is classified by this list at the sync that first
    /// stores it. May be left out: then no account is set apart as automation.
    /// </summary>
    public IReadOnlyList<string> AutomationIdentities { get; init; } = [];

    /// <summary>
    /// The address the organization is under. Left out, it is <see cref="AdoSettings.ServicesUrl"/>; an
    /// Azure DevOps Server has its own, e.g. <c>https://ado.example.com/tfs/</c>.
    /// </summary>
    public Uri? ApiUrl { get; init; }

    /// <summary>
    /// A date, e.g. <c>"2026-07-01"</c>: a pull request that was closed (completed or abandoned) before the
    /// start of that day (UTC) is not collected at all. It bounds how much the first sync asks for. Azure
    /// DevOps's list does not say when a pull request last changed, so a pull request that is still open is
    /// collected whenever it was opened. Left out, there is no bound.
    /// </summary>
    public DateOnly? CollectFrom { get; init; }
}

/// <summary>The <c>ado</c> setting, checked. Made only by <see cref="Checked"/>.</summary>
public sealed record AdoSettings
{
    /// <summary>Azure DevOps Services.</summary>
    public static readonly Uri ServicesUrl = new("https://dev.azure.com/");

    private AdoSettings()
    {
    }

    /// <summary>The address every request is relative to: the organization's. It ends in a slash.</summary>
    public required Uri OrganizationUrl { get; init; }

    /// <summary>The repositories read, each once, in order.</summary>
    public required IReadOnlyList<AdoRepository> Repositories { get; init; }

    /// <summary>The identity IDs of the accounts that are automation, each in the one form IDs are compared in (<see cref="IdentityId"/>).</summary>
    public required IReadOnlySet<string> AutomationIdentities { get; init; }

    /// <summary>The moment before which a pull request's closing puts it out of what is collected, or null for no bound.</summary>
    public required DateTimeOffset? CollectFrom { get; init; }

    /// <summary>
    /// The form identity IDs are compared in: the GUID in lower case with hyphens. Null for text that is not a
    /// GUID, which is no identity ID of Azure DevOps.
    /// </summary>
    public static string? IdentityId(string? written) =>
        Guid.TryParse(written?.Trim(), out var id) ? id.ToString("D") : null;

    /// <summary>
    /// Checks the setting and throws <see cref="SettingRefusedException"/> for the first part that is refused.
    /// No message repeats a refused value (owner decision D43).
    /// </summary>
    public static AdoSettings Checked(AdoConfig ado)
    {
        var apiUrl = ado.ApiUrl ?? ServicesUrl;
        if (SettingRules.SiteUrlProblem(apiUrl) is { } apiUrlProblem)
        {
            throw new SettingRefusedException("ado.apiUrl" + apiUrlProblem);
        }

        if (Name(ado.Organization) is not { } organization)
        {
            throw new SettingRefusedException("ado.organization must be the organization's name as it stands in its address.");
        }

        if (ado.Repositories is not { Count: > 0 })
        {
            throw new SettingRefusedException("ado.repositories must list at least one repository as project/repository.");
        }

        var repositories = new List<AdoRepository>();
        foreach (var listed in ado.Repositories)
        {
            var parts = (listed ?? "").Split('/');
            if (parts.Length != 2 || Name(parts[0]) is not { } project || Name(parts[1]) is not { } name)
            {
                throw new SettingRefusedException("ado.repositories: every entry must be a repository written as project/repository, and one is not.");
            }

            // Azure DevOps compares names without regard to case, so the same repository written twice is one.
            var repository = new AdoRepository(project, name);
            if (!repositories.Any(already => already.Key == repository.Key))
            {
                repositories.Add(repository);
            }
        }

        var automation = new HashSet<string>(StringComparer.Ordinal);
        foreach (var listed in ado.AutomationIdentities)
        {
            automation.Add(IdentityId(listed) ?? throw new SettingRefusedException("ado.automationIdentities: every entry must be an identity ID (a GUID), and one is not."));
        }

        return new AdoSettings
        {
            OrganizationUrl = new Uri(SettingRules.WithTrailingSlash(apiUrl), Uri.EscapeDataString(organization) + "/"),
            Repositories = repositories,
            AutomationIdentities = automation,
            CollectFrom = ado.CollectFrom is { } day ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
        };
    }

    /// <summary>
    /// The name without surrounding white space, or null when the text is no name of an organization, project
    /// or repository: empty, a path step, or holding a character that would end or split the path it is
    /// written into. A name can hold a space; it is escaped where the path is built.
    /// </summary>
    private static string? Name(string? written)
    {
        var name = (written ?? "").Trim();
        return name.Length > 0 && name is not ("." or "..") && !name.Any(character => char.IsControl(character) || character is '/' or '\\' or '?' or '#' or '%')
            ? name
            : null;
    }
}

/// <summary>A repository of a project on Azure DevOps.</summary>
public sealed record AdoRepository(string Project, string Name)
{
    /// <summary>The name the store keeps the repository's rows under: <c>project/repository</c> in lower case, because Azure DevOps compares names without regard to case.</summary>
    public string Key => $"{Project}/{Name}".ToLowerInvariant();

    /// <summary>The path of the repository's part of the Git API, relative to the organization's address, each name escaped.</summary>
    public string ApiPath => $"{Uri.EscapeDataString(Project)}/_apis/git/repositories/{Uri.EscapeDataString(Name)}";
}
