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
    /// A different salt gives every person a different hash. So without <c>--replace</c>:
    /// <list type="bullet">
    /// <item>a stored salt is kept;</item>
    /// <item>with no salt stored and hashed people in the database (the salt went missing), a new one is not
    /// generated, and typing one is allowed: it is how the original is restored;</item>
    /// <item>a stored salt that was read and is not usable text is overwritten when the database holds no
    /// hashed people, since the message about it says to set it again; when it does, the command stops and
    /// says how to restore the original and what any other salt costs.</item>
    /// </list>
    /// A read that failed is none of these: it is reported as it is, because the salt may be there and good.
    /// </summary>
    private static async Task RefuseToSplitPeopleAsync(AppServices services, IWritableSecretStore store, SecretInfo salt, bool generate, CancellationToken cancellationToken)
    {
        string? stored;
        try
        {
            stored = store.Get(salt.Name);
        }
        catch (StoredSecretUnusableException)
        {
            if (await SqliteStore.HoldsHashedPeopleAsync(services.DatabasePath, cancellationToken))
            {
                throw new UserFacingException(
                    "The stored hashing salt is not text this tool stored, so it cannot be used, and the database already holds people hashed with a salt. "
                    + $"Only that salt keeps each of them one person: to restore it, run 'telltale secrets set {salt.Argument} --replace' and type the original salt. "
                    + "A new or generated salt stored with --replace makes every person count twice until a backfill.");
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
