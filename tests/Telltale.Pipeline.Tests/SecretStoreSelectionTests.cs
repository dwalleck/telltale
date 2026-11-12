using Telltale.Cli;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;

namespace Telltale.Pipeline.Tests;

public class SecretStoreSelectionTests
{
    [Test]
    public async Task On_windows_secrets_come_from_credential_manager_and_not_from_environment_variables()
    {
        await Assert.That(SecretStores.ForPlatform(isWindows: true)).IsTypeOf<CredentialManagerSecretStore>();
    }

    [Test]
    public async Task Off_windows_secrets_come_from_environment_variables()
    {
        await Assert.That(SecretStores.ForPlatform(isWindows: false)).IsTypeOf<EnvironmentSecretStore>();
    }

    [Test]
    public async Task The_executable_selects_its_secret_store_by_the_system_it_runs_on()
    {
        var services = AppServices.ForProduction();
        using var handler = services.HttpHandler;

        // Stated per system on purpose: this suite runs off Windows today and on the work laptop in ticket 13.
        if (OperatingSystem.IsWindows())
        {
            await Assert.That(services.Secrets).IsTypeOf<CredentialManagerSecretStore>();
        }
        else
        {
            await Assert.That(services.Secrets).IsTypeOf<EnvironmentSecretStore>();
        }
    }

    [Test]
    [Arguments(SecretName.JiraEmail, "Run 'telltale secrets set jira-email'.")]
    [Arguments(SecretName.JiraApiToken, "Run 'telltale secrets set jira-api-token'.")]
    [Arguments(SecretName.HashingSalt, "Run 'telltale secrets set hashing-salt'.")]
    public async Task Credential_manager_says_which_command_sets_a_secret(SecretName name, string expected)
    {
        await Assert.That(new CredentialManagerSecretStore(new FakeCredentialVault()).HowToSet(name)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(SecretName.JiraEmail, "The secret 'Jira email' is not set in the secret store. Set the environment variable TELLTALE_JIRA_EMAIL.\n")]
    [Arguments(SecretName.JiraApiToken, "The secret 'Jira API token' is not set in the secret store. Set the environment variable TELLTALE_JIRA_API_TOKEN.\n")]
    [Arguments(SecretName.HashingSalt, "The secret 'hashing salt' is not set in the secret store. Set the environment variable TELLTALE_HASHING_SALT.\n")]
    public async Task A_missing_secret_is_named_with_how_to_set_it(SecretName missing, string expected)
    {
        await using var pipeline = new PipelineHarness();
        var variables = new Dictionary<string, string>
        {
            ["TELLTALE_JIRA_EMAIL"] = PipelineHarness.JiraEmail,
            ["TELLTALE_JIRA_API_TOKEN"] = PipelineHarness.JiraApiToken,
            ["TELLTALE_HASHING_SALT"] = PipelineHarness.HashingSalt,
        };
        variables.Remove(SecretInfo.Of(missing).EnvironmentVariable);
        pipeline.SecretStore = new EnvironmentSecretStore(variables.GetValueOrDefault);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(expected);
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }
}
