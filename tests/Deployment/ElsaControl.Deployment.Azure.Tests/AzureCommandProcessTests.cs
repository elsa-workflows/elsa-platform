using System.Diagnostics;
using System.Text.Json;

using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureCommandProcessTests
{
    [Fact]
    public async Task Passes_arguments_as_inert_values_without_shell_interpretation()
    {
        if (OperatingSystem.IsWindows())
            return;

        var marker = Path.Combine(Path.GetTempPath(), $"elsa-command-process-{Guid.NewGuid():N}");
        try
        {
            var result = await Process().ExecuteAsync(new AzureCommandProcessRequest(
                EchoExecutable(),
                [Arg($"$(touch {marker})")]), ProjectText);

            Assert.True(result.Succeeded);
            Assert.Contains("$", result.Value, StringComparison.Ordinal);
            Assert.False(File.Exists(marker));
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public async Task Returns_bounded_output_for_a_successful_command()
    {
        var result = await Process().ExecuteAsync(Shell("printf 'output'; printf 'diagnostic' >&2"), ProjectText);

        Assert.Equal(AzureCommandProcessStatus.Succeeded, result.Status);
        Assert.Equal(AzureCommandProcessFailureKind.None, result.FailureKind);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("output", result.Value);
    }

    [Fact]
    public async Task Does_not_wait_for_a_descendant_that_inherits_redirected_output_handles()
    {
        if (OperatingSystem.IsWindows())
            return;

        var stopwatch = Stopwatch.StartNew();
        var result = await Process().ExecuteAsync(Shell("sleep 5 &"), ProjectText);
        stopwatch.Stop();

        Assert.True(result.Succeeded);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"The completed parent took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Classifies_nonzero_exit_without_returning_process_output()
    {
        var result = await Process().ExecuteAsync(Shell("printf 'secret-output'; printf 'secret-error' >&2; exit 23"), ProjectText);

        Assert.Equal(AzureCommandProcessStatus.Failed, result.Status);
        Assert.Equal(AzureCommandProcessFailureKind.NonZeroExitCode, result.FailureKind);
        Assert.Equal(23, result.ExitCode);
        Assert.Null(result.Value);
        Assert.DoesNotContain("secret", result.Code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Terminates_a_command_when_the_timeout_expires()
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await new AzureCommandProcess(TimeSpan.FromMilliseconds(100), 4096)
            .ExecuteAsync(Shell(SleepScript(TimeSpan.FromSeconds(5))), ProjectText);
        stopwatch.Stop();

        Assert.Equal(AzureCommandProcessStatus.TimedOut, result.Status);
        Assert.Equal(AzureCommandProcessFailureKind.TimedOut, result.FailureKind);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"The timed-out process took {stopwatch.Elapsed}.");
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Cancellation_terminates_the_process_tree_and_returns_a_safe_classification()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"elsa-command-process-{Guid.NewGuid():N}");
        using var cancellation = new CancellationTokenSource();
        try
        {
            var command = Shell(SleepThenCreateScript(TimeSpan.FromSeconds(5), marker));
            var running = Process().ExecuteAsync(command, ProjectText, cancellation.Token);
            await Task.Delay(100);
            cancellation.Cancel();
            var result = await running;

            Assert.Equal(AzureCommandProcessStatus.Cancelled, result.Status);
            Assert.Equal(AzureCommandProcessFailureKind.Cancelled, result.FailureKind);
            Assert.Null(result.Value);
            await Task.Delay(250);
            Assert.False(File.Exists(marker));
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public async Task Stops_and_classifies_output_that_exceeds_the_configured_cap()
    {
        var result = await new AzureCommandProcess(TimeSpan.FromSeconds(5), 32)
            .ExecuteAsync(Shell("printf '1234567890123456789012345678901234567890'"), ProjectText);

        Assert.Equal(AzureCommandProcessStatus.OutputLimitExceeded, result.Status);
        Assert.Equal(AzureCommandProcessFailureKind.OutputLimitExceeded, result.FailureKind);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Projects_success_output_before_returning_a_serializable_result()
    {
        var result = await Process().ExecuteAsync(
            Shell("printf 'secret-provider-payload'; printf 'secret-diagnostic' >&2"),
            _ => "safe-typed-value");

        Assert.True(result.Succeeded);
        Assert.Equal("safe-typed-value", result.Value);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("secret-provider-payload", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-diagnostic", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Classifies_a_missing_executable_without_leaking_the_path_or_exception_text()
    {
        const string missingExecutable = "this-executable-does-not-exist-elsa-control";

        var result = await Process().ExecuteAsync(new AzureCommandProcessRequest(missingExecutable), ProjectText);

        Assert.Equal(AzureCommandProcessStatus.Failed, result.Status);
        Assert.Equal(AzureCommandProcessFailureKind.ExecutableNotFound, result.FailureKind);
        Assert.Equal("azure.command.executable-not-found", result.Code);
        Assert.DoesNotContain(missingExecutable, result.Code, StringComparison.Ordinal);
        Assert.DoesNotContain(missingExecutable, result.Message, StringComparison.Ordinal);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("az\nforged")]
    [InlineData("az\rforged")]
    public void Rejects_control_characters_in_executable_locators(string executable)
    {
        Assert.Throws<ArgumentException>(() => new AzureCommandProcessRequest(executable));
    }

    [Fact]
    public void Request_and_result_string_forms_do_not_include_secret_values()
    {
        var request = new AzureCommandProcessRequest(
            "az",
            [Arg("--token"), Arg("secret-token")],
            new Dictionary<string, string?> { ["AZURE_SECRET"] = "secret-value" });
        var result = new AzureCommandProcessResult<string>(
            AzureCommandProcessStatus.Failed,
            AzureCommandProcessFailureKind.ExecutionFailed,
            null,
            "safe-typed-value",
            "azure.command.execution-failed",
            "The Azure command failed before a result could be observed.");

        Assert.DoesNotContain("secret", request.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", result.ToString(), StringComparison.OrdinalIgnoreCase);
        var serializedRequest = JsonSerializer.Serialize(request);
        Assert.DoesNotContain("secret-value", serializedRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", serializedRequest, StringComparison.Ordinal);
    }

    private static AzureCommandProcess Process() => new(TimeSpan.FromSeconds(5), 4096);

    private static string ProjectText(ReadOnlyMemory<char> output) => output.ToString();

    private static AzureCommandArgument Arg(string value) => AzureCommandArgument.Safe(value);

    private static string EchoExecutable() => "/bin/echo";

    private static AzureCommandProcessRequest Shell(string script) => OperatingSystem.IsWindows()
        ? new("cmd.exe", [Arg("/d"), Arg("/s"), Arg("/c"), Arg(script)])
        : new("/bin/sh", [Arg("-c"), Arg(script)]);

    private static string SleepScript(TimeSpan duration) => OperatingSystem.IsWindows()
        ? $"ping 127.0.0.1 -n {(int)duration.TotalSeconds + 1} > nul"
        : $"sleep {duration.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static string SleepThenCreateScript(TimeSpan duration, string marker) => OperatingSystem.IsWindows()
        ? $"ping 127.0.0.1 -n {(int)duration.TotalSeconds + 1} > nul & echo created > \"{marker}\""
        : $"sleep {duration.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}; touch \"{marker}\"";
}
