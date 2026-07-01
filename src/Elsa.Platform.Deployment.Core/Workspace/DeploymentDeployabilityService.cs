using System.Text.Json;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class DeploymentDeployabilityService(
    IWorkspaceDeploymentStore deploymentStore,
    IWorkspaceArtifactStore artifactStore,
    IArtifactTypeRegistry? artifactTypes = null,
    TimeProvider? timeProvider = null)
{
    private readonly IArtifactTypeRegistry _artifactTypes = artifactTypes ?? new ArtifactTypeRegistry();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<DeploymentDeployabilityResult> EvaluateAsync(
        Guid workspaceId,
        Guid revisionId,
        DeploymentDeployabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var revision = await deploymentStore.GetRevisionAsync(workspaceId, revisionId, cancellationToken)
            ?? throw new KeyNotFoundException("Revision does not exist in the workspace.");
        var cockpit = await deploymentStore.GetCockpitAsync(workspaceId, cancellationToken);
        var environment = cockpit.Applications
            .SelectMany(x => x.Environments)
            .SingleOrDefault(x => string.Equals(x.Id, request.TargetEnvironmentId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        var engine = cockpit.Engines.SingleOrDefault(x => string.Equals(x.Id, request.TargetEngineId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        var blockers = new List<DeploymentBlocker>();

        if (revision.EnvironmentId != request.TargetEnvironmentId)
            blockers.Add(Blocker("deployment.environment.mismatch", DeploymentBlockerScope.Engine, "Target environment does not match the revision environment.", "Use promotion to create a revision for the target environment before deploying.", engineId: request.TargetEngineId));
        if (environment is null)
            blockers.Add(Blocker("deployment.environment.missing", DeploymentBlockerScope.Engine, "Target environment is not visible in this workspace.", "Refresh deployment data or select a visible environment.", engineId: request.TargetEngineId));
        if (engine is null)
            blockers.Add(Blocker("deployment.engine.missing", DeploymentBlockerScope.Engine, "Target engine is not visible in this workspace.", "Register a compatible engine in the target environment.", engineId: request.TargetEngineId));
        else if (!string.Equals(engine.EnvironmentId, request.TargetEnvironmentId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            blockers.Add(Blocker("deployment.engine.environment-mismatch", DeploymentBlockerScope.Engine, "Target engine does not belong to the target environment.", "Select an engine registered in the revision environment.", engineId: request.TargetEngineId));

        var artifactResults = new List<ArtifactDeployabilityResult>();
        foreach (var reference in ParseArtifactReferences(revision.DesiredStateJson))
        {
            var result = await EvaluateArtifactAsync(workspaceId, reference, engine, cancellationToken);
            artifactResults.Add(result);
            blockers.AddRange(result.Diagnostics
                .Where(x => x.Severity == ValidationSeverity.Blocker)
                .Select(x => Blocker(
                    x.Id,
                    ScopeFor(x.Id),
                    x.Message,
                    RemediationFor(x.Id),
                    result.ArtifactRecordId,
                    request.TargetEngineId)));
        }

        if (engine is not null && engine.Capabilities.Count == 0)
            blockers.Add(Blocker("engine.capabilities.missing", DeploymentBlockerScope.EngineCapabilities, "Target engine has not reported runtime capabilities.", "Reconnect the engine or refresh its heartbeat before deploying.", engineId: request.TargetEngineId));
        if (engine is not null && engine.LastHeartbeatAt is not null && engine.LastHeartbeatAt < now.Subtract(TimeSpan.FromMinutes(15)))
            blockers.Add(Blocker("engine.capabilities.stale", DeploymentBlockerScope.EngineCapabilities, "Target engine capability metadata is stale.", "Reconnect the engine or wait for a fresh heartbeat before deploying.", engineId: request.TargetEngineId));

        var status = blockers.Any(x => x.Severity == ValidationSeverity.Blocker) || artifactResults.Any(x => x.Status == DeploymentDeployabilityStatus.Blocked)
            ? DeploymentDeployabilityStatus.Blocked
            : artifactResults.Any(x => x.Status == DeploymentDeployabilityStatus.Warning)
                ? DeploymentDeployabilityStatus.Warning
                : DeploymentDeployabilityStatus.Deployable;

        return new DeploymentDeployabilityResult(
            workspaceId,
            revisionId,
            request.TargetEnvironmentId,
            request.TargetEngineId,
            request.Mode,
            status,
            now,
            artifactResults,
            blockers
                .GroupBy(x => (x.Id, x.ArtifactRecordId, x.EngineId))
                .Select(x => x.First())
                .ToList());
    }

    public static IReadOnlyList<DesiredStateArtifactReference> ParseArtifactReferences(string desiredStateJson)
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

    public static IReadOnlyList<string> RequiredCapabilities(WorkspaceArtifact artifact, ArtifactTypeDefinition? artifactType)
    {
        var artifactTypeId = artifact.ArtifactTypeId ?? ArtifactTypeIds.ElsaLoomRecipe;
        var requiredCapabilities = (artifact.CompatibilityHints ?? [])
            .Where(x => string.Equals(x.RequiredArtifactType, artifactTypeId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.RequiredCapabilities)
            .Select(capability => ArtifactApplyCapability.Normalize(artifactTypeId, capability))
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requiredCapabilities.Count == 0 && artifactType?.DefaultRequiredCapabilities is not null)
            requiredCapabilities = artifactType.DefaultRequiredCapabilities
                .Select(capability => ArtifactApplyCapability.Normalize(artifactTypeId, capability))
                .Where(capability => !string.IsNullOrWhiteSpace(capability))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        return requiredCapabilities;
    }

    private async Task<ArtifactDeployabilityResult> EvaluateArtifactAsync(
        Guid workspaceId,
        DesiredStateArtifactReference reference,
        WorkflowEngineRegistration? engine,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<DeploymentValidation>();
        WorkspaceArtifact? artifact = null;
        if (reference.ArtifactRecordId is not null)
            artifact = await artifactStore.GetArtifactAsync(workspaceId, reference.ArtifactRecordId.Value, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(reference.ArtifactId))
            artifact = await artifactStore.FindArtifactByIdentityAsync(workspaceId, reference.ArtifactId, cancellationToken);

        if (artifact is null)
            return BlockedArtifact(reference, diagnostics, "artifact.reference.missing", $"{reference.Name} references an artifact that is not visible in this workspace.");

        var artifactTypeId = artifact.ArtifactTypeId ?? ArtifactTypeIds.ElsaLoomRecipe;
        var artifactType = _artifactTypes.FindType(artifactTypeId);
        if (reference.ArtifactRecordId is not null && reference.ArtifactRecordId != artifact.Id)
            diagnostics.Add(Validation("artifact.record.mismatch", $"{reference.Name} artifact record does not match the registered artifact."));
        if (!string.IsNullOrWhiteSpace(reference.ArtifactId) && !string.Equals(reference.ArtifactId, artifact.ArtifactId, StringComparison.Ordinal))
            diagnostics.Add(Validation("artifact.identity.mismatch", $"{reference.Name} artifact identity does not match the registered artifact."));
        if (string.IsNullOrWhiteSpace(reference.ArtifactTypeId))
            diagnostics.Add(Validation("artifact.type.missing", $"{reference.Name} artifact type is missing."));
        if (artifactType is null)
            diagnostics.Add(Validation("artifact.type.unsupported", $"{reference.Name} artifact type is not supported."));
        if (!string.IsNullOrWhiteSpace(reference.ArtifactTypeId) && !string.Equals(reference.ArtifactTypeId, artifactTypeId, StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(Validation("artifact.type.mismatch", $"{reference.Name} artifact type does not match the registered artifact."));
        if (artifactType is not null && !artifactType.SupportedSchemaVersions.Contains(artifact.ArtifactSchemaVersion ?? "", StringComparer.OrdinalIgnoreCase))
            diagnostics.Add(Validation("artifact.schema.unsupported", $"{reference.Name} artifact schema version is not supported."));
        if (reference.ContentDigest is null)
            diagnostics.Add(Validation("artifact.digest.missing", $"{reference.Name} artifact digest is missing."));
        else if (!string.Equals(reference.ContentDigest.Algorithm, artifact.ContentDigest.Algorithm, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(reference.ContentDigest.Value, artifact.ContentDigest.Value, StringComparison.Ordinal))
            diagnostics.Add(Validation("artifact.digest.mismatch", $"{reference.Name} artifact digest does not match the registered artifact."));
        if (artifact.Status == WorkspaceArtifactLifecycleStatus.Archived)
            diagnostics.Add(Validation("artifact.archived", $"{reference.Name} artifact is archived."));
        if (artifact.InspectionStatus is WorkspaceArtifactInspectionStatus.Invalid or WorkspaceArtifactInspectionStatus.Unavailable or WorkspaceArtifactInspectionStatus.Unsupported)
            diagnostics.Add(Validation("artifact.inspection.invalid", $"{reference.Name} artifact inspection is not valid."));

        var payloadAvailable = artifact.PayloadReference is not null
            && !string.IsNullOrWhiteSpace(artifact.PayloadReference.Provider)
            && !string.IsNullOrWhiteSpace(artifact.PayloadReference.Uri);
        if (!payloadAvailable)
            diagnostics.Add(Validation("artifact.payload.unavailable", $"{reference.Name} artifact payload reference is unavailable."));

        var requiredCapabilities = RequiredCapabilities(artifact, artifactType);
        var engineCapabilities = (engine?.Capabilities ?? [])
            .Select(x => ArtifactApplyCapability.Normalize(artifactTypeId, x.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingCapabilities = engine is null
            ? requiredCapabilities
            : requiredCapabilities.Where(capability => !engineCapabilities.Contains(capability)).ToList();
        diagnostics.AddRange(missingCapabilities.Select(capability => Validation("artifact.capability.missing", $"{reference.Name} requires runtime capability {capability}.")));

        var status = diagnostics.Any(x => x.Severity == ValidationSeverity.Blocker)
            ? DeploymentDeployabilityStatus.Blocked
            : DeploymentDeployabilityStatus.Deployable;
        if (status == DeploymentDeployabilityStatus.Deployable)
            diagnostics.Add(new DeploymentValidation("artifact.deployable", ValidationSeverity.Pass, "Artifact", $"{reference.Name} can be applied by the target engine."));

        return new ArtifactDeployabilityResult(
            artifact.Id,
            reference.Name,
            artifact.ArtifactId,
            artifactTypeId,
            artifact.ArtifactSchemaVersion,
            artifact.ContentDigest,
            status,
            requiredCapabilities,
            missingCapabilities,
            payloadAvailable,
            diagnostics);
    }

    private static ArtifactDeployabilityResult BlockedArtifact(
        DesiredStateArtifactReference reference,
        List<DeploymentValidation> diagnostics,
        string code,
        string message)
    {
        diagnostics.Add(Validation(code, message));
        return new ArtifactDeployabilityResult(
            reference.ArtifactRecordId,
            reference.Name,
            reference.ArtifactId,
            reference.ArtifactTypeId,
            null,
            reference.ContentDigest,
            DeploymentDeployabilityStatus.Blocked,
            [],
            [],
            false,
            diagnostics);
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
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DeploymentValidation Validation(string id, string message) =>
        new(id, ValidationSeverity.Blocker, ScopeFor(id).ToString(), message);

    private static DeploymentBlocker Blocker(
        string id,
        DeploymentBlockerScope scope,
        string message,
        string remediation,
        Guid? artifactRecordId = null,
        Guid? engineId = null) =>
        new(id, ValidationSeverity.Blocker, scope, message, remediation, artifactRecordId, engineId);

    private static DeploymentBlockerScope ScopeFor(string id) =>
        id switch
        {
            var value when value.Contains("capability", StringComparison.OrdinalIgnoreCase) => DeploymentBlockerScope.EngineCapabilities,
            var value when value.Contains("payload", StringComparison.OrdinalIgnoreCase) => DeploymentBlockerScope.Payload,
            var value when value.Contains("engine", StringComparison.OrdinalIgnoreCase) => DeploymentBlockerScope.Engine,
            _ => DeploymentBlockerScope.Artifact
        };

    private static string RemediationFor(string id) =>
        id switch
        {
            "artifact.capability.missing" => "Refresh the engine heartbeat or install the runtime applier for the missing capability.",
            "engine.capabilities.missing" => "Reconnect the engine or refresh its heartbeat before deploying.",
            "engine.capabilities.stale" => "Reconnect the engine or wait for a fresh heartbeat before deploying.",
            "artifact.payload.unavailable" => "Refresh artifact inspection, restore the artifact, or fix artifact storage access.",
            "artifact.archived" => "Restore the artifact or create a new revision using an active artifact.",
            "artifact.schema.unsupported" => "Use a compatible artifact version or upgrade the runtime applier.",
            _ => "Review the deployment prerequisite and update the artifact, engine, or revision before deploying."
        };
}
