using System.Net;
using System.Security.Cryptography;
using System.Text;
using ValenceControl.Api.Healing;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Agent;

namespace ValenceControl.Api.Workspace.Healing;

public sealed class HealingRepairWorkflowEndpointModule : IHealingEndpointModule
{
    private const int MaximumCapabilityLength = 4_096;
    private const int MaximumWebhookBodyBytes = 1_048_576;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var workload = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/healing/workload");
        workload.MapPost("/exchange", ExchangeAsync);
        workload.MapGet("/attempts/{attemptId:guid}/evidence", GetEvidenceAsync);
        workload.MapPost("/attempts/{attemptId:guid}/proposal", CreateProposalAsync);
        workload.MapPost("/attempts/{attemptId:guid}/proposals/{proposalId:guid}/finalize-exchange", ExchangeFinalizationAsync);
        workload.MapPost("/attempts/{attemptId:guid}/heartbeat", HeartbeatAsync);
        workload.MapPost("/attempts/{attemptId:guid}/result", UploadResultAsync);

        endpoints.MapPost("/api/integrations/github/webhooks", ProcessWebhookAsync);
    }

    private static async Task<IResult> CreateProposalAsync(
        Guid workspaceId,
        Guid attemptId,
        WorkloadProposalCreateRequest request,
        HttpContext context,
        IHealingWorkloadRequestAuthorizer authorizer,
        IHealingWorkloadApi workloadApi,
        CancellationToken cancellationToken)
    {
        if (!IsValidProposalRequest(attemptId, request))
            return Problem(context, HttpStatusCode.BadRequest, "healing.workload.proposal.invalid");

        var denied = await AuthorizeAsync(
            workspaceId,
            attemptId,
            WorkloadCapabilityScopes.CreateProposal,
            context,
            authorizer,
            cancellationToken);
        if (denied is not null)
            return denied;

        try
        {
            var response = await workloadApi.CreateProposalAsync(request, cancellationToken);
            return IsValidProposalResponse(response, attemptId)
                ? response.IsReplay ? Results.Ok(response) : Results.Accepted(value: response)
                : Problem(context, HttpStatusCode.BadGateway, "healing.workload.proposal-response.invalid");
        }
        catch (HealingWorkflowRequestException exception)
        {
            return Problem(context, exception.StatusCode, exception.ReasonCode);
        }
    }

    private static async Task<IResult> ExchangeFinalizationAsync(
        Guid workspaceId,
        Guid attemptId,
        Guid proposalId,
        WorkloadProposalFinalizationExchangeRequest request,
        HttpContext context,
        IHealingWorkloadRequestAuthorizer authorizer,
        IHealingWorkloadApi workloadApi,
        CancellationToken cancellationToken)
    {
        if (request.ProtocolVersion != HealingContractVersions.WorkloadProtocol ||
            request.AttemptId != attemptId || request.ProposalId != proposalId || proposalId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.OneTimeNonce) || string.IsNullOrWhiteSpace(request.IdentityAssertion) ||
            request.OneTimeNonce.Length > 1_024 || request.IdentityAssertion.Length > 32_768)
            return Problem(context, HttpStatusCode.BadRequest, "healing.workload.finalization-exchange.invalid");

        var authorization = await authorizer.AuthorizeExchangeAsync(workspaceId, attemptId, cancellationToken);
        if (!authorization.Authorized)
            return Problem(context, authorization.StatusCode, authorization.ReasonCode);

        try
        {
            var grant = await workloadApi.ExchangeFinalizationAsync(request, cancellationToken);
            if (grant.ProtocolVersion != HealingContractVersions.WorkloadProtocol ||
                grant.AttemptId != attemptId || string.IsNullOrWhiteSpace(grant.CapabilityToken) ||
                grant.CapabilityToken.Length > MaximumCapabilityLength || grant.ExpiresAt <= DateTimeOffset.UtcNow ||
                !grant.AllowedScopes.SetEquals(new HashSet<string>(
                    [WorkloadCapabilityScopes.FinalizeProposal, WorkloadCapabilityScopes.UploadResult],
                    StringComparer.Ordinal)))
                return Problem(context, HttpStatusCode.BadGateway, "healing.workload.finalization-grant.invalid");
            return Results.Ok(new HealingWorkloadCapabilityResponse(
                grant.ProtocolVersion,
                grant.AttemptId,
                grant.CapabilityToken,
                grant.AllowedScopes.Order(StringComparer.Ordinal).ToArray(),
                grant.ExpiresAt));
        }
        catch (HealingWorkflowRequestException exception)
        {
            return Problem(context, exception.StatusCode, exception.ReasonCode);
        }
    }

    private static async Task<IResult> ExchangeAsync(
        Guid workspaceId,
        WorkloadIdentityExchangeRequest request,
        HttpContext context,
        IHealingWorkloadRequestAuthorizer authorizer,
        IHealingWorkloadApi workloadApi,
        CancellationToken cancellationToken)
    {
        if (request.ProtocolVersion != HealingContractVersions.WorkloadProtocol ||
            request.AttemptId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.OneTimeNonce) ||
            string.IsNullOrWhiteSpace(request.IdentityAssertion) ||
            request.OneTimeNonce.Length > 1_024 ||
            request.IdentityAssertion.Length > 32_768)
            return Problem(context, HttpStatusCode.BadRequest, "healing.workload.exchange.invalid");

        var authorization = await authorizer.AuthorizeExchangeAsync(workspaceId, request.AttemptId, cancellationToken);
        if (!authorization.Authorized)
            return Problem(context, authorization.StatusCode, authorization.ReasonCode);

        try
        {
            var grant = await workloadApi.ExchangeAsync(request, cancellationToken);
            if (grant.ProtocolVersion != HealingContractVersions.WorkloadProtocol ||
                grant.AttemptId != request.AttemptId ||
                string.IsNullOrWhiteSpace(grant.CapabilityToken) ||
                grant.CapabilityToken.Length > MaximumCapabilityLength ||
                grant.ExpiresAt <= DateTimeOffset.UtcNow ||
                grant.AllowedScopes.Count == 0 ||
                !grant.AllowedScopes.IsSubsetOf(WorkloadCapabilityScopes.All))
                return Problem(context, HttpStatusCode.BadGateway, "healing.workload.grant.invalid");
            return Results.Ok(new HealingWorkloadCapabilityResponse(
                grant.ProtocolVersion,
                grant.AttemptId,
                grant.CapabilityToken,
                grant.AllowedScopes.Order(StringComparer.Ordinal).ToArray(),
                grant.ExpiresAt));
        }
        catch (HealingWorkflowRequestException exception)
        {
            return Problem(context, exception.StatusCode, exception.ReasonCode);
        }
    }

    private static async Task<IResult> GetEvidenceAsync(
        Guid workspaceId,
        Guid attemptId,
        HttpContext context,
        IHealingWorkloadRequestAuthorizer authorizer,
        IHealingWorkloadApi workloadApi,
        CancellationToken cancellationToken)
    {
        var denied = await AuthorizeAsync(
            workspaceId,
            attemptId,
            WorkloadCapabilityScopes.ReadEvidence,
            context,
            authorizer,
            cancellationToken);
        if (denied is not null)
            return denied;

        try
        {
            var response = await workloadApi.GetEvidenceAsync(
                new(HealingContractVersions.WorkloadProtocol, attemptId),
                cancellationToken);
            return IsValidEvidenceResponse(response, attemptId)
                ? Results.Ok(response)
                : Problem(context, HttpStatusCode.BadGateway, "healing.workload.evidence.invalid");
        }
        catch (HealingWorkflowRequestException exception)
        {
            return Problem(context, exception.StatusCode, exception.ReasonCode);
        }
    }

    private static async Task<IResult> HeartbeatAsync(
        Guid workspaceId,
        Guid attemptId,
        WorkloadHeartbeatRequest request,
        HttpContext context,
        IHealingWorkloadRequestAuthorizer authorizer,
        IHealingWorkloadApi workloadApi,
        CancellationToken cancellationToken)
    {
        if (request.ProtocolVersion != HealingContractVersions.WorkloadProtocol ||
            request.AttemptId != attemptId ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 256)
            return Problem(context, HttpStatusCode.BadRequest, "healing.workload.heartbeat.invalid");

        var denied = await AuthorizeAsync(
            workspaceId,
            attemptId,
            WorkloadCapabilityScopes.HeartbeatAttempt,
            context,
            authorizer,
            cancellationToken);
        if (denied is not null)
            return denied;

        try
        {
            var receipt = await workloadApi.HeartbeatAsync(request, cancellationToken);
            return receipt.ProtocolVersion == HealingContractVersions.WorkloadProtocol && receipt.AttemptId == attemptId
                ? Results.Ok(receipt)
                : Problem(context, HttpStatusCode.BadGateway, "healing.workload.heartbeat-receipt.invalid");
        }
        catch (HealingWorkflowRequestException exception)
        {
            return Problem(context, exception.StatusCode, exception.ReasonCode);
        }
    }

    private static async Task<IResult> UploadResultAsync(
        Guid workspaceId,
        Guid attemptId,
        WorkloadResultUploadRequest request,
        HttpContext context,
        IHealingWorkloadRequestAuthorizer authorizer,
        IHealingWorkloadApi workloadApi,
        CancellationToken cancellationToken)
    {
        if (!IsValidResultUpload(attemptId, request))
            return Problem(context, HttpStatusCode.BadRequest, "healing.workload.result.invalid");

        var denied = await AuthorizeAsync(
            workspaceId,
            attemptId,
            WorkloadCapabilityScopes.UploadResult,
            context,
            authorizer,
            cancellationToken);
        if (denied is not null)
            return denied;

        try
        {
            var receipt = await workloadApi.UploadResultAsync(request, cancellationToken);
            if (receipt.ProtocolVersion != HealingContractVersions.WorkloadProtocol || receipt.AttemptId != attemptId)
                return Problem(context, HttpStatusCode.BadGateway, "healing.workload.result-receipt.invalid");
            return receipt.IsReplay ? Results.Ok(receipt) : Results.Accepted(value: receipt);
        }
        catch (HealingWorkflowRequestException exception)
        {
            return Problem(context, exception.StatusCode, exception.ReasonCode);
        }
    }

    private static async Task<IResult> ProcessWebhookAsync(
        HttpContext context,
        IHealingVerifiedWebhookHandler handler,
        CancellationToken cancellationToken)
    {
        var signature = Header(context, "X-Hub-Signature-256");
        var deliveryId = Header(context, "X-GitHub-Delivery");
        var eventName = Header(context, "X-GitHub-Event");
        if (string.IsNullOrWhiteSpace(signature) ||
            string.IsNullOrWhiteSpace(deliveryId) ||
            string.IsNullOrWhiteSpace(eventName) ||
            signature.Length > 256 ||
            deliveryId.Length > 256 ||
            eventName.Length > 128)
            return Problem(context, HttpStatusCode.BadRequest, "healing.webhook.headers.invalid");
        if (context.Request.ContentLength > MaximumWebhookBodyBytes)
            return Problem(context, HttpStatusCode.RequestEntityTooLarge, "healing.webhook.body.too-large");

        byte[] body;
        try
        {
            body = await ReadBoundedBodyAsync(context.Request.Body, cancellationToken);
        }
        catch (HealingWorkflowRequestException exception)
        {
            return Problem(context, exception.StatusCode, exception.ReasonCode);
        }
        if (body.Length == 0)
            return Problem(context, HttpStatusCode.BadRequest, "healing.webhook.body.empty");

        try
        {
            var receipt = await handler.ProcessAsync(
                new(deliveryId, eventName, signature, body),
                cancellationToken);
            if (!string.Equals(receipt.DeliveryId, deliveryId, StringComparison.Ordinal))
                return Problem(context, HttpStatusCode.BadGateway, "healing.webhook.receipt.invalid");
            return receipt.IsReplay ? Results.Ok(receipt) : Results.Accepted(value: receipt);
        }
        catch (HealingWorkflowRequestException exception)
        {
            return Problem(context, exception.StatusCode, exception.ReasonCode);
        }
    }

    private static async ValueTask<IResult?> AuthorizeAsync(
        Guid workspaceId,
        Guid attemptId,
        string requiredScope,
        HttpContext context,
        IHealingWorkloadRequestAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            authorization.Length <= prefix.Length ||
            authorization.Length - prefix.Length > MaximumCapabilityLength)
            return Problem(context, HttpStatusCode.Unauthorized, "healing.workload.capability.missing");
        var token = authorization[prefix.Length..].Trim();
        if (token.Length == 0)
            return Problem(context, HttpStatusCode.Unauthorized, "healing.workload.capability.missing");

        var result = await authorizer.AuthorizeAsync(
            new(workspaceId, attemptId, token, requiredScope),
            cancellationToken);
        return result.Authorized ? null : Problem(context, result.StatusCode, result.ReasonCode);
    }

    private static bool IsValidResultUpload(Guid attemptId, WorkloadResultUploadRequest request)
    {
        var result = request.Result;
        return result is not null &&
               result.Reproduction is not null &&
               result.UnifiedDiff is not null &&
               request.ProtocolVersion == HealingContractVersions.WorkloadProtocol &&
               request.AttemptId == attemptId &&
               !string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
               request.IdempotencyKey.Length <= 256 &&
               result.ProtocolVersion == HealingContractVersions.AgentProtocol &&
               result.AttemptId == attemptId &&
               result.ProposalId is not null &&
               result.ProposalDigest is { Length: 71 } &&
               result.Confidence is >= 0 and <= 1 &&
               ResultClassifications.Contains(result.Classification) &&
               ReproductionClassifications.Contains(result.Reproduction.Classification) &&
               result.Reproduction.WasAttempted ==
               (result.Reproduction.Classification != "not-attempted") &&
               result.Reproduction.WasReproduced ==
               (result.Reproduction.Classification == "reproduced") &&
               (result.Classification == "reproduced") == result.Reproduction.WasReproduced &&
               !string.IsNullOrWhiteSpace(result.Reproduction.Summary) &&
               result.Reproduction.Summary.Length <= 8_192 &&
               Encoding.UTF8.GetByteCount(result.UnifiedDiff) <= 1_048_576;
    }

    private static bool IsValidProposalRequest(Guid attemptId, WorkloadProposalCreateRequest request)
    {
        var source = request.SourceContext;
        return request.ProtocolVersion == HealingContractVersions.WorkloadProtocol &&
               request.AttemptId == attemptId &&
               !string.IsNullOrWhiteSpace(request.IdempotencyKey) && request.IdempotencyKey.Length <= 256 &&
               source is not null && source.TargetRevision is { Length: > 0 and <= 256 } &&
               source.Digest is { Length: 71 } &&
               source.Files is { Count: > 0 and <= RepairProposalLimits.MaximumSourceFiles } &&
               source.OmittedPaths is { Count: <= RepairProposalLimits.MaximumOmittedPaths } &&
               source.Files.All(x => x is not null && x.Path is { Length: > 0 and <= RepairProposalLimits.MaximumPathCharacters } &&
                                     x.Content is not null && Encoding.UTF8.GetByteCount(x.Content) <= RepairProposalLimits.MaximumSourceFileBytes &&
                                     x.Digest is { Length: 71 }) &&
               source.Files.Sum(x => (long)Encoding.UTF8.GetByteCount(x.Content)) <= RepairProposalLimits.MaximumSourceBytes;
    }

    private static bool IsValidProposalResponse(WorkloadProposalCreateResponse response, Guid attemptId)
    {
        var proposal = response.Proposal;
        return response.ProtocolVersion == HealingContractVersions.WorkloadProtocol &&
               response.AttemptId == attemptId && proposal is not null && proposal.AttemptId == attemptId &&
               proposal.ProtocolVersion == HealingContractVersions.WorkloadProtocol && proposal.ProposalId != Guid.Empty &&
               proposal.ProposalDigest is { Length: 71 } && proposal.SourceContextDigest is { Length: 71 } &&
               proposal.PatchDigest is { Length: 71 } && proposal.ExpiresAt > DateTimeOffset.UtcNow &&
               !string.IsNullOrWhiteSpace(response.FinalizationNonce) && response.FinalizationNonce.Length <= 1_024;
    }

    private static bool IsValidEvidenceResponse(WorkloadEvidenceResponse response, Guid attemptId)
    {
        var evidence = response.Evidence;
        var budget = response.Budget;
        if (evidence is null || budget is null ||
            response.ProtocolVersion != HealingContractVersions.WorkloadProtocol ||
            response.AttemptId != attemptId ||
            evidence.AttemptId != attemptId ||
            evidence.ProtocolVersion != HealingContractVersions.AgentProtocol ||
            evidence.ExpiresAt <= DateTimeOffset.UtcNow ||
            evidence.Tier is not ("default-redacted" or "elevated") ||
            evidence.CanonicalJson is null ||
            evidence.OmittedFields is null ||
            Encoding.UTF8.GetByteCount(evidence.CanonicalJson) > RepairAgentGatewayLimits.MaximumEvidenceBytes ||
            evidence.OmittedFields.Count > RepairAgentGatewayLimits.MaximumCollectionItems ||
            budget.TimeLimit <= TimeSpan.Zero ||
            budget.TimeLimit > RepairAgentGatewayLimits.MaximumTimeLimit ||
            budget.InferenceUnitLimit <= 0 ||
            budget.RepositoryRunLimit <= 0)
            return false;

        var digest = RepairAgentGateway.ComputeSha256Digest(evidence.CanonicalJson);
        return evidence.Digest?.Length == digest.Length &&
               CryptographicOperations.FixedTimeEquals(
                   Encoding.ASCII.GetBytes(evidence.Digest),
                   Encoding.ASCII.GetBytes(digest));
    }

    private static readonly IReadOnlySet<string> ResultClassifications = new HashSet<string>(StringComparer.Ordinal)
    {
        "reproduced",
        "inferred-high-confidence",
        "insufficient-confidence",
        "revision-unverified"
    };

    private static readonly IReadOnlySet<string> ReproductionClassifications = new HashSet<string>(StringComparer.Ordinal)
    {
        "reproduced",
        "not-reproduced",
        "not-attempted",
        "failed"
    };

    private static string Header(HttpContext context, string name) => context.Request.Headers[name].ToString();

    private static async Task<byte[]> ReadBoundedBodyAsync(Stream source, CancellationToken cancellationToken)
    {
        await using var destination = new MemoryStream();
        var buffer = new byte[16_384];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                return destination.ToArray();
            if (destination.Length + read > MaximumWebhookBodyBytes)
                throw new HealingWorkflowRequestException(
                    HttpStatusCode.RequestEntityTooLarge,
                    "healing.webhook.body.too-large");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static IResult Problem(HttpContext context, HttpStatusCode statusCode, string reasonCode) =>
        Results.Problem(
            statusCode: (int)statusCode,
            title: "Healing workflow request rejected.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = reasonCode,
                ["traceId"] = context.TraceIdentifier
            });
}

