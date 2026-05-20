using Elsa.Platform.Deployment.Abstractions;
using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.History;
using Elsa.Platform.Deployment.Abstractions.Plans;
using Elsa.Platform.Deployment.Abstractions.Resources;
using Elsa.Platform.Deployment.Abstractions.Targets;

namespace Elsa.Platform.Deployment.Engine;

public sealed class DeploymentEngine : IDeploymentEngine
{
    private static readonly DeploymentArtifactIdentity UnknownArtifact = new(
        "unknown",
        "unknown",
        new ArtifactDigest("sha256", "unknown"),
        new ArtifactDigest("sha256", "unknown"));

    private readonly ResourceHandlerRegistry _handlers;
    private readonly IDeploymentHistoryStore _history;
    private readonly DeploymentEngineOptions _options;

    public DeploymentEngine(
        IEnumerable<IResourceHandler> handlers,
        IDeploymentHistoryStore? history = null,
        DeploymentEngineOptions? options = null)
    {
        _handlers = new ResourceHandlerRegistry(handlers);
        _history = history ?? new InMemoryDeploymentHistoryStore();
        _options = options ?? new DeploymentEngineOptions();
    }

    public async ValueTask<DeploymentResult> ValidateAsync(
        IArtifactReader artifact,
        IDeploymentTarget target,
        DeploymentExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        // Reserved for future validation-level options while keeping the engine contract uniform.
        _ = context;
        var startedAt = _options.Clock();
        var snapshot = await ReadArtifactAsync(artifact, cancellationToken);
        var diagnostics = new List<DeploymentDiagnostic>(snapshot.Diagnostics);
        var resources = snapshot.Resources;

        diagnostics.AddRange(_handlers.Diagnostics);
        diagnostics.AddRange(FindDuplicateDiagnostics(resources));

        foreach (var resource in resources.OrderBy(ResourceSortKey, StringComparer.Ordinal))
        {
            if (!_handlers.TryGetHandler(resource.Id.Type, out var handler))
            {
                diagnostics.Add(HandlerMissing(resource.Id));
                continue;
            }

            diagnostics.AddRange(await GuardAsync(
                () => handler.ValidateAsync(resource, target, cancellationToken),
                resource.Id,
                DeploymentEngineDiagnosticCodes.ValidateFailed,
                "Resource validation failed."));
        }

        var status = HasErrors(diagnostics) ? DeploymentStatus.ValidationFailed : DeploymentStatus.Validated;
        return CreateResult(
            DeploymentOperationMode.Validate,
            status,
            snapshot.Artifact,
            target,
            diagnostics: diagnostics,
            startedAt: startedAt);
    }

    public async ValueTask<DeploymentPlan> DiffAsync(
        IArtifactReader artifact,
        IDeploymentTarget target,
        DeploymentExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        context ??= DeploymentExecutionContext.Default;
        var snapshot = await ReadArtifactAsync(artifact, cancellationToken);
        var diagnostics = new List<DeploymentDiagnostic>(snapshot.Diagnostics);
        var changes = new List<DeploymentChange>();
        var resources = snapshot.Resources.OrderBy(ResourceSortKey, StringComparer.Ordinal).ToArray();

        diagnostics.AddRange(_handlers.Diagnostics);
        diagnostics.AddRange(FindDuplicateDiagnostics(resources));

        foreach (var resource in resources)
        {
            if (!_handlers.TryGetHandler(resource.Id.Type, out var handler))
            {
                var diagnostic = HandlerMissing(resource.Id);
                changes.Add(new DeploymentChange(resource.Id, DeploymentChangeAction.Unsupported, DeploymentChangeStatus.Blocked, diagnostic.Message, [diagnostic], resource));
                continue;
            }

            var validationDiagnostics = await GuardAsync(
                () => handler.ValidateAsync(resource, target, cancellationToken),
                resource.Id,
                DeploymentEngineDiagnosticCodes.ValidateFailed,
                "Resource validation failed.");
            diagnostics.AddRange(validationDiagnostics);
            if (HasErrors(validationDiagnostics))
            {
                changes.Add(new DeploymentChange(resource.Id, DeploymentChangeAction.Conflict, DeploymentChangeStatus.Blocked, "Resource validation failed.", validationDiagnostics, resource));
                continue;
            }

            var current = await GuardAsync(
                () => handler.ReadAsync(resource, target, cancellationToken),
                resource.Id,
                DeploymentEngineDiagnosticCodes.ReadFailed,
                "Current resource state could not be read.",
                diagnostics);
            if (HasResourceError(diagnostics, resource.Id))
                continue;

            var change = await GuardChangeAsync(
                () => handler.DiffAsync(resource, current, target, cancellationToken),
                resource.Id,
                DeploymentEngineDiagnosticCodes.DiffFailed,
                "Resource diff failed.",
                diagnostics);
            if (change is null || HasResourceError(diagnostics, resource.Id))
                continue;

            changes.Add(NormalizeChange(change, resource, context));
        }

        return new DeploymentPlan(
            _options.PlanIdFactory(),
            snapshot.Artifact,
            target.Descriptor,
            changes.OrderBy(change => change.ResourceId.ToString(), StringComparer.Ordinal),
            diagnostics,
            _options.Clock());
    }

