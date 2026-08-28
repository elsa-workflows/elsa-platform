using System.Text;
using ElsaControl.Deployment.Abstractions.Artifacts;

namespace ElsaControl.Workflows.RuntimeApplier;

public sealed class WorkflowArtifactCommandProcessor(
    IWorkflowRuntimeCommandClient commands,
    IWorkflowArtifactEnvelopeProvider envelopes,
    IWorkflowArtifactPayloadFetcher payloads,
    IWorkflowArtifactSchemaValidator validator,
    IWorkflowDefinitionApplier applier,
    WorkflowArtifactRuntimeOptions options,
    TimeProvider? timeProvider = null)
{
    private readonly WorkflowArtifactRuntimeCapability _capability = WorkflowArtifactRuntimeCapability.FromOptions(options);
    private readonly WorkflowRuntimeCommandLeasePolicy _leasePolicy = new(options);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<WorkflowArtifactCommandProcessResult> ProcessAsync(
        WorkflowRuntimeCommandClaim claim,
        CancellationToken cancellationToken = default)
    {
        WorkflowRuntimeCommandLease lease;
        try
        {
            lease = _leasePolicy.Create(claim);
        }
        catch (InvalidOperationException ex)
        {
            return new WorkflowArtifactCommandProcessResult(
                WorkflowArtifactCommandProcessStatus.Skipped,
                null,
                null,
                [Diagnostic("workflow-artifact.lease-invalid", WorkflowArtifactDiagnosticSeverity.Error, ex.Message)]);
        }

        var command = claim.Command;
        if (command.Action is not WorkflowRuntimeCommandAction.Deploy and not WorkflowRuntimeCommandAction.Rollback)
        {
            return await RejectAsync(
                command.Id,
                lease.LeaseToken,
                null,
                null,
                [Diagnostic("workflow-artifact.unsupported-command", WorkflowArtifactDiagnosticSeverity.Error, "Runtime command action is not supported by the workflow artifact applier.")],
                cancellationToken);
        }

        if (command.Artifact is null)
        {
            return await RejectAsync(
                command.Id,
                lease.LeaseToken,
                null,
                null,
                [Diagnostic("workflow-artifact.missing-artifact", WorkflowArtifactDiagnosticSeverity.Error, "Runtime command does not include an artifact reference.")],
                cancellationToken);
        }

        try
        {
            await ReportProgressIfAllowedAsync(command.Id, lease, "validating", 10, "Validating workflow artifact.", cancellationToken);
            var envelope = await envelopes.GetEnvelopeAsync(command.Artifact, cancellationToken);
            var payload = await payloads.FetchAsync(envelope.PayloadReference, cancellationToken);
            var validation = await validator.ValidateAsync(envelope, payload, _capability, cancellationToken);
            if (!validation.Succeeded || validation.ObservedDigest is null)
            {
                return await RejectAsync(
                    command.Id,
                    lease.LeaseToken,
                    validation.ObservedDigest,
                    null,
                    validation.Diagnostics,
                    cancellationToken);
            }

            var observedDigest = validation.ObservedDigest.Value;
            var evaluation = _leasePolicy.Evaluate(lease, _timeProvider.GetUtcNow());
            if (!evaluation.CanContinueLocalApply)
            {
                return new WorkflowArtifactCommandProcessResult(
                    WorkflowArtifactCommandProcessStatus.Skipped,
                    observedDigest,
                    null,
                    [Diagnostic("workflow-artifact.lease-not-safe", WorkflowArtifactDiagnosticSeverity.Warning, "Runtime command lease is not safe for local apply.")]);
            }

            await ReportProgressIfAllowedAsync(command.Id, lease, "applying", 60, "Applying workflow artifact.", cancellationToken);
            WorkflowArtifactApplyResult applyResult;
            try
            {
                applyResult = await applier.ApplyAsync(
                    new WorkflowArtifactApplyRequest(
                        command.Id,
                        command.IdempotencyKey,
                        envelope,
                        Encoding.UTF8.GetString(payload.Content),
                        observedDigest),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await FailAsync(
                    command.Id,
                    lease.LeaseToken,
                    observedDigest,
                    null,
                    [Diagnostic("workflow-artifact.apply-failed", WorkflowArtifactDiagnosticSeverity.Error, ex.Message)],
                    cancellationToken);
            }

            return applyResult.Status switch
            {
                WorkflowArtifactApplyStatus.Applied =>
                    await CompleteAsync(command.Id, lease.LeaseToken, applyResult, WorkflowArtifactCommandProcessStatus.Completed, cancellationToken),
                WorkflowArtifactApplyStatus.AlreadyApplied =>
                    await CompleteAsync(command.Id, lease.LeaseToken, applyResult, WorkflowArtifactCommandProcessStatus.AlreadyApplied, cancellationToken),
                WorkflowArtifactApplyStatus.Rejected =>
                    await RejectAsync(command.Id, lease.LeaseToken, applyResult.ObservedDigest, applyResult.RuntimeReference, applyResult.Diagnostics, cancellationToken),
                WorkflowArtifactApplyStatus.Failed =>
                    await FailAsync(command.Id, lease.LeaseToken, applyResult.ObservedDigest, applyResult.RuntimeReference, applyResult.Diagnostics, cancellationToken),
                _ =>
                    await FailAsync(
                        command.Id,
                        lease.LeaseToken,
                        applyResult.ObservedDigest,
                        applyResult.RuntimeReference,
                        [Diagnostic("workflow-artifact.apply-status-unknown", WorkflowArtifactDiagnosticSeverity.Error, "Workflow artifact apply returned an unknown status.")],
                        cancellationToken)
            };
        }
        catch (InvalidOperationException ex)
        {
            return await RejectAsync(
                command.Id,
                lease.LeaseToken,
                null,
                null,
                [Diagnostic("workflow-artifact.rejected", WorkflowArtifactDiagnosticSeverity.Error, ex.Message)],
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await FailAsync(
                command.Id,
                lease.LeaseToken,
                null,
                null,
                [Diagnostic("workflow-artifact.apply-failed", WorkflowArtifactDiagnosticSeverity.Error, ex.Message)],
                cancellationToken);
        }
    }

    private async Task ReportProgressIfAllowedAsync(
        Guid commandId,
        WorkflowRuntimeCommandLease lease,
        string status,
        int percentComplete,
        string message,
        CancellationToken cancellationToken)
    {
        if (!_leasePolicy.Evaluate(lease, _timeProvider.GetUtcNow()).CanReportToControl)
            return;

        await commands.ReportProgressAsync(commandId, lease.LeaseToken, status, percentComplete, message, cancellationToken);
    }

    private async Task<WorkflowArtifactCommandProcessResult> CompleteAsync(
        Guid commandId,
        string leaseToken,
        WorkflowArtifactApplyResult result,
        WorkflowArtifactCommandProcessStatus status,
        CancellationToken cancellationToken)
    {
        var report = await commands.CompleteAsync(commandId, leaseToken, result.ObservedDigest, result.RuntimeReference, Safe(result.Diagnostics), cancellationToken);
        return report.Succeeded
            ? new WorkflowArtifactCommandProcessResult(status, result.ObservedDigest, result.RuntimeReference, Safe(result.Diagnostics))
            : ReportFailed(result.ObservedDigest, result.RuntimeReference, report.Message);
    }

    private async Task<WorkflowArtifactCommandProcessResult> RejectAsync(
        Guid commandId,
        string leaseToken,
        ArtifactDigest? observedDigest,
        string? runtimeReference,
        IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var safe = Safe(diagnostics);
        var report = await commands.RejectAsync(commandId, leaseToken, safe, cancellationToken);
        return report.Succeeded
            ? new WorkflowArtifactCommandProcessResult(WorkflowArtifactCommandProcessStatus.Rejected, observedDigest, runtimeReference, safe)
            : ReportFailed(observedDigest, runtimeReference, report.Message);
    }

    private async Task<WorkflowArtifactCommandProcessResult> FailAsync(
        Guid commandId,
        string leaseToken,
        ArtifactDigest? observedDigest,
        string? runtimeReference,
        IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var safe = Safe(diagnostics);
        var report = await commands.FailAsync(commandId, leaseToken, safe, cancellationToken);
        return report.Succeeded
            ? new WorkflowArtifactCommandProcessResult(WorkflowArtifactCommandProcessStatus.Failed, observedDigest, runtimeReference, safe)
            : ReportFailed(observedDigest, runtimeReference, report.Message);
    }

    private static WorkflowArtifactCommandProcessResult ReportFailed(
        ArtifactDigest? observedDigest,
        string? runtimeReference,
        string message) =>
        new(
            WorkflowArtifactCommandProcessStatus.ReportFailed,
            observedDigest,
            runtimeReference,
            [Diagnostic("workflow-artifact.report-failed", WorkflowArtifactDiagnosticSeverity.Error, message)]);

    private static IReadOnlyList<WorkflowArtifactDiagnostic> Safe(IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics) =>
        diagnostics.Select(x => Diagnostic(x.Code, x.Severity, x.Message)).ToList();

    private static WorkflowArtifactDiagnostic Diagnostic(string code, WorkflowArtifactDiagnosticSeverity severity, string message) =>
        WorkflowArtifactRuntimeContractValidator.SafeDiagnostic(code, severity, message);
}
