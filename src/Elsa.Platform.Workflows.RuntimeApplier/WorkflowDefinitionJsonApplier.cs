using System.Text.Json;
using Elsa.Platform.Deployment.Abstractions.Artifacts;

namespace Elsa.Platform.Workflows.RuntimeApplier;

public sealed class WorkflowDefinitionJsonApplier(IWorkflowDefinitionRuntimeStore store) : IWorkflowDefinitionApplier
{
    public async Task<WorkflowArtifactApplyResult> ApplyAsync(
        WorkflowArtifactApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryReadWorkflowDefinitionId(request.WorkflowDefinitionJson, out var workflowDefinitionId))
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
                    request.WorkflowDefinitionJson,
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
