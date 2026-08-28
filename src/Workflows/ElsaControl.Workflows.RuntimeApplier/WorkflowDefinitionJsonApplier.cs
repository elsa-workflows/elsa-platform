using System.Text.Json;
using ElsaControl.Deployment.Abstractions.Artifacts;
using ElsaControl.Deployment.Artifacts;

namespace ElsaControl.Workflows.RuntimeApplier;

public sealed class WorkflowDefinitionJsonApplier(IWorkflowDefinitionRuntimeStore store) : IWorkflowDefinitionApplier
{
    public async Task<WorkflowArtifactApplyResult> ApplyAsync(
        WorkflowArtifactApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        // Resolve every workflow definition to apply BEFORE saving any of them, so a malformed
        // step can never leave the store partially populated (atomic admission). A loom recipe
        // may carry N upsert steps; a plain workflow-definition artifact is a single definition.
        IReadOnlyList<WorkflowDefinitionToApply> definitions;
        if (request.Envelope.ArtifactTypeId.Equals(ArtifactTypeIds.ElsaLoomRecipe, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadRecipeDefinitions(request.WorkflowDefinitionJson, out definitions, out var recipeError))
                return Rejected(request.ObservedDigest, recipeError);
        }
        else
        {
            if (!TryReadWorkflowDefinition(request.WorkflowDefinitionJson, out var definition, out var error))
                return Rejected(request.ObservedDigest, error);
            definitions = [definition];
        }

        // Apply each definition. The steps are idempotent upserts, so on a mid-batch store fault
        // the command is re-driven and the whole set re-applies to convergence — we deliberately
        // do NOT compensate by deleting earlier saves, because an upsert may have updated a
        // pre-existing definition and deleting it would destroy state the retry cannot restore.
        var references = new List<string>(definitions.Count);
        var diagnostics = new List<WorkflowArtifactDiagnostic>();
        foreach (var definition in definitions)
        {
            try
            {
                var result = await store.SaveAsync(
                    new WorkflowDefinitionRuntimeStoreRequest(
                        definition.Id,
                        definition.Json,
                        request.Envelope,
                        request.ObservedDigest),
                    cancellationToken);

                references.Add(result.RuntimeReference);
                diagnostics.AddRange(result.Diagnostics);
            }
            catch (InvalidOperationException ex)
            {
                return Rejected(request.ObservedDigest, ex.Message);
            }
        }

        if (references.Count > 1)
        {
            diagnostics.Add(new WorkflowArtifactDiagnostic(
                "workflow-artifact.applied-multiple",
                WorkflowArtifactDiagnosticSeverity.Info,
                $"Applied {references.Count} workflow definitions: {string.Join(", ", references)}."));
        }

        return new WorkflowArtifactApplyResult(
            WorkflowArtifactApplyStatus.Applied,
            request.ObservedDigest,
            references.Count > 0 ? references[0] : null,
            Safe(diagnostics));
    }

    private static bool TryReadRecipeDefinitions(
        string recipeJson,
        out IReadOnlyList<WorkflowDefinitionToApply> definitions,
        out string error)
    {
        definitions = [];
        error = "";
        try
        {
            using var document = JsonDocument.Parse(recipeJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Loom recipe payload is not a JSON object.";
                return false;
            }

            if (!TryReadRecipeSchemaVersion(root, out error))
                return false;

            if (!root.TryGetProperty(LoomRecipeContract.StepsProperty, out var steps) || steps.ValueKind != JsonValueKind.Array)
            {
                error = "Loom recipe payload does not include a steps collection.";
                return false;
            }

            var resolved = new List<WorkflowDefinitionToApply>();
            foreach (var step in steps.EnumerateArray())
            {
                var stepType = step.ValueKind == JsonValueKind.Object
                    && step.TryGetProperty(LoomRecipeContract.StepTypeProperty, out var typeElement)
                    && typeElement.ValueKind == JsonValueKind.String
                        ? typeElement.GetString()
                        : null;
                if (!LoomRecipeContract.WorkflowDefinitionUpsertStep.Equals(stepType, StringComparison.OrdinalIgnoreCase))
                {
                    error = "Loom recipe contains a step that is not supported by the workflow runtime.";
                    return false;
                }

                if (!step.TryGetProperty(LoomRecipeContract.StepPayloadProperty, out var payload) || payload.ValueKind != JsonValueKind.Object)
                {
                    error = "Loom recipe step does not include a workflow definition payload.";
                    return false;
                }

                if (!TryReadWorkflowDefinitionId(payload, out var workflowDefinitionId))
                {
                    error = "Workflow definition payload does not include a supported workflow definition identifier.";
                    return false;
                }

                resolved.Add(new WorkflowDefinitionToApply(workflowDefinitionId, payload.GetRawText()));
            }

            if (resolved.Count == 0)
            {
                error = "Loom recipe does not include any workflow definition steps.";
                return false;
            }

            definitions = resolved;
            return true;
        }
        catch (JsonException)
        {
            error = "Loom recipe payload is not valid JSON.";
            return false;
        }
    }

    private static bool TryReadRecipeSchemaVersion(JsonElement root, out string error)
    {
        error = "";
        var schemaVersion = root.TryGetProperty(LoomRecipeContract.SchemaVersionProperty, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
        if (!LoomRecipeContract.SchemaVersion.Equals(schemaVersion, StringComparison.OrdinalIgnoreCase))
        {
            error = "Loom recipe schema version is not supported by this runtime.";
            return false;
        }

        return true;
    }

    private static bool TryReadWorkflowDefinition(
        string workflowDefinitionJson,
        out WorkflowDefinitionToApply definition,
        out string error)
    {
        definition = default;
        error = "";
        try
        {
            using var document = JsonDocument.Parse(workflowDefinitionJson);
            if (!TryReadWorkflowDefinitionId(document.RootElement, out var workflowDefinitionId))
            {
                error = "Workflow definition payload does not include a supported workflow definition identifier.";
                return false;
            }

            definition = new WorkflowDefinitionToApply(workflowDefinitionId, workflowDefinitionJson);
            return true;
        }
        catch (JsonException)
        {
            error = "Workflow definition payload is not valid JSON.";
            return false;
        }
    }

    private static bool TryReadWorkflowDefinitionId(JsonElement workflowDefinition, out string workflowDefinitionId)
    {
        workflowDefinitionId = "";
        if (workflowDefinition.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var propertyName in new[] { "definitionId", "workflowDefinitionId", "id" })
        {
            if (!workflowDefinition.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
                continue;

            var value = property.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                workflowDefinitionId = value.Trim();
                return true;
            }
        }

        return false;
    }

    private static WorkflowArtifactApplyResult Rejected(ArtifactDigest observedDigest, string message) =>
        new(
            WorkflowArtifactApplyStatus.Rejected,
            observedDigest,
            null,
            [WorkflowArtifactRuntimeContractValidator.SafeDiagnostic("workflow-artifact.local-validation-failed", WorkflowArtifactDiagnosticSeverity.Error, message)]);

    private static IReadOnlyList<WorkflowArtifactDiagnostic> Safe(IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics) =>
        diagnostics
            .Select(x => WorkflowArtifactRuntimeContractValidator.SafeDiagnostic(x.Code, x.Severity, x.Message))
            .ToList();

    private readonly record struct WorkflowDefinitionToApply(string Id, string Json);
}
