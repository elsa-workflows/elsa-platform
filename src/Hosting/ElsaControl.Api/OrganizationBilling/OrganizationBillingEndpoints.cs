using ElsaControl.Api.Authentication;
using ElsaControl.Billing.Stripe;
using ElsaControl.PackageCatalog.Core.Accounts;

namespace ElsaControl.Api.OrganizationBilling;

public static class OrganizationBillingEndpoints
{
    private const int MaxWebhookBodyBytes = 512 * 1024;

    public static IEndpointRouteBuilder MapOrganizationBillingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/organizations/{organizationId:guid}/billing")
            .WithTags("Organization Billing");

        group.MapPost("/checkout", async (
            Guid organizationId,
            HttpContext context,
            IWorkspaceIdentityReader identityReader,
            OrganizationBillingApiService billing,
            CancellationToken cancellationToken) =>
        {
            var identity = await identityReader.ReadAsync(context);
            if (identity is null)
                return WorkspaceIdentityHttpContextExtensions.UnauthorizedWorkspaceIdentity();

            var result = await billing.CreateCheckoutAsync(identity, organizationId, cancellationToken);
            return ToHttpResult(result);
        });

        group.MapPost("/portal", async (
            Guid organizationId,
            HttpContext context,
            IWorkspaceIdentityReader identityReader,
            OrganizationBillingApiService billing,
            CancellationToken cancellationToken) =>
        {
            var identity = await identityReader.ReadAsync(context);
            if (identity is null)
                return WorkspaceIdentityHttpContextExtensions.UnauthorizedWorkspaceIdentity();

            var result = await billing.CreatePortalAsync(identity, organizationId, cancellationToken);
            return ToHttpResult(result);
        });

        endpoints.MapPost("/api/billing/webhooks/stripe", HandleStripeWebhookAsync)
            .WithTags("Billing Webhooks");

        return endpoints;
    }

    private static async Task<IResult> HandleStripeWebhookAsync(
        HttpContext context,
        IBillingProvider provider,
        OrganizationBillingService billing,
        CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength is > MaxWebhookBodyBytes)
            return Results.BadRequest(new { code = "webhook.invalid" });

        var rawBody = await ReadBodyAsync(context.Request.Body, cancellationToken);
        if (rawBody is null)
            return Results.BadRequest(new { code = "webhook.invalid" });

        var signature = context.Request.Headers["Stripe-Signature"].FirstOrDefault();
        BillingWebhookNormalizationResult normalized;
        try
        {
            normalized = provider.VerifyAndNormalizeWebhook(rawBody.Value, signature ?? "", DateTimeOffset.UtcNow);
        }
        catch (BillingProviderUnavailableException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception)
        {
            return Results.BadRequest(new { code = "webhook.invalid" });
        }
        if (normalized.Status == BillingWebhookNormalizationStatus.Invalid || normalized.Event is null)
            return Results.BadRequest(new { code = normalized.FailureCode ?? "webhook.invalid" });

        BillingEventConsumptionResult result;
        try
        {
            result = normalized.Status == BillingWebhookNormalizationStatus.Unknown
                ? await billing.RecordUnknownAsync(normalized.Event, cancellationToken)
                : await billing.ConsumeAsync(normalized.Event, cancellationToken);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { code = "webhook.invalid" });
        }
        return Results.Ok(new { status = ToWireStatus(result.Outcome) });
    }

    private static async Task<ReadOnlyMemory<byte>?> ReadBodyAsync(Stream body, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        var total = 0;
        while (true)
        {
            var read = await body.ReadAsync(chunk, cancellationToken);
            if (read == 0)
                break;
            total += read;
            if (total > MaxWebhookBodyBytes)
                return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    private static IResult ToHttpResult(OrganizationBillingApiResult result)
    {
        if (result.Succeeded)
            return Results.Ok(new OrganizationBillingSessionResponse(result.Session!.Url));
        if (result.Failure is OrganizationWorkspaceFailure.OrganizationNotAllowed)
            return Results.NotFound(new { code = "organization.not-found" });
        if (result.Failure is OrganizationWorkspaceFailure.OrganizationRoleNotAllowed)
            return Results.Forbid();
        if (result.CustomerNotReady)
            return Results.Conflict(new { code = "billing.customer-not-ready" });
        if (result.ProviderUnavailable)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        return Results.BadRequest(new { code = "billing.invalid" });
    }

    private static string ToWireStatus(BillingEventConsumptionOutcome outcome) => outcome switch
    {
        BillingEventConsumptionOutcome.Applied => "applied",
        BillingEventConsumptionOutcome.Replayed => "replayed",
        BillingEventConsumptionOutcome.IgnoredOutOfOrder => "ignored-out-of-order",
        BillingEventConsumptionOutcome.Rejected => "rejected",
        BillingEventConsumptionOutcome.RecordedUnknown => "recorded-unknown",
        _ => "unknown"
    };
}

public sealed record OrganizationBillingSessionResponse(string Url);