    public async ValueTask<DeploymentResult> DryRunAsync(
        DeploymentPlan plan,
        IDeploymentTarget target,
        DeploymentExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        // Dry-run behavior is fully plan-derived in Phase 1; context is reserved for future options.
        _ = context;
        var startedAt = _options.Clock();
        var diagnostics = new List<DeploymentDiagnostic>(plan.Diagnostics);
        var resourceResults = new List<DeploymentResourceResult>();

        if (HasErrors(diagnostics))
        {
            resourceResults.AddRange(plan.Changes.Select(Skipped));
        }
        else
        {
            foreach (var change in plan.Changes.OrderBy(change => change.ResourceId.ToString(), StringComparer.Ordinal))
            {
                if (!IsApplyable(change))
                {
                    resourceResults.Add(Skipped(change));
                    continue;
                }

                if (change.Resource is null)
                {
                    var diagnostic = InvalidPlan(change.ResourceId, "Plan change is missing desired resource state.");
                    diagnostics.Add(diagnostic);
                    resourceResults.Add(Failed(change, retryable: false, [diagnostic]));
                    continue;
                }

                if (!_handlers.TryGetHandler(change.ResourceId.Type, out var handler))
                {
                    var diagnostic = HandlerMissing(change.ResourceId);
                    diagnostics.Add(diagnostic);
                    resourceResults.Add(Failed(change, retryable: false, [diagnostic]));
                    continue;
                }

                var resourceResult = await GuardResourceResultAsync(
                    () => handler.DryRunAsync(change, change.Resource, target, cancellationToken),
                    change,
                    DeploymentEngineDiagnosticCodes.DryRunFailed,
                    "Dry-run failed.");
                resourceResults.Add(resourceResult);
                diagnostics.AddRange(resourceResult.Diagnostics.Where(x => x.Severity >= DeploymentDiagnosticSeverity.Error));
            }
        }

        var status = HasErrors(diagnostics) ? DeploymentStatus.ValidationFailed :
            resourceResults.Any(result => result.Status != DeploymentChangeStatus.Skipped && IsApplyable(result.Action)) ? DeploymentStatus.DryRunCompleted : DeploymentStatus.NoOp;
        return CreateResult(
            DeploymentOperationMode.DryRun,
            status,
            plan.Artifact,
            target,
            plan,
            resourceResults,
            diagnostics,
            startedAt);
    }

    public async ValueTask<DeploymentResult> ApplyAsync(
        DeploymentPlan plan,
        IDeploymentTarget target,
        DeploymentExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        context ??= DeploymentExecutionContext.Default;
        var startedAt = _options.Clock();
        var diagnostics = new List<DeploymentDiagnostic>(plan.Diagnostics);
        var resourceResults = new List<DeploymentResourceResult>();

        if (HasErrors(diagnostics))
        {
            resourceResults.AddRange(plan.Changes.Select(Skipped));
        }
        else
        {
            foreach (var change in plan.Changes.OrderBy(change => change.ResourceId.ToString(), StringComparer.Ordinal))
            {
                if (!IsApplyable(change))
                {
                    resourceResults.Add(Skipped(change));
                    continue;
                }

                if (change.Resource is null)
                {
                    var diagnostic = InvalidPlan(change.ResourceId, "Plan change is missing desired resource state.");
                    diagnostics.Add(diagnostic);
                    resourceResults.Add(Failed(change, retryable: false, [diagnostic]));
                    continue;
                }

                if (!_handlers.TryGetHandler(change.ResourceId.Type, out var handler))
                {
                    var diagnostic = HandlerMissing(change.ResourceId);
                    diagnostics.Add(diagnostic);
                    resourceResults.Add(Failed(change, retryable: false, [diagnostic]));
                    continue;
                }

                var resourceResult = await GuardResourceResultAsync(
                    () => handler.ApplyAsync(change, change.Resource, target, cancellationToken),
                    change,
                    DeploymentEngineDiagnosticCodes.ApplyFailed,
                    "Apply failed.");
                resourceResults.Add(resourceResult);
                diagnostics.AddRange(resourceResult.Diagnostics.Where(x => x.Severity >= DeploymentDiagnosticSeverity.Error));
            }
        }

        var status = MapApplyStatus(resourceResults, diagnostics);
        var deploymentId = _options.DeploymentIdFactory();
        var completedAt = _options.Clock();
        var result = new DeploymentResult(
            deploymentId,
            DeploymentOperationMode.Apply,
            status,
            plan.Artifact,
            target.Descriptor,
            plan,
            resourceResults,
            diagnostics,
            startedAt,
            completedAt);

        try
        {
            await _history.RecordAsync(new DeploymentHistoryRecord(
                deploymentId,
                plan.Artifact,
                target.Descriptor,
                result.Status,
                context.Actor,
                plan,
                result.ResourceResults,
                result.Diagnostics,
                startedAt,
                completedAt), cancellationToken);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add(new DeploymentDiagnostic(
                DeploymentEngineDiagnosticCodes.HistoryFailed,
                DeploymentDiagnosticSeverity.Error,
                ex.Message));

            return new DeploymentResult(
                deploymentId,
                DeploymentOperationMode.Apply,
                MapHistoryFailureStatus(status),
                plan.Artifact,
                target.Descriptor,
                plan,
                resourceResults,
                diagnostics,
                startedAt,
                completedAt);
        }
    }

