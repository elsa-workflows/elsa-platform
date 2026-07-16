using System.Collections.Frozen;

namespace Elsa.Platform.Healing.Abstractions;

/// <summary>
/// Stable authorization vocabulary for Healing APIs and operator actions.
/// </summary>
public static class HealingPermissions
{
    public const string Read = "healing.read";
    public const string Configure = "healing.configure";
    public const string ElevateEvidence = "healing.evidence.elevate";
    public const string RetryRepair = "healing.repair.retry";
    public const string StopRepair = "healing.repair.stop";
    public const string WaiveVerification = "healing.verification.waive";
    public const string ConfigureAutoMerge = "healing.automerge.configure";

    public static IReadOnlySet<string> All { get; } = new[]
    {
        Read,
        Configure,
        ElevateEvidence,
        RetryRepair,
        StopRepair,
        WaiveVerification,
        ConfigureAutoMerge
    }.ToFrozenSet(StringComparer.Ordinal);
}

public static class HealingActorTypes
{
    public const string Human = "human";
    public const string Platform = "platform";
    public const string SourceProvider = "source-provider";
    public const string RepositoryWorkflow = "repository-workflow";
    public const string RepairAgent = "repair-agent";
    public const string DeploymentSystem = "deployment-system";

    public static IReadOnlySet<string> All { get; } = new[]
    {
        Human,
        Platform,
        SourceProvider,
        RepositoryWorkflow,
        RepairAgent,
        DeploymentSystem
    }.ToFrozenSet(StringComparer.Ordinal);
}

public static class HealingHumanCommands
{
    public const string Retry = "retry";
    public const string Stop = "stop";
    public const string RequestEvidence = "request-evidence";
    public const string WaiveEnvironment = "waive-environment";

    public static IReadOnlySet<string> All { get; } = new[]
    {
        Retry,
        Stop,
        RequestEvidence,
        WaiveEnvironment
    }.ToFrozenSet(StringComparer.Ordinal);
}
