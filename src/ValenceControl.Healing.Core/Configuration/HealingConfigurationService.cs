using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core.Ownership;
using ValenceControl.Healing.Core.Security;
using System.Text;
using System.Text.Json;

namespace ValenceControl.Healing.Core.Configuration;

public sealed record EffectiveHealingConfiguration(
    bool DiscoveryEnabled,
    bool RepairEnabled,
    bool AutomaticMergeEnabled,
    int OccurrenceThreshold,
    TimeSpan DebounceWindow,
    bool ApplicationKillSwitch,
    bool EnvironmentKillSwitch);

public sealed class HealingConfigurationService(
    IHealingOwnershipStore store,
    HealingAuditService auditService,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<HealingOperationResult<HealingConfiguration>> SaveAsync(
        HealingConfiguration configuration,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var existing = await store.GetConfigurationAsync(
            configuration.WorkspaceId, configuration.ApplicationId, cancellationToken);
        var automaticMergeChanged = existing is null
            ? configuration.AutomaticMergeEnabled
            : existing.AutomaticMergeEnabled != configuration.AutomaticMergeEnabled;
        var authorizationFailure = HealingOwnershipAuthorization.ConfigurationFailure(
            authorization,
            configuration.WorkspaceId,
            configuration.ApplicationId,
            automaticMergeChanged);
        if (authorizationFailure is not null)
            return HealingOperationResult<HealingConfiguration>.Denied(authorizationFailure);

        if (!IsValid(configuration))
            return HealingOperationResult<HealingConfiguration>.Denied(HealingOwnershipReasonCodes.InvalidConfiguration);

        var now = _timeProvider.GetUtcNow();
        if (configuration.Id == Guid.Empty)
            configuration.Id = Guid.NewGuid();
        if (configuration.CreatedAt == default)
            configuration.CreatedAt = now;
        configuration.UpdatedAt = now;
        foreach (var environment in configuration.Environments)
        {
            if (environment.Id == Guid.Empty)
                environment.Id = Guid.NewGuid();
            environment.HealingConfigurationId = configuration.Id;
            environment.WorkspaceId = configuration.WorkspaceId;
            environment.ApplicationId = configuration.ApplicationId;
            if (environment.CreatedAt == default)
                environment.CreatedAt = now;
            environment.UpdatedAt = now;
        }

        var saved = await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var workspaceConfiguration = await store.GetWorkspaceConfigurationAsync(
                configuration.WorkspaceId,
                transactionCancellationToken);
            if (workspaceConfiguration is null)
            {
                await store.UpsertWorkspaceConfigurationAsync(new HealingWorkspaceConfiguration
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = configuration.WorkspaceId,
                    WorkspaceKillSwitch = false,
                    CreatedAt = now,
                    UpdatedAt = now
                }, transactionCancellationToken);
            }
            var persisted = await store.SaveConfigurationAsync(configuration, transactionCancellationToken);
            await AuditAsync(persisted, "configuration-saved", "configured", authorization, transactionCancellationToken);
            return persisted;
        }, cancellationToken);
        return HealingOperationResult<HealingConfiguration>.Success(saved);
    }

    public async ValueTask<HealingOperationResult<HealingConfiguration>> GetAsync(
        Guid workspaceId,
        Guid applicationId,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        var authorizationFailure = HealingOwnershipAuthorization.ReadFailure(authorization, workspaceId, applicationId);
        if (authorizationFailure is not null)
            return HealingOperationResult<HealingConfiguration>.Denied(authorizationFailure);
        var configuration = await store.GetConfigurationAsync(workspaceId, applicationId, cancellationToken);
        return configuration is null
            ? HealingOperationResult<HealingConfiguration>.Denied(HealingOwnershipReasonCodes.NotFound)
            : HealingOperationResult<HealingConfiguration>.Success(configuration);
    }

    public async ValueTask<HealingOperationResult<HealingConfiguration>> EmergencyStopAsync(
        Guid workspaceId,
        Guid applicationId,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        var authorizationFailure = HealingOwnershipAuthorization.ConfigurationFailure(authorization, workspaceId, applicationId);
        if (authorizationFailure is not null)
            return HealingOperationResult<HealingConfiguration>.Denied(authorizationFailure);
        var configuration = await store.GetConfigurationAsync(workspaceId, applicationId, cancellationToken);
        if (configuration is null)
            return HealingOperationResult<HealingConfiguration>.Denied(HealingOwnershipReasonCodes.NotFound);
        if (configuration.ApplicationKillSwitch)
            return HealingOperationResult<HealingConfiguration>.Success(configuration);

        configuration.ApplicationKillSwitch = true;
        configuration.UpdatedAt = _timeProvider.GetUtcNow();
        var saved = await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var persisted = await store.SaveConfigurationAsync(configuration, transactionCancellationToken);
            await AuditAsync(persisted, "emergency-stop-activated", "stopped", authorization, transactionCancellationToken);
            return persisted;
        }, cancellationToken);
        return HealingOperationResult<HealingConfiguration>.Success(saved);
    }

    public async ValueTask<HealingOperationResult<HealingConfiguration>> ResumeAsync(
        Guid workspaceId,
        Guid applicationId,
        HealingAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        var authorizationFailure = HealingOwnershipAuthorization.ConfigurationFailure(authorization, workspaceId, applicationId);
        if (authorizationFailure is not null)
            return HealingOperationResult<HealingConfiguration>.Denied(authorizationFailure);
        var configuration = await store.GetConfigurationAsync(workspaceId, applicationId, cancellationToken);
        if (configuration is null)
            return HealingOperationResult<HealingConfiguration>.Denied(HealingOwnershipReasonCodes.NotFound);
        if (!configuration.ApplicationKillSwitch)
            return HealingOperationResult<HealingConfiguration>.Success(configuration);

        configuration.ApplicationKillSwitch = false;
        configuration.UpdatedAt = _timeProvider.GetUtcNow();
        var saved = await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var persisted = await store.SaveConfigurationAsync(configuration, transactionCancellationToken);
            await AuditAsync(persisted, "emergency-stop-cleared", "active", authorization, transactionCancellationToken);
            return persisted;
        }, cancellationToken);
        return HealingOperationResult<HealingConfiguration>.Success(saved);
    }

    public async ValueTask<EffectiveHealingConfiguration?> GetEffectiveAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid? environmentId = null,
        CancellationToken cancellationToken = default)
    {
        var configuration = await store.GetConfigurationAsync(workspaceId, applicationId, cancellationToken);
        if (configuration is null)
            return null;
        var environment = environmentId is null
            ? null
            : configuration.Environments.SingleOrDefault(x => x.EnvironmentId == environmentId);
        return new EffectiveHealingConfiguration(
            environment?.DiscoveryEnabled ?? configuration.DiscoveryEnabled,
            environment?.RepairEnabled ?? configuration.RepairEnabled,
            configuration.AutomaticMergeEnabled,
            environment?.OccurrenceThreshold ?? 1,
            environment?.DebounceWindow ?? TimeSpan.Zero,
            configuration.ApplicationKillSwitch,
            environment?.EnvironmentKillSwitch ?? false);
    }

    private static bool IsValid(HealingConfiguration configuration)
    {
        if (configuration.WorkspaceId == Guid.Empty || configuration.ApplicationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(configuration.SignalProfileVersion) ||
            configuration.DefaultAttemptLimit is < 1 or > HealingBudgetOptions.MaximumRepairAttempts ||
            configuration.VerificationWindow <= TimeSpan.Zero ||
            configuration.TimeBudget <= TimeSpan.Zero || configuration.TimeBudget > HealingBudgetOptions.MaximumTimeBudget ||
            configuration.ConcurrencyBudget is < 1 or > HealingBudgetOptions.MaximumConcurrency ||
            configuration.InferenceBudget is < 0 or > HealingBudgetOptions.MaximumInferenceUnits ||
            configuration.RepositoryRunBudget is < 0 or > HealingBudgetOptions.MaximumRepositoryRuns ||
            !IsValidClassificationPolicy(configuration.ClassificationPolicyJson))
            return false;

        return configuration.Environments
            .GroupBy(x => x.EnvironmentId)
            .All(group => group.Key != Guid.Empty && group.Count() == 1) &&
               configuration.Environments.All(x =>
                   (x.OccurrenceThreshold is null or >= 1) &&
                   (x.DebounceWindow is null || x.DebounceWindow >= TimeSpan.Zero) &&
                   IsValidClassificationPolicy(x.ClassificationPolicyJson));
    }

    private static bool IsValidClassificationPolicy(string policyJson)
    {
        if (string.IsNullOrWhiteSpace(policyJson) || Encoding.UTF8.GetByteCount(policyJson) > 8_192)
            return false;
        try
        {
            using var document = JsonDocument.Parse(policyJson);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private ValueTask<HealingAuditEvent> AuditAsync(
        HealingConfiguration configuration,
        string eventType,
        string status,
        HealingAuthorization authorization,
        CancellationToken cancellationToken) =>
        auditService.AppendAsync(new HealingAuditWrite(
            configuration.WorkspaceId,
            "healing-configuration",
            configuration.Id,
            eventType,
            HealingOwnershipReasonCodes.Succeeded,
            HealingActorTypes.Human,
            authorization.ActorId,
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            new Dictionary<string, string?> { ["status"] = status }), cancellationToken);
}
