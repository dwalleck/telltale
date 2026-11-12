using System.CommandLine;
using System.Security.Cryptography;
using Telltale.Core;
using Telltale.Core.Secrets;

namespace Telltale.Cli.Commands;

/// <summary><c>secrets set</c>: stores one secret in the secret store, without ever showing its value.</summary>
internal static class SecretsCommand
{
    /// <summary>256 bits: the key size HMAC-SHA256 is built for.</summary>
    private const int GeneratedSaltBytes = 32;

    /// <summary>The name a secret goes by on the command line, as in <c>secrets set jira-api-token</c>.</summary>
    public static string ArgumentFor(SecretName name) => name switch
    {
        SecretName.JiraEmail => "jira-email",
        SecretName.JiraApiToken => "jira-api-token",
        SecretName.HashingSalt => "hashing-salt",
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown secret."),
    };

    public static Command Create(AppServices services)
    {
        var secret = new Argument<string>("secret") { Description = "Which secret to set: jira-email, jira-api-token or hashing-salt." };
        secret.AcceptOnlyFromAmong([.. Enum.GetValues<SecretName>().Select(ArgumentFor)]);
        var generate = new Option<bool>("--generate") { Description = "For hashing-salt: store a new random salt in place of asking for one." };
        var replace = new Option<bool>("--replace") { Description = "For hashing-salt: replace a salt that is already stored. Every person hash already in the database stops matching." };

        var set = new Command("set", "Store one secret. Its value is asked for and never shown; it can also be piped in.") { secret, generate, replace };
        set.SetAction((parse, cancellationToken) => CliApp.GuardAsync(services, async () =>
        {
            var name = Enum.GetValues<SecretName>().Single(candidate => ArgumentFor(candidate) == parse.GetValue(secret));
            await SetAsync(services, name, parse.GetValue(generate), parse.GetValue(replace));
        }));

        return new Command("secrets", "Manage the secrets the tool needs: the Jira email, the Jira API token and the hashing salt.") { set };
    }

    private static async Task SetAsync(AppServices services, SecretName name, bool generate, bool replace)
    {
        if (generate && name != SecretName.HashingSalt)
        {
            throw new UserFacingException($"--generate applies only to {ArgumentFor(SecretName.HashingSalt)}.");
        }

        if (services.Secrets is not IWritableSecretStore store)
        {
            throw new UserFacingException($"'secrets set' cannot store a secret on this system. {services.Secrets.HowToSet(name)}");
        }

        if (name == SecretName.HashingSalt && !replace && !string.IsNullOrWhiteSpace(store.Get(name)))
        {
            throw new UserFacingException(
                "A hashing salt is already stored. A different salt gives every person a different hash, so the hashes already in the database would stop matching new ones. To replace it anyway, add --replace.");
        }

        var value = generate
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(GeneratedSaltBytes))
            : services.SecretInput.Read($"Value for the secret {name.Describe()} (not shown as you type): ");
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UserFacingException($"No value was given for the secret {name.Describe()}. Nothing was stored.");
        }

        // Stored without surrounding white space, which is how every reader takes it (SecretStoreExtensions.Require).
        store.Set(name, value.Trim());
        await services.Out.WriteLineAsync($"Stored the secret {name.Describe()}.");
    }
}
