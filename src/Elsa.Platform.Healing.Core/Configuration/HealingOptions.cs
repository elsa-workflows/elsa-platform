using Microsoft.Extensions.Options;

namespace Elsa.Platform.Healing.Core.Configuration;

public sealed class HealingOptions
{
    public const string SectionName = "Healing";
    public const string IncidentReviewEnabledConfigurationKey = SectionName + ":" + nameof(IncidentReviewEnabled);
    public const string VerificationEnabledConfigurationKey = SectionName + ":" + nameof(VerificationEnabled);

    public bool PlatformKillSwitch { get; set; }
    public bool DiscoveryEnabled { get; set; } = true;
    public bool IncidentReviewEnabled { get; set; } = true;
    public bool RepairDispatchEnabled { get; set; }
    public bool AutomaticMergeEnabled { get; set; }
    public bool VerificationEnabled { get; set; } = true;
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan LeaseSafetyMargin { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan IdleDelay { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(1);
    public HealingBudgetOptions Budgets { get; set; } = new();

    public void Validate()
    {
        if (Budgets is null)
            throw new InvalidOperationException("Healing Budgets are required.");
        Budgets.Validate();

        if (LeaseDuration < TimeSpan.FromSeconds(5) || LeaseDuration > TimeSpan.FromHours(1))
            throw new InvalidOperationException("Healing LeaseDuration must be between 5 seconds and 1 hour.");
        if (LeaseSafetyMargin < TimeSpan.Zero || LeaseSafetyMargin >= LeaseDuration)
            throw new InvalidOperationException("Healing LeaseSafetyMargin must be non-negative and less than LeaseDuration.");
        if (IdleDelay < TimeSpan.FromMilliseconds(100) || IdleDelay > TimeSpan.FromMinutes(1))
            throw new InvalidOperationException("Healing IdleDelay must be between 100 milliseconds and 1 minute.");
        if (RetryDelay < TimeSpan.Zero || RetryDelay > TimeSpan.FromHours(1))
            throw new InvalidOperationException("Healing RetryDelay must be between zero and 1 hour.");
    }
}

public sealed class HealingBudgetOptions
{
    public static readonly TimeSpan MaximumTimeBudget = TimeSpan.FromHours(4);
    public const int MaximumConcurrency = 32;
    public const long MaximumInferenceUnits = 2_000_000;
    public const int MaximumRepositoryRuns = 10;
    public const int MaximumRepairAttempts = 2;

    public TimeSpan TimeBudget { get; set; } = TimeSpan.FromMinutes(30);
    public int MaxConcurrentOperations { get; set; } = 4;
    public long MaxInferenceUnits { get; set; } = 200_000;
    public int MaxRepositoryRuns { get; set; } = 2;
    public int MaxRepairAttempts { get; set; } = 2;

    public void Validate()
    {
        if (TimeBudget <= TimeSpan.Zero || TimeBudget > MaximumTimeBudget)
            throw new InvalidOperationException($"Healing TimeBudget must be positive and no greater than {MaximumTimeBudget}.");
        if (MaxConcurrentOperations is < 1 or > MaximumConcurrency)
            throw new InvalidOperationException($"Healing MaxConcurrentOperations must be between 1 and {MaximumConcurrency}.");
        if (MaxInferenceUnits is < 0 or > MaximumInferenceUnits)
            throw new InvalidOperationException($"Healing MaxInferenceUnits must be between 0 and {MaximumInferenceUnits}.");
        if (MaxRepositoryRuns is < 0 or > MaximumRepositoryRuns)
            throw new InvalidOperationException($"Healing MaxRepositoryRuns must be between 0 and {MaximumRepositoryRuns}.");
        if (MaxRepairAttempts is < 0 or > MaximumRepairAttempts)
            throw new InvalidOperationException($"Healing MaxRepairAttempts must be between 0 and {MaximumRepairAttempts}.");
    }
}

public sealed class HealingOptionsValidator : IValidateOptions<HealingOptions>
{
    public ValidateOptionsResult Validate(string? name, HealingOptions options)
    {
        try
        {
            options.Validate();
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException exception)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }
    }
}

public static class HealingGateReasonCodes
{
    public const string Allowed = "allowed";
    public const string PlatformKillSwitch = "platform-kill-switch";
    public const string WorkspaceConfigurationNotFound = "workspace-configuration-not-found";
    public const string WorkspaceKillSwitch = "workspace-kill-switch";
    public const string ApplicationKillSwitch = "application-kill-switch";
    public const string EnvironmentKillSwitch = "environment-kill-switch";
    public const string StageDisabled = "stage-disabled";
    public const string ApplicationDisabled = "application-disabled";
    public const string EnvironmentDisabled = "environment-disabled";
}

public sealed record HealingGateResult(bool Allowed, string ReasonCode)
{
    public static HealingGateResult Permit() => new(true, HealingGateReasonCodes.Allowed);
    public static HealingGateResult Block(string reasonCode) => new(false, reasonCode);
}

public sealed class HealingKillSwitch
{
    private readonly Func<HealingOptions> _getOptions;

