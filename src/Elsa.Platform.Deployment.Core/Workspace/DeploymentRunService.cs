using System.Text.Json;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class DeploymentRunService(
    IWorkspaceDeploymentMutationStore? store = null,
    ConfirmationService? confirmations = null,
    TimeProvider? timeProvider = null,
    IArtifactTypeRegistry? artifactTypes = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IArtifactTypeRegistry _artifactTypes = artifactTypes ?? new ArtifactTypeRegistry();

    public DeploymentValidation ValidateRunRequest(WorkspaceDeploymentRunRequest request)
    {
        return request.Mode switch
        {
            DeploymentRunMode.DryRun or DeploymentRunMode.Apply => new DeploymentValidation("deployment.run.request.valid", ValidationSeverity.Pass, "Deployment run", "Deployment run request is valid."),
            _ => new DeploymentValidation("deployment.run.mode.invalid", ValidationSeverity.Blocker, "Deployment run", "Deployment run mode is not supported.")
        };
    }

    public Task<WorkspaceDeploymentRun> QueueDeploymentAsync(
        Guid workspaceId,
        WorkspaceDeploymentRunRequest request,
        Guid confirmationId,
        CancellationToken cancellationToken = default) =>
        QueueRunAsync(workspaceId, request, confirmationId, ConfirmationActionType.Deploy, null, cancellationToken);

    public Task<WorkspaceDeploymentRun> QueueRollbackAsync(
        Guid workspaceId,
        WorkspaceDeploymentRunRequest request,
        Guid confirmationId,
        Guid rollbackSourceRunId,
        CancellationToken cancellationToken = default) =>
        QueueRunAsync(workspaceId, request, confirmationId, ConfirmationActionType.Rollback, rollbackSourceRunId, cancellationToken);

    public async Task<WorkspaceDeploymentRunDetail?> GetRunDetailAsync(
        Guid workspaceId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        if (store is null)
            throw new InvalidOperationException("Deployment run persistence is not configured.");

        var run = await store.GetRunAsync(workspaceId, runId, cancellationToken);
        if (run is null)
            return null;

        var history = await store.GetRunHistoryAsync(workspaceId, runId, cancellationToken);
        var commands = await store.GetRunCommandSummariesAsync(workspaceId, runId, cancellationToken);
        return new WorkspaceDeploymentRunDetail(run, history, commands);
    }

    private async Task<WorkspaceDeploymentRun> QueueRunAsync(
        Guid workspaceId,
        WorkspaceDeploymentRunRequest request,
        Guid confirmationId,
        ConfirmationActionType actionType,
        Guid? rollbackSourceRunId,
        CancellationToken cancellationToken)
    {
        if (store is null || confirmations is null)
            throw new InvalidOperationException("Deployment run persistence is not configured.");

        var requestValidation = ValidateRunRequest(request);
        if (requestValidation.Severity == ValidationSeverity.Blocker)
            throw new InvalidOperationException(requestValidation.Message);

        if (await store.HasActiveRunAsync(workspaceId, request.TargetEnvironmentId, cancellationToken))
            throw new InvalidOperationException("An active deployment run already exists for the target environment.");

        var targetEnvironment = await GetTargetEnvironmentAsync(workspaceId, request.TargetEnvironmentId, cancellationToken);
        ValidateTierCapabilities(targetEnvironment, actionType);
        await ValidateArtifactBackedRevisionAsync(workspaceId, request, cancellationToken);

        var confirmation = await confirmations.ConsumeConfirmationAsync(
            workspaceId,
            confirmationId,
            request.ActorAccountId,
            actionType,
            request.SourceRevisionId.ToString("D"),
            cancellationToken);
        if (!confirmation.Succeeded)
            throw new InvalidOperationException(confirmation.Validation.Message);

        return await store.CreateRunAsync(
            workspaceId,
            new QueueWorkspaceDeploymentRunRequest(
                request.SourceRevisionId,
                request.TargetEnvironmentId,
                request.TargetEngineId,
                confirmationId,
                request.ActorAccountId,
                rollbackSourceRunId),
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private async Task<EnvironmentSummary?> GetTargetEnvironmentAsync(
        Guid workspaceId,
        Guid targetEnvironmentId,
        CancellationToken cancellationToken)
    {
        if (store is not IWorkspaceDeploymentStore deploymentStore)
            return null;

        var cockpit = await deploymentStore.GetCockpitAsync(workspaceId, cancellationToken);
        return cockpit.Applications
            .SelectMany(x => x.Environments)
            .SingleOrDefault(x => string.Equals(x.Id, targetEnvironmentId.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateTierCapabilities(EnvironmentSummary? targetEnvironment, ConfirmationActionType actionType)
    {
        if (targetEnvironment is null)
            return;

        if (actionType == ConfirmationActionType.Rollback && !DeploymentTierService.CanRollback(targetEnvironment))
            throw new InvalidOperationException($"{TierLabel(targetEnvironment)} does not allow rollback actions.");
    }

    private static string TierLabel(EnvironmentSummary environment) =>
        string.IsNullOrWhiteSpace(environment.TierName) ? environment.Tier.ToString() : environment.TierName;

    private async Task ValidateArtifactBackedRevisionAsync(
        Guid workspaceId,
        WorkspaceDeploymentRunRequest request,
        CancellationToken cancellationToken)
    {
        if (store is not IWorkspaceDeploymentStore deploymentStore || store is not IWorkspaceArtifactStore artifactStore)
            return;

        var revision = await deploymentStore.GetRevisionAsync(workspaceId, request.SourceRevisionId, cancellationToken)
            ?? throw new InvalidOperationException("Source revision does not exist in the workspace.");
        var references = ParseArtifactReferences(revision.DesiredStateJson);
        if (references.Count == 0)
            return;

        var engine = await GetTargetEngineRegistrationAsync(deploymentStore, workspaceId, request.TargetEnvironmentId, request.TargetEngineId, cancellationToken)
            ?? throw new InvalidOperationException("Target engine is not visible in this workspace.");
        var engineCapabilities = engine.Capabilities.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in references)
        {
            var artifact = await ResolveArtifactAsync(artifactStore, workspaceId, reference, cancellationToken);
            ValidateArtifactReference(reference, artifact, engineCapabilities);
        }
    }

    private static async Task<WorkflowEngineRegistration?> GetTargetEngineRegistrationAsync(
        IWorkspaceDeploymentStore deploymentStore,
        Guid workspaceId,
        Guid targetEnvironmentId,
        Guid engineId,
        CancellationToken cancellationToken)
    {
        var cockpit = await deploymentStore.GetCockpitAsync(workspaceId, cancellationToken);
        return cockpit.Engines.SingleOrDefault(x =>
            string.Equals(x.Id, engineId.ToString("D"), StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.EnvironmentId, targetEnvironmentId.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<WorkspaceArtifact> ResolveArtifactAsync(
        IWorkspaceArtifactStore artifactStore,
        Guid workspaceId,
        DesiredStateArtifactReference reference,
        CancellationToken cancellationToken)
    {
        WorkspaceArtifact? artifact = null;
        if (reference.ArtifactRecordId is not null)
            artifact = await artifactStore.GetArtifactAsync(workspaceId, reference.ArtifactRecordId.Value, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(reference.ArtifactId))
            artifact = await artifactStore.FindArtifactByIdentityAsync(workspaceId, reference.ArtifactId, cancellationToken);

        return artifact ?? throw new InvalidOperationException($"{reference.Name} references an artifact that is not visible in this workspace.");
    }

    private void ValidateArtifactReference(
        DesiredStateArtifactReference reference,
        WorkspaceArtifact artifact,
        IReadOnlySet<string> engineCapabilities)
    {
        if (reference.ArtifactRecordId is not null && reference.ArtifactRecordId != artifact.Id)
            throw new InvalidOperationException($"{reference.Name} artifact record does not match the registered artifact.");
        if (!string.IsNullOrWhiteSpace(reference.ArtifactId) && !string.Equals(reference.ArtifactId, artifact.ArtifactId, StringComparison.Ordinal))
            throw new InvalidOperationException($"{reference.Name} artifact identity does not match the registered artifact.");
        if (string.IsNullOrWhiteSpace(reference.ArtifactTypeId))
            throw new InvalidOperationException($"{reference.Name} artifact type is missing.");
        var artifactType = _artifactTypes.FindType(reference.ArtifactTypeId);
        if (artifactType is null)
            throw new InvalidOperationException($"{reference.Name} artifact type is not supported.");
        if (!string.Equals(reference.ArtifactTypeId, artifact.ArtifactTypeId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{reference.Name} artifact type does not match the registered artifact.");
        if (reference.ContentDigest is null)
            throw new InvalidOperationException($"{reference.Name} artifact digest is missing.");
        if (!string.Equals(reference.ContentDigest.Algorithm, artifact.ContentDigest.Algorithm, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(reference.ContentDigest.Value, artifact.ContentDigest.Value, StringComparison.Ordinal))
            throw new InvalidOperationException($"{reference.Name} artifact digest does not match the registered artifact.");
        if (artifact.PayloadReference is null
            || string.IsNullOrWhiteSpace(artifact.PayloadReference.Provider)
            || string.IsNullOrWhiteSpace(artifact.PayloadReference.Uri))
            throw new InvalidOperationException($"{reference.Name} artifact payload reference is unavailable.");

        var requiredCapabilities = (artifact.CompatibilityHints ?? [])
            .Where(x => string.Equals(x.RequiredArtifactType, artifact.ArtifactTypeId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.RequiredCapabilities)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requiredCapabilities.Count == 0 && artifactType.DefaultRequiredCapabilities is not null)
            requiredCapabilities = artifactType.DefaultRequiredCapabilities
                .Where(capability => !string.IsNullOrWhiteSpace(capability))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        var missingCapability = requiredCapabilities
            .FirstOrDefault(capability => !engineCapabilities.Contains(capability));
        if (!string.IsNullOrWhiteSpace(missingCapability))
            throw new InvalidOperationException($"{reference.Name} requires runtime capability {missingCapability}.");
    }

    private static IReadOnlyList<DesiredStateArtifactReference> ParseArtifactReferences(string desiredStateJson)
    {
        try
        {
            using var document = JsonDocument.Parse(desiredStateJson);
            var records = document.RootElement.TryGetProperty("records", out var recordsElement) && recordsElement.ValueKind == JsonValueKind.Array
                ? recordsElement
                : document.RootElement;
            if (records.ValueKind != JsonValueKind.Array)
                return [];

            return records.EnumerateArray()
                .Select(ParseArtifactReference)
                .Where(reference => reference is not null)
                .Cast<DesiredStateArtifactReference>()
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static DesiredStateArtifactReference? ParseArtifactReference(JsonElement record)
    {
        var kind = record.TryGetProperty("kind", out var kindElement) && kindElement.ValueKind == JsonValueKind.String
            ? kindElement.GetString()
            : null;
        if (!string.Equals(kind, DesiredStateRecordKind.ArtifactReference.ToString(), StringComparison.OrdinalIgnoreCase))
            return null;

        var name = record.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString() ?? "Artifact"
            : "Artifact";
        var payload = record.TryGetProperty("payload", out var payloadElement) && payloadElement.ValueKind == JsonValueKind.Object
            ? payloadElement
            : record;
        var artifactRecordId = payload.TryGetProperty("artifactRecordId", out var artifactRecordIdElement)
            && artifactRecordIdElement.ValueKind == JsonValueKind.String
            && Guid.TryParse(artifactRecordIdElement.GetString(), out var parsedArtifactRecordId)
                ? parsedArtifactRecordId
                : (Guid?)null;
        var artifactId = GetString(payload, "artifactId");
        var artifactTypeId = GetString(payload, "artifactTypeId");
        var digest = payload.TryGetProperty("contentDigest", out var digestElement) && digestElement.ValueKind == JsonValueKind.Object
            ? ParseDigest(digestElement)
            : null;

        return new DesiredStateArtifactReference(name, artifactRecordId, artifactId, artifactTypeId, digest);
    }

    private static WorkspaceArtifactDigest? ParseDigest(JsonElement digestElement)
    {
        var algorithm = GetString(digestElement, "algorithm");
        var value = GetString(digestElement, "value");
        return string.IsNullOrWhiteSpace(algorithm) || string.IsNullOrWhiteSpace(value)
            ? null
            : new WorkspaceArtifactDigest(algorithm, value);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private sealed record DesiredStateArtifactReference(
        string Name,
        Guid? ArtifactRecordId,
        string? ArtifactId,
        string? ArtifactTypeId,
        WorkspaceArtifactDigest? ContentDigest);
}
