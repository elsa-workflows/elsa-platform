using Elsa.Platform.Api.Authentication;
using Elsa.Platform.Api.Healing;
using Elsa.Platform.Healing.Core.Manifests;
using Elsa.Platform.Healing.Core.Ownership;

namespace Elsa.Platform.Api.Workspace.Healing;

/// <summary>
/// Trusted builder boundary. Workspace users cannot use this route to elevate an owner-verified
/// manifest into automation authority.
/// </summary>
public sealed class PlatformManagedManifestAttestationEndpointModule : IHealingEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/builder/healing/workspaces/{workspaceId:guid}/applications/{applicationId:guid}/component-manifests/{manifestId:guid}/attest",
                AttestAsync)
            .RequireAuthorization(BuilderClientAuthorization.Policy);
    }

    private static async Task<IResult> AttestAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid manifestId,
        PlatformManagedManifestAttestationRequest request,
        HttpContext context,
        ComponentManifestService service,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ManifestDigest) || request.ManifestDigest.Length != 71 ||
            !request.ManifestDigest.StartsWith("sha256:", StringComparison.Ordinal) ||
            !request.ManifestDigest.AsSpan(7).ContainsOnlyAsciiHexDigits() ||
            string.IsNullOrWhiteSpace(request.BuildId) || request.BuildId.Length > 256)
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Manifest attestation evidence is invalid.",
                extensions: new Dictionary<string, object?> { ["code"] = "healing.attestation-evidence.invalid" });

        var result = await service.VerifyAttestedAsync(
            workspaceId,
            applicationId,
            manifestId,
            new ComponentManifestAttestationEvidence(request.ManifestDigest, request.BuildId),
            cancellationToken);
        if (!result.Succeeded)
        {
            var status = result.ReasonCode == HealingOwnershipReasonCodes.NotFound
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;
            return Results.Problem(
                statusCode: status,
                title: "Manifest attestation was rejected.",
                detail: "The persisted manifest does not match a Platform-managed application revision and build identity.",
                extensions: new Dictionary<string, object?> { ["code"] = $"healing.{result.ReasonCode}" });
        }

        var manifest = result.Value!;
        return Results.Ok(new
        {
            manifest.Id,
            manifest.WorkspaceId,
            manifest.ApplicationId,
            manifest.RevisionId,
            manifest.ManifestDigest,
            manifest.TrustState,
            manifest.VerificationMethod,
            AutomationAuthoritative = ComponentManifestService.IsAutomationAuthoritative(manifest)
        });
    }
}

public sealed record PlatformManagedManifestAttestationRequest(string? ManifestDigest, string? BuildId);

file static class AttestationStringExtensions
{
    public static bool ContainsOnlyAsciiHexDigits(this ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
                return false;
        }
        return true;
    }
}