    public HealingKillSwitch(HealingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _getOptions = () => options;
    }

    public HealingKillSwitch(IOptionsMonitor<HealingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _getOptions = () => options.CurrentValue;
    }

    public HealingGateResult CanDiscover(
        HealingWorkspaceConfiguration workspace,
        HealingConfiguration application,
        HealingEnvironmentConfiguration? environment = null)
    {
        var options = _getOptions();
        return Evaluate(
            options,
            workspace,
            application,
            environment,
            options.DiscoveryEnabled,
            application.DiscoveryEnabled,
            environment?.DiscoveryEnabled);
    }

    public HealingGateResult CanDispatchRepair(
        HealingWorkspaceConfiguration workspace,
        HealingConfiguration application,
        HealingEnvironmentConfiguration? environment = null)
    {
        var options = _getOptions();
        return Evaluate(
            options,
            workspace,
            application,
            environment,
            options.RepairDispatchEnabled,
            application.RepairEnabled,
            environment?.RepairEnabled);
    }

    public HealingGateResult CanReviewIncidents() => EvaluatePlatformStage(_getOptions().IncidentReviewEnabled);

    public HealingGateResult CanVerify() => EvaluatePlatformStage(_getOptions().VerificationEnabled);

    public HealingGateResult CanAutomaticallyMerge(
        HealingWorkspaceConfiguration workspace,
        HealingConfiguration application,
        HealingEnvironmentConfiguration? environment = null)
    {
        var options = _getOptions();
        var killSwitch = EvaluateKillSwitches(options, workspace, application, environment);
        if (!killSwitch.Allowed)
            return killSwitch;

        return options.AutomaticMergeEnabled && application.AutomaticMergeEnabled
            ? HealingGateResult.Permit()
            : HealingGateResult.Block(HealingGateReasonCodes.StageDisabled);
    }

    private HealingGateResult Evaluate(
        HealingOptions options,
        HealingWorkspaceConfiguration workspace,
        HealingConfiguration application,
        HealingEnvironmentConfiguration? environment,
        bool platformStageEnabled,
        bool applicationStageEnabled,
        bool? environmentStageEnabled)
    {
        var killSwitch = EvaluateKillSwitches(options, workspace, application, environment);
        if (!killSwitch.Allowed)
            return killSwitch;
        if (!platformStageEnabled)
            return HealingGateResult.Block(HealingGateReasonCodes.StageDisabled);
        if (!applicationStageEnabled)
            return HealingGateResult.Block(HealingGateReasonCodes.ApplicationDisabled);
        if (environmentStageEnabled == false)
            return HealingGateResult.Block(HealingGateReasonCodes.EnvironmentDisabled);

        return HealingGateResult.Permit();
    }

    private HealingGateResult EvaluatePlatformStage(bool enabled)
    {
        var options = _getOptions();
        if (options.PlatformKillSwitch)
            return HealingGateResult.Block(HealingGateReasonCodes.PlatformKillSwitch);
        return enabled
            ? HealingGateResult.Permit()
            : HealingGateResult.Block(HealingGateReasonCodes.StageDisabled);
    }

    private HealingGateResult EvaluateKillSwitches(
        HealingOptions options,
        HealingWorkspaceConfiguration workspace,
        HealingConfiguration application,
        HealingEnvironmentConfiguration? environment)
    {
        if (options.PlatformKillSwitch)
            return HealingGateResult.Block(HealingGateReasonCodes.PlatformKillSwitch);
        if (workspace.WorkspaceKillSwitch)
            return HealingGateResult.Block(HealingGateReasonCodes.WorkspaceKillSwitch);
        if (application.ApplicationKillSwitch)
            return HealingGateResult.Block(HealingGateReasonCodes.ApplicationKillSwitch);
        if (environment?.EnvironmentKillSwitch == true)
            return HealingGateResult.Block(HealingGateReasonCodes.EnvironmentKillSwitch);

        return HealingGateResult.Permit();
    }
}