    private async ValueTask<ArtifactSnapshot> ReadArtifactAsync(IArtifactReader artifact, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await artifact.ReadMetadataAsync(cancellationToken);
            var resources = await artifact.ReadResourcesAsync(cancellationToken);
            return new ArtifactSnapshot(metadata.Identity, resources ?? [], []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ArtifactSnapshot(
                UnknownArtifact,
                [],
                [new DeploymentDiagnostic(DeploymentEngineDiagnosticCodes.ArtifactInvalid, DeploymentDiagnosticSeverity.Error, ex.Message)]);
        }
    }

    private static IReadOnlyCollection<DeploymentDiagnostic> FindDuplicateDiagnostics(IReadOnlyCollection<DeploymentResource> resources) =>
        resources
            .GroupBy(resource => resource.Id)
            .Where(group => group.Count() > 1)
            .Select(group => new DeploymentDiagnostic(
                DeploymentEngineDiagnosticCodes.ResourceDuplicate,
                DeploymentDiagnosticSeverity.Error,
                $"Deployment resource '{group.Key}' appears more than once.",
                group.Key))
            .ToArray();

    private static DeploymentChange NormalizeChange(DeploymentChange change, DeploymentResource resource, DeploymentExecutionContext context)
    {
        if (change.Action == DeploymentChangeAction.Delete && !context.Prune)
        {
            var diagnostic = new DeploymentDiagnostic(
                DeploymentEngineDiagnosticCodes.PruneDisabled,
                DeploymentDiagnosticSeverity.Error,
                $"Delete change for resource '{change.ResourceId}' requires pruning to be enabled.",
                change.ResourceId);
            return new DeploymentChange(change.ResourceId, change.Action, DeploymentChangeStatus.Blocked, diagnostic.Message, [diagnostic, ..change.Diagnostics], resource);
        }

        return new DeploymentChange(
            change.ResourceId,
            change.Action,
            change.Status,
            change.Reason,
            change.Diagnostics,
            resource);
    }

    private DeploymentResult CreateResult(
        DeploymentOperationMode mode,
        DeploymentStatus status,
        DeploymentArtifactIdentity artifact,
        IDeploymentTarget target,
        DeploymentPlan? plan = null,
        IEnumerable<DeploymentResourceResult>? resourceResults = null,
        IEnumerable<DeploymentDiagnostic>? diagnostics = null,
        DateTimeOffset? startedAt = null) =>
        new(
            _options.DeploymentIdFactory(),
            mode,
            status,
            artifact,
            target.Descriptor,
            plan,
            resourceResults,
            diagnostics,
            startedAt,
            _options.Clock());

    private static DeploymentDiagnostic HandlerMissing(DeploymentResourceId resourceId) =>
        new(
            DeploymentEngineDiagnosticCodes.HandlerMissing,
            DeploymentDiagnosticSeverity.Error,
            $"No deployment resource handler is registered for resource type '{resourceId.Type}'.",
            resourceId);

    private static DeploymentDiagnostic InvalidPlan(DeploymentResourceId resourceId, string message) =>
        new(DeploymentEngineDiagnosticCodes.PlanInvalid, DeploymentDiagnosticSeverity.Error, message, resourceId);

    private static bool HasErrors(IEnumerable<DeploymentDiagnostic> diagnostics) =>
        diagnostics.Any(x => x.Severity >= DeploymentDiagnosticSeverity.Error);

