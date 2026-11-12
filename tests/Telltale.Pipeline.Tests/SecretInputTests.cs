using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;

namespace Telltale.Pipeline.Tests;

/// <summary>How <c>secrets set</c> takes a value from the keyboard and from piped input. Every value here is synthetic.</summary>
public class SecretInputTests
{
    private const string ControlCharacterMessage =
        "The value given for the secret 'Jira API token' contains a control character, such as a line break, a tab or a NUL. A secret is one line of text. Nothing was stored.\n";

    [Test]
    public async Task Input_that_is_not_a_console_is_refused_without_the_stdin_option()
    {
        // Git Bash and some IDE terminals are not a console to Windows: the keys cannot be read unseen there.
        await using var pipeline = new PipelineHarness();
        pipeline.Console.Pipe("synthetic-api-token-0002\n");

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The value cannot be typed here without being shown, because input does not come from a Windows console. Run this in PowerShell, cmd or Windows Terminal, or pipe the value in and add --stdin.\n");
        await Assert.That(pipeline.Console.StandardInputOpened).IsFalse();
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo(PipelineHarness.JiraApiToken);
    }

    [Test]
    public async Task A_piped_value_is_stored_with_the_stdin_option_and_nothing_is_asked()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Console.Pipe("synthetic-api-token-0002\r\n");

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token", "--stdin");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo("synthetic-api-token-0002");
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).IsEqualTo("Stored the secret 'Jira API token'.\n");
    }

    [Test]
    public async Task The_stdin_option_is_refused_when_nothing_is_piped_in()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Console.TypeLine("synthetic-api-token-0002");

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token", "--stdin");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "--stdin reads the value from piped input, and nothing is piped in. Leave --stdin out to type the value.\n");
        await Assert.That(pipeline.Console.KeysRead).IsEqualTo(0);
        await Assert.That(pipeline.Console.StandardInputOpened).IsFalse();
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo(PipelineHarness.JiraApiToken);
    }

    [Test]
    [Arguments("as a second argument", new[] { "secrets", "set", "jira-api-token", "synthetic-api-token-0002" })]
    [Arguments("as a value option", new[] { "secrets", "set", "jira-api-token", "--value", "synthetic-api-token-0002" })]
    [Arguments("as the stdin option's value", new[] { "secrets", "set", "jira-api-token", "--stdin", "synthetic-api-token-0002" })]
    public async Task A_value_on_the_command_line_is_never_accepted(string how, string[] args)
    {
        // The command line is kept in shell history and shown in process lists.
        await using var pipeline = new PipelineHarness();
        pipeline.Console.Pipe("synthetic-api-token-0003");

        var exit = await pipeline.RunAsync(args);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo(PipelineHarness.JiraApiToken).Because(how);
    }

    [Test]
    [Arguments("UTF-8 without a byte-order mark")]
    [Arguments("UTF-8 with a byte-order mark")]
    [Arguments("UTF-16 little-endian with a byte-order mark")]
    [Arguments("UTF-16 big-endian with a byte-order mark")]
    public async Task A_piped_value_is_read_in_the_encoding_its_byte_order_mark_declares(string encoding)
    {
        // "tøken-€-0004\r\n", encoded by hand per encoding rather than by the code under test's decoder.
        byte[] bytes = encoding switch
        {
            "UTF-8 without a byte-order mark" => [0x74, 0xC3, 0xB8, 0x6B, 0x65, 0x6E, 0x2D, 0xE2, 0x82, 0xAC, 0x2D, 0x30, 0x30, 0x30, 0x34, 0x0D, 0x0A],
            "UTF-8 with a byte-order mark" => [0xEF, 0xBB, 0xBF, 0x74, 0xC3, 0xB8, 0x6B, 0x65, 0x6E, 0x2D, 0xE2, 0x82, 0xAC, 0x2D, 0x30, 0x30, 0x30, 0x34, 0x0D, 0x0A],
            "UTF-16 little-endian with a byte-order mark" =>
            [
                0xFF, 0xFE, 0x74, 0x00, 0xF8, 0x00, 0x6B, 0x00, 0x65, 0x00, 0x6E, 0x00, 0x2D, 0x00, 0xAC, 0x20, 0x2D, 0x00,
                0x30, 0x00, 0x30, 0x00, 0x30, 0x00, 0x34, 0x00, 0x0D, 0x00, 0x0A, 0x00,
            ],
            _ =>
            [
                0xFE, 0xFF, 0x00, 0x74, 0x00, 0xF8, 0x00, 0x6B, 0x00, 0x65, 0x00, 0x6E, 0x00, 0x2D, 0x20, 0xAC, 0x00, 0x2D,
                0x00, 0x30, 0x00, 0x30, 0x00, 0x30, 0x00, 0x34, 0x00, 0x0D, 0x00, 0x0A,
            ],
        };
        await using var pipeline = new PipelineHarness();
        pipeline.Console.Pipe(bytes);

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token", "--stdin");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo("tøken-€-0004");
    }

    [Test]
    [Arguments("two lines", new byte[] { 0x61, 0x62, 0x0A, 0x63, 0x64, 0x0A })]
    [Arguments("a tab inside", new byte[] { 0x61, 0x62, 0x09, 0x63, 0x64 })]
    [Arguments("a NUL inside", new byte[] { 0x61, 0x62, 0x00, 0x63, 0x64 })]
    [Arguments("UTF-16 without a byte-order mark", new byte[] { 0x61, 0x00, 0x62, 0x00, 0x63, 0x00, 0x64, 0x00 })]
    public async Task A_piped_value_with_a_control_character_is_refused_without_showing_any_of_it(string what, byte[] bytes)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Console.Pipe(bytes);

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token", "--stdin");

        await Assert.That(exit).IsEqualTo(1).Because(what);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(ControlCharacterMessage);
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo(PipelineHarness.JiraApiToken);
    }

    [Test]
    [Arguments("a broken UTF-8 sequence", new byte[] { 0x61, 0xC3, 0x28, 0x62 })]
    [Arguments("half a UTF-16 character", new byte[] { 0xFF, 0xFE, 0x61, 0x00, 0x62 })]
    public async Task Piped_bytes_that_are_not_text_are_refused(string what, byte[] bytes)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Console.Pipe(bytes);

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token", "--stdin");

        await Assert.That(exit).IsEqualTo(1).Because(what);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The piped value for the secret 'Jira API token' is not text in UTF-8, or in UTF-16 with a byte-order mark. Nothing was stored.\n");
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo(PipelineHarness.JiraApiToken);
    }

    [Test]
    [Arguments("")]
    [Arguments("\r\n")]
    public async Task An_empty_pipe_stores_nothing_and_names_the_secret(string piped)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Console.Pipe(piped);

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token", "--stdin");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("No value was given for the secret 'Jira API token'. Nothing was stored.\n");
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo(PipelineHarness.JiraApiToken);
    }

    [Test]
    public async Task Piped_input_far_longer_than_any_secret_is_refused()
    {
        // The wrong file piped in, for example. 16 KiB is the limit; this is one byte more.
        await using var pipeline = new PipelineHarness();
        pipeline.Console.Pipe(Enumerable.Repeat((byte)0x61, 16385).ToArray());

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token", "--stdin");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The piped input for the secret 'Jira API token' is longer than 16384 bytes, which no secret is. Nothing was stored.\n");
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo(PipelineHarness.JiraApiToken);
    }

    [Test]
    public async Task A_piped_value_of_exactly_the_longest_length_is_read()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Console.Pipe(Enumerable.Repeat((byte)0x61, 16384).ToArray());

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token", "--stdin");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken].Length).IsEqualTo(16384);
    }

    [Test]
    public async Task Backspace_removes_the_last_typed_character()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Console.Type("synthetic-X").Press(ConsoleKey.Backspace).TypeLine("0005");

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo("synthetic-0005");
    }

    [Test]
    public async Task Backspace_removes_a_whole_character_that_takes_two_code_units()
    {
        // U+1F600 arrives as two key presses, a high and a low surrogate. Removing only one would leave half a character.
        await using var pipeline = new PipelineHarness();
        pipeline.Console.Type("synthetic-😀").Press(ConsoleKey.Backspace).TypeLine("0006");

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo("synthetic-0006");
    }

    [Test]
    public async Task Backspace_with_nothing_typed_does_nothing()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Console.Press(ConsoleKey.Backspace).Type("x").Press(ConsoleKey.Backspace).Press(ConsoleKey.Backspace).TypeLine("synthetic-0007");

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo("synthetic-0007");
    }

    [Test]
    public async Task A_key_that_types_no_character_is_ignored()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Console.Type("synthetic-").Press(ConsoleKey.LeftArrow).Press(ConsoleKey.F5).TypeLine("0008");

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo("synthetic-0008");
    }

    [Test]
    public async Task A_typed_control_character_is_refused_and_not_dropped()
    {
        // Dropping the tab would store a value that differs from what was pasted, without saying so.
        await using var pipeline = new PipelineHarness();
        pipeline.Console.TypeLine("synthetic\t0009");

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).EndsWith("\n" + ControlCharacterMessage);
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo(PipelineHarness.JiraApiToken);
    }

    [Test]
    public async Task A_paste_of_more_than_one_line_is_refused_and_none_of_it_is_left_for_the_shell()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Console.Type("synthetic-0010\nsynthetic-0011\n");

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).EndsWith(
            "\nMore than one line was entered for the secret 'Jira API token'. A secret is one line of text. Nothing was stored.\n");
        await Assert.That(pipeline.Console.KeysLeft).IsEqualTo(0);
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo(PipelineHarness.JiraApiToken);
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("synthetic-001");
    }

    [Test]
    public async Task A_paste_that_ends_with_extra_line_endings_is_one_line()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Console.Type("synthetic-0012\n\n");

        var exit = await pipeline.RunAsync("secrets", "set", "jira-api-token");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo("synthetic-0012");
        await Assert.That(pipeline.Console.KeysLeft).IsEqualTo(0);
    }

    [Test]
    [Arguments("jira-email")]
    [Arguments("jira-api-token")]
    public async Task The_replace_option_is_refused_for_a_secret_other_than_the_salt(string argument)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Console.TypeLine("synthetic-value-0013");

        var exit = await pipeline.RunAsync("secrets", "set", argument, "--replace");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("--replace applies only to hashing-salt.\n");
        await Assert.That(pipeline.Console.KeysRead).IsEqualTo(0);
        await Assert.That(pipeline.Secrets[SecretName.JiraEmail]).IsEqualTo(PipelineHarness.JiraEmail);
        await Assert.That(pipeline.Secrets[SecretName.JiraApiToken]).IsEqualTo(PipelineHarness.JiraApiToken);
    }

    [Test]
    public async Task Generating_the_salt_cannot_be_combined_with_a_piped_value()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Secrets.Clear();
        pipeline.Console.Pipe("synthetic-salt-three\n");

        var exit = await pipeline.RunAsync("secrets", "set", "hashing-salt", "--generate", "--stdin");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("--generate makes the salt itself, so it cannot be combined with --stdin.\n");
        await Assert.That(pipeline.Secrets).IsEmpty();
        await Assert.That(pipeline.Console.StandardInputOpened).IsFalse();
    }

    [Test]
    public async Task The_salt_can_be_generated_where_input_is_not_a_console()
    {
        // Nothing is typed for a generated salt, so a terminal that would show typing is no obstacle.
        await using var pipeline = new PipelineHarness();
        pipeline.Secrets.Clear();
        pipeline.Console.Pipe(string.Empty);

        var exit = await pipeline.RunAsync("secrets", "set", "hashing-salt", "--generate");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(Convert.FromBase64String(pipeline.Secrets[SecretName.HashingSalt]).Length).IsEqualTo(32);
    }
}
