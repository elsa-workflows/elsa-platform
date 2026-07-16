using Microsoft.AspNetCore.Diagnostics;

namespace Elsa.Platform.Api.Healing;

/// <summary>
/// Keeps framework-level JSON/body binding failures on Healing routes inside the public ProblemDetails contract.
/// </summary>
public sealed class HealingBadRequestExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
            return false;

        var isHealingRequest = httpContext.Request.Path.StartsWithSegments("/api/workspaces") &&
                               httpContext.Request.Path.Value!.Contains("/healing/", StringComparison.Ordinal);
        var status = badRequest.StatusCode is >= 400 and < 500
            ? badRequest.StatusCode
            : StatusCodes.Status400BadRequest;
        await Results.Problem(
            title: isHealingRequest ? "Healing request failed." : "The request is invalid.",
            detail: status == StatusCodes.Status413PayloadTooLarge
                ? "The Healing request body is too large."
                : "The Healing request body is invalid.",
            statusCode: status,
            extensions: isHealingRequest
                ? new Dictionary<string, object?>
                {
                    ["code"] = status == StatusCodes.Status413PayloadTooLarge
                        ? "healing.request.too-large"
                        : "healing.request.invalid",
                    ["correlationId"] = httpContext.TraceIdentifier
                }
                : new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier })
            .ExecuteAsync(httpContext);
        return true;
    }
}
