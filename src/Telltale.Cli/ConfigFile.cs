using System.Text.Json;
using System.Text.Json.Serialization;
using Telltale.Core;
using Telltale.Core.Configuration;

namespace Telltale.Cli;

/// <summary>Reads the configuration file: JSON with camelCase names. Comments and trailing commas are allowed.</summary>
public static class ConfigFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,

        // A setting the tool does not know is refused, not ignored. That catches typos, and it means
        // a secret pasted into the file stops the run instead of sitting there unnoticed.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,

        // A setting written as null is refused where the setting is required, with its name in the message.
        RespectNullableAnnotations = true,

        // A setting written twice is refused: the reader of the file could not tell which value is in force.
        AllowDuplicateProperties = false,
    };

    /// <summary>True for <c>customfield_</c> followed by digits only, the form of every Jira custom field ID.</summary>
    internal static bool IsCustomFieldId(string fieldId)
    {
        const string prefix = "customfield_";
        return fieldId.Length > prefix.Length
            && fieldId.StartsWith(prefix, StringComparison.Ordinal)
            && fieldId.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    public static async Task<TelltaleConfig> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new UserFacingException($"The configuration file {path} does not exist.");
        }

        await using var stream = File.OpenRead(path);
        return await ReadAsync(stream, path, cancellationToken);
    }

    /// <summary>
    /// Reads and checks the content of the file at <paramref name="path"/>, or content about to be written
    /// there: <c>discover</c> passes what it is about to write, so it never writes a file the run would refuse.
    /// </summary>
    public static async Task<TelltaleConfig> ReadAsync(Stream stream, string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await JsonSerializer.DeserializeAsync<TelltaleConfig>(stream, Options, cancellationToken)
                ?? throw new UserFacingException($"The configuration file {path} is empty.");

            // Basic authentication sends the Jira email and API token with every request, so only https will do.
            if (config.Jira.SiteUrl is not { IsAbsoluteUri: true } siteUrl || siteUrl.Scheme != Uri.UriSchemeHttps)
            {
                throw new UserFacingException($"The configuration file {path} is not valid: jira.siteUrl must be an absolute https:// address.");
            }

            // The file holds no secrets, and the address is printed in messages. The message does not repeat it.
            if (siteUrl.UserInfo.Length > 0)
            {
                throw new UserFacingException($"The configuration file {path} is not valid: jira.siteUrl must not carry a user name or password.");
            }

            if (config.Jira.BoardId <= 0)
            {
                throw new UserFacingException($"The configuration file {path} is not valid: jira.boardId must be a positive number.");
            }

            if (string.IsNullOrWhiteSpace(config.Jira.StoryPointFieldId))
            {
                throw new UserFacingException($"The configuration file {path} is not valid: jira.storyPointFieldId must not be empty.");
            }

            // The search asks Jira for this field by name, so it must be a custom field: a system field
            // such as "description" would bring free text over the wire.
            if (!IsCustomFieldId(config.Jira.StoryPointFieldId))
            {
                throw new UserFacingException($"The configuration file {path} is not valid: jira.storyPointFieldId must be a custom field ID such as customfield_10016.");
            }

            if (string.IsNullOrWhiteSpace(config.Jira.SprintFieldId))
            {
                throw new UserFacingException($"The configuration file {path} is not valid: jira.sprintFieldId must not be empty.");
            }

            // The collector stores this field's history, so it must be a custom field, and not the estimate.
            if (!IsCustomFieldId(config.Jira.SprintFieldId))
            {
                throw new UserFacingException($"The configuration file {path} is not valid: jira.sprintFieldId must be a custom field ID such as customfield_10020.");
            }

            if (config.Jira.SprintFieldId == config.Jira.StoryPointFieldId)
            {
                throw new UserFacingException($"The configuration file {path} is not valid: jira.sprintFieldId and jira.storyPointFieldId name the same field.");
            }

            if (config.Jira.ProjectKeys is not { Count: > 0 })
            {
                throw new UserFacingException($"The configuration file {path} is not valid: jira.projectKeys must list at least one project key.");
            }

            try
            {
                JiraProjectKey.RequireValid(config.Jira.ProjectKeys);
            }
            catch (FormatException exception)
            {
                throw new UserFacingException($"The configuration file {path} is not valid: jira.projectKeys: {exception.Message}", exception);
            }

            // Relative addresses are resolved against the site URL, and a base address without a trailing
            // slash loses its last path segment: https://host/tenant + rest/api would become https://host/rest/api.
            return siteUrl.AbsolutePath.EndsWith('/')
                ? config
                : config with { Jira = config.Jira with { SiteUrl = new UriBuilder(siteUrl) { Path = siteUrl.AbsolutePath + "/" }.Uri } };
        }
        catch (JsonException exception)
        {
            throw new UserFacingException($"The configuration file {path} is not valid: {exception.Message}", exception);
        }
    }
}
