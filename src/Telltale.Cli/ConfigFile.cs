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
    };

    public static async Task<TelltaleConfig> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new UserFacingException($"The configuration file {path} does not exist.");
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var config = await JsonSerializer.DeserializeAsync<TelltaleConfig>(stream, Options, cancellationToken)
                ?? throw new UserFacingException($"The configuration file {path} is empty.");

            // Basic authentication sends the Jira email and API token with every request, so only https will do.
            if (config.Jira.SiteUrl is not { IsAbsoluteUri: true } siteUrl || siteUrl.Scheme != Uri.UriSchemeHttps)
            {
                throw new UserFacingException($"The configuration file {path} is not valid: jira.siteUrl must be an absolute https:// address.");
            }

            return config;
        }
        catch (JsonException exception)
        {
            throw new UserFacingException($"The configuration file {path} is not valid: {exception.Message}", exception);
        }
    }
}