public sealed record HealingWorkloadAuthorizationRequest(
    Guid WorkspaceId,
    Guid AttemptId,
    string CapabilityToken,
    string RequiredScope);

public sealed record HealingWorkloadCapabilityResponse(
    string ProtocolVersion,
    Guid AttemptId,
    string CapabilityToken,
    IReadOnlyList<string> AllowedScopes,
    DateTimeOffset ExpiresAt);

public sealed record HealingWorkloadAuthorizationResult(
    bool Authorized,
    HttpStatusCode StatusCode,
    string ReasonCode)
{
    public static HealingWorkloadAuthorizationResult Allow() =>
        new(true, HttpStatusCode.OK, "allowed");

    public static HealingWorkloadAuthorizationResult Deny(
        string reasonCode,
        HttpStatusCode statusCode = HttpStatusCode.Forbidden) =>
        new(false, statusCode, reasonCode);
}

public interface IHealingWorkloadRequestAuthorizer
{
    ValueTask<HealingWorkloadAuthorizationResult> AuthorizeExchangeAsync(
        Guid workspaceId,
        Guid attemptId,
        CancellationToken cancellationToken = default);

    ValueTask<HealingWorkloadAuthorizationResult> AuthorizeAsync(
        HealingWorkloadAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record HealingVerifiedWebhookRequest(
    string DeliveryId,
    string Event,
    string Signature,
    byte[] RawBody);

public sealed record HealingVerifiedWebhookReceipt(
    string DeliveryId,
    bool IsReplay,
    string OutcomeCode);

public interface IHealingVerifiedWebhookHandler
{
    ValueTask<HealingVerifiedWebhookReceipt> ProcessAsync(
        HealingVerifiedWebhookRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HealingWorkflowRequestException(
    HttpStatusCode statusCode,
    string reasonCode) : Exception("The healing workflow request was rejected.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ReasonCode { get; } = reasonCode;
}
