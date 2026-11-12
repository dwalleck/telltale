using System.CommandLine;
using System.Security.Cryptography;
using Telltale.Core;
using Telltale.Core.Secrets;
using Telltale.Storage.Sqlite;

namespace Telltale.Cli.Commands;

/// <summary><c>secrets set</c>: stores one secret in the secret store, without ever showing its value.</summary>
internal static class SecretsCommand
{
    /// <summary>256 bits: the key size HMAC-SHA256 is built for.</summary>
    private const int GeneratedSaltBytes = 32;

    /// <summary>What the user asked for besides which secret.</summary>
    private sealed record Options(bool Generate, bool Replace, bool FromStandardInput);

    public static Command Create(AppServices services)
    {
        // There is deliberately no option or argument that takes the value: a command line is kept in shell
        // history and shown in process lists.
        var secret = new Argument<string>("secret") { Description = "Which secret to set: jira-email, jira-api-token or hashing-salt." };
        secret.AcceptOnlyFromAmong([.. SecretInfo.All.Select(info => info.Argument)]);
        var generate = new Option<bool>("--generate") { Description = "For hashing-salt: store a new random salt in place of asking for one.", Arity = ArgumentArity.Zero };
        var replace = new Option<bool>("--replace") { Description = "For hashing-salt: replace the salt people are already hashed with. Every person hash already in the database stops matching.", Arity = ArgumentArity.Zero };
        var stdin = new Option<bool>("--stdin") { Description = "Read the value from piped input (UTF-8, or UTF-16 with a byte-order mark) in place of asking for it.", Arity = ArgumentArity.Zero };

        var set = new Command("set", "Store one secret. Its value is asked for and never shown; with --stdin it is piped in.") { secret, generate, replace, stdin };
        set.SetAction((parse, cancellationToken) => CliApp.GuardAsync(services, async () =>
        {
            var chosen = SecretInfo.All.Single(info => info.Argument == parse.GetValue(secret));
            await SetAsync(services, chosen, new Options(parse.GetValue(generate), parse.GetValue(replace), parse.GetValue(stdin)), cancellationToken);
        }));

        return new Command("secrets", "Manage the secrets the tool needs: the Jira email, the Jira API token and the hashing salt.") { set };
    }

    private static async Task SetAsync(AppServices services, SecretInfo secret, Options options, CancellationToken cancellationToken)
    {
        var salt = SecretInfo.Of(SecretName.HashingSalt);
        if (options.Generate && secret != salt)
        {
            throw new UserFacingException($"--generate applies only to {salt.Argument}.");
        }

        if (options.Replace && secret != salt)
        {
            throw new UserFacingException($"--replace applies only to {salt.Argument}.");
        }

        if (options.Generate && options.FromStandardInput)
        {
            throw new UserFacingException("--generate makes the salt itself, so it cannot be combined with --stdin.");
        }

        if (services.Secrets is not IWritableSecretStore store)
        {
            throw new UserFacingException($"'secrets set' cannot store a secret on this system. {services.Secrets.HowToSet(secret.Name)}");
        }

        if (secret == salt && !options.Replace)
        {
            await RefuseToSplitPeopleAsync(services, store, salt, options.Generate, cancellationToken);
        }

        var given = options.Generate
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(GeneratedSaltBytes))
            : options.FromStandardInput
                ? SecretReader.ReadPiped(services.ConsoleInput, secret)
                : SecretReader.ReadTyped(services.ConsoleInput, services.Error, secret);

        // Stored without surrounding white space, which is how every reader takes it (SecretStoreExtensions.Require).
        var value = given.Trim();
        if (value.Length == 0)
        {
            throw new UserFacingException($"No value was given for the secret {secret.Label}. Nothing was stored.");
        }

        if (value.Any(char.IsControl))
        {
            throw new UserFacingException($"The value given for the secret {secret.Label} contains a control character, such as a line break, a tab or a NUL. A secret is one line of text. Nothing was stored.");
        }

        store.Set(secret.Name, value);
        await services.Out.WriteLineAsync($"Stored the secret {secret.Label}.");
    }

    /// <summary>
    /// A different salt gives every person a different hash. So without <c>--replace</c>, a stored salt is
    /// kept, and a new one is not generated while the database holds people hashed with a salt that has since
    /// gone missing. Typing a salt in that case is allowed: it is how the original is restored.
    /// A stored salt that cannot be read is no reason to stop: the message about it says to set it again.
    /// Only when the database holds hashed people does it count as a stored salt, which <c>--replace</c> replaces.
    /// </summary>
    private static async Task RefuseToSplitPeopleAsync(AppServices services, IWritableSecretStore store, SecretInfo salt, bool generate, CancellationToken cancellationToken)
    {
        string? stored;
        try
        {
            stored = store.Get(salt.Name);
        }
        catch (UserFacingException)
        {
            if (await SqliteStore.HoldsHashedPeopleAsync(services.DatabasePath, cancellationToken))
            {
                throw new UserFacingException(
                    "The stored hashing salt cannot be read, and the database already holds people hashed with a salt. A different salt would give every person a second hash, "
                    + "so each would count as two. To store a salt over the one that cannot be read, add --replace.");
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(stored))
        {
            throw new UserFacingException(
                "A hashing salt is already stored. A different salt gives every person a different hash, so the hashes already in the database would stop matching new ones. To replace it anyway, add --replace.");
        }

        if (generate && await SqliteStore.HoldsHashedPeopleAsync(services.DatabasePath, cancellationToken))
        {
            throw new UserFacingException(
                "The database already holds people hashed with an earlier salt. A new salt would give every person a second hash, so each would count as two. "
                + $"Restore the original salt by running 'telltale secrets set {salt.Argument}' without --generate, or add --replace to start again with a new salt.");
        }
    }
}
