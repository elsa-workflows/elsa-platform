using System.Text.Json;
using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Artifacts;

namespace Elsa.Platform.Workflows.RuntimeApplier;

public sealed class WorkflowDefinitionJsonApplier(IWorkflowDefinitionRuntimeStore store) : IWorkflowDefinitionApplier
{
    private const string UpsertStepType = "workflowDefinition.upsert";

    public async Task<WorkflowArtifactApplyResult> ApplyAsync(
        WorkflowArtifactApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (ArtifactTypeIds.ElsaLoomRecipe.Equals(request.Envelope.ArtifactTypeId, StringComparison.OrdinalIgnoreCase))
            return await ApplyLoomRecipeAsync(request, cancellationToken);

        return await ApplyWorkflowDefinitionAsync(request, request.WorkflowDefinitionJson, cancellationToken);
    }

    private async Task<WorkflowArtifactApplyResult> ApplyLoomRecipeAsync(
        WorkflowArtifactApplyRequest request,
        CancellationToken cancellationToken)
    {
        var stepPayloads = ReadUpsertStepPayloads(request.WorkflowDefinitionJson);
        if (stepPayloads.Count == 0)
        {
            return Rejected(
                request.ObservedDigest,
                "workflow-artifact.local-validation-failed",
                "Loom recipe payload does not include any supported workflow definition steps.");
        }

        string? runtimeReference = null;
        var diagnostics = new List<WorkflowArtifactDiagnostic>();
        foreach (var stepPayload in stepPayloads)
        {
            var stepResult = await ApplyWorkflowDefinitionAsync(request, stepPayload, cancellationToken);
            if (stepResult.Status != WorkflowArtifactApplyStatus.Applied)
                return stepResult;

            runtimeReference = stepResult.RuntimeReference;
            diagnostics.AddRange(stepResult.Diagnostics);
        }

        return new WorkflowArtifactApplyResult(
            WorkflowArtifactApplyStatus.Applied,
            request.ObservedDigest,
            runtimeReference,
            diagnostics);
    }

    private async Task<WorkflowArtifactApplyResult> ApplyWorkflowDefinitionAsync(
        WorkflowArtifactApplyRequest request,
        string workflowDefinitionJson,
        CancellationToken cancellationToken)
    {
        if (!TryReadWorkflowDefinitionId(workflowDefinitionJson, out var workflowDefinitionId))
        {
            return Rejected(
                request.ObservedDigest,
                "workflow-artifact.local-validation-failed",
                "Workflow definition payload does not include a supported workflow definition identifier.");
        }

        try
        {
            var result = await store.SaveAsync(
                new WorkflowDefinitionRuntimeStoreRequest(
                    workflowDefinitionId,
                    workflowDefinitionJson,
                    request.Envelope,
                    request.ObservedDigest),
                cancellationToken);

            return new WorkflowArtifactApplyResult(
                WorkflowArtifactApplyStatus.Applied,
                request.ObservedDigest,
                result.RuntimeReference,
                Safe(result.Diagnostics));
        }
        catch (InvalidOperationException ex)
        {
            return Rejected(
                request.ObservedDigest,
                "workflow-artifact.local-validation-failed",
                ex.Message);
        }
    }

    private static IReadOnlyList<string> ReadUpsertStepPayloads(string recipeJson)
    {
        try
        {
            using var document = JsonDocument.Parse(recipeJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("steps", out var steps)
                || steps.ValueKind != JsonValueKind.Array)
                return [];

            return steps.EnumerateArray()
                .Where(step => step.ValueKind == JsonValueKind.Object
                    && step.TryGetProperty("type", out var type)
                    && type.ValueKind == JsonValueKind.String
                    && UpsertStepType.Equals(type.GetString(), StringComparison.OrdinalIgnoreCase)
                    && step.TryGetProperty("payload", out var payload)
                    && payload.ValueKind == JsonValueKind.Object)
                .Select(step => step.GetProperty("payload").GetRawText())
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool TryReadWorkflowDefinitionId(string workflowDefinitionJson, out string workflowDefinitionId)
    {
        workflowDefinitionId = "";
        try
        {
            using var document = JsonDocument.Parse(workflowDefinitionJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var propertyName in new[] { "definitionId", "workflowDefinitionId", "id" })
            {
                if (!document.RootElement.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
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
        catch (JsonException)
        {
            return false;
        }
    }

    private static WorkflowArtifactApplyResult Rejected(ArtifactDigest observedDigest, string code, string message) =>
        new(
            WorkflowArtifactApplyStatus.Rejected,
            observedDigest,
            null,
            [WorkflowArtifactRuntimeContractValidator.SafeDiagnostic(code, WorkflowArtifactDiagnosticSeverity.Error, message)]);

    private static IReadOnlyList<WorkflowArtifactDiagnostic> Safe(IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics) =>
        diagnostics
            .Select(x => WorkflowArtifactRuntimeContractValidator.SafeDiagnostic(x.Code, x.Severity, x.Message))
            .ToList();
}
