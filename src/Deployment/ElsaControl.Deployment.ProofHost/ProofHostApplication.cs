using System.Text;

namespace ElsaControl.Deployment.ProofHost;

/// <summary>
/// Extension point for the later live Azure composition. The parser and mutation gate remain
/// usable without constructing Azure SDK/CLI dependencies, which keeps validation offline and
/// prevents accidental production activation.
/// </summary>
public interface IProofHostExecutor
{
    Task<int> ExecuteAsync(ProofHostOptions options, CancellationToken cancellationToken = default);
}

public static class ProofHostApplication
{
    public const int InvalidConfigurationExitCode = 2;
    public const int MutationGateExitCode = 3;
    public const int CompositionUnavailableExitCode = 4;

    public static async Task<int> RunAsync(
        IEnumerable<string>? arguments,
        IReadOnlyDictionary<string, string?>? environment = null,
        IProofHostExecutor? executor = null,
        TextWriter? output = null,
        TextWriter? error = null,
        CancellationToken cancellationToken = default)
    {
        output ??= Console.Out;
        error ??= Console.Error;

        var parsed = ProofHostOptionsParser.Parse(arguments, environment ?? ProofHostOptionsParser.ReadProcessEnvironment());
        if (parsed.HelpRequested)
        {
            await output.WriteAsync(UsageText);
            return 0;
        }

        if (!parsed.Succeeded)
        {
            foreach (var validationError in parsed.Errors)
                await error.WriteLineAsync($"proof-host.{validationError}");
            return parsed.MutationGateFailed ? MutationGateExitCode : InvalidConfigurationExitCode;
        }

        var options = parsed.Options!;
        if (options.Mode == ProofHostMode.Validate)
        {
            await output.WriteLineAsync(options.ToSafeJson());
            return 0;
        }

        if (executor is null)
        {
            await error.WriteLineAsync("proof-host.composition.unavailable");
            return CompositionUnavailableExitCode;
        }

        return await executor.ExecuteAsync(options, cancellationToken);
    }

    public static string UsageText => """
        Usage:
          ElsaControl.ProofHost validate [options]
          ElsaControl.ProofHost run [options]
          ElsaControl.ProofHost cleanup [options]

        Mutating run and cleanup modes require the exact environment value:
          DISPOSABLE_PROOF_APPLY=YES

        Safe inputs may be supplied as CLI options or DISPOSABLE_PROOF_* environment values.
        Secret values are never accepted; only secret:// reference locators are allowed.
        """;
}