    private static bool HasResourceError(IEnumerable<DeploymentDiagnostic> diagnostics, DeploymentResourceId resourceId) =>
        diagnostics.Any(x => x.ResourceId == resourceId && x.Severity >= DeploymentDiagnosticSeverity.Error);

    private static bool IsApplyable(DeploymentChange change) =>
        change.Status is not DeploymentChangeStatus.Blocked &&
        IsApplyable(change.Action);

    private static bool IsApplyable(DeploymentChangeAction action) =>
        action is DeploymentChangeAction.Create or DeploymentChangeAction.Update or DeploymentChangeAction.Activate or DeploymentChangeAction.Deactivate or DeploymentChangeAction.Delete;

    private static DeploymentResourceResult Skipped(DeploymentChange change) =>
        new(change.ResourceId, change.Action, DeploymentChangeStatus.Skipped);

    private static DeploymentResourceResult Failed(
        DeploymentChange change,
        bool retryable,
        IEnumerable<DeploymentDiagnostic> diagnostics) =>
        new(change.ResourceId, change.Action, DeploymentChangeStatus.Failed, retryable, diagnostics);

    private static DeploymentStatus MapApplyStatus(
        IReadOnlyCollection<DeploymentResourceResult> resourceResults,
        IReadOnlyCollection<DeploymentDiagnostic> diagnostics)
    {
        var applyableResults = resourceResults.Where(result => result.Status != DeploymentChangeStatus.Skipped && IsApplyable(result.Action)).ToArray();
        if (applyableResults.Length == 0)
            return HasErrors(diagnostics) ? DeploymentStatus.Failed : DeploymentStatus.NoOp;

        var completed = applyableResults.Count(result => result.Status == DeploymentChangeStatus.Completed);
        var failed = applyableResults.Count(result => result.Status == DeploymentChangeStatus.Failed);
        if (failed == 0 && HasErrors(diagnostics))
            return completed > 0 ? DeploymentStatus.PartiallyApplied : DeploymentStatus.Failed;

        if (failed == 0)
            return DeploymentStatus.Applied;

        return failed == applyableResults.Length ? DeploymentStatus.Failed : DeploymentStatus.PartiallyApplied;
    }

    private static DeploymentStatus MapHistoryFailureStatus(DeploymentStatus status) =>
        status is DeploymentStatus.Applied or DeploymentStatus.NoOp
            ? DeploymentStatus.CompletedWithWarnings
            : status;

    private static async ValueTask<IReadOnlyCollection<DeploymentDiagnostic>> GuardAsync(
        Func<ValueTask<IReadOnlyCollection<DeploymentDiagnostic>>> action,
        DeploymentResourceId resourceId,
        string diagnosticCode,
        string message)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [new DeploymentDiagnostic(diagnosticCode, DeploymentDiagnosticSeverity.Error, $"{message} {ex.Message}", resourceId)];
        }
    }

    private static async ValueTask<T?> GuardAsync<T>(
        Func<ValueTask<T?>> action,
        DeploymentResourceId resourceId,
        string diagnosticCode,
        string message,
        ICollection<DeploymentDiagnostic> diagnostics)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add(new DeploymentDiagnostic(diagnosticCode, DeploymentDiagnosticSeverity.Error, $"{message} {ex.Message}", resourceId));
            return default;
        }
    }

    private static async ValueTask<DeploymentChange?> GuardChangeAsync(
        Func<ValueTask<DeploymentChange>> action,
        DeploymentResourceId resourceId,
        string diagnosticCode,
        string message,
        ICollection<DeploymentDiagnostic> diagnostics)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add(new DeploymentDiagnostic(diagnosticCode, DeploymentDiagnosticSeverity.Error, $"{message} {ex.Message}", resourceId));
            return default;
        }
    }

    private static async ValueTask<DeploymentResourceResult> GuardResourceResultAsync(
        Func<ValueTask<DeploymentResourceResult>> action,
        DeploymentChange change,
        string diagnosticCode,
        string message)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var diagnostic = new DeploymentDiagnostic(
                diagnosticCode,
                DeploymentDiagnosticSeverity.Error,
                $"{message} {ex.Message}",
                change.ResourceId);
            return Failed(change, retryable: true, [diagnostic]);
        }
    }

    private static string ResourceSortKey(DeploymentResource resource) => resource.Id.ToString();

    private sealed record ArtifactSnapshot(
        DeploymentArtifactIdentity Artifact,
        IReadOnlyCollection<DeploymentResource> Resources,
        IReadOnlyCollection<DeploymentDiagnostic> Diagnostics);
}
