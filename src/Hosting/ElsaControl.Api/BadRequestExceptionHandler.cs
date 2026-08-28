using Microsoft.AspNetCore.Diagnostics;

namespace ElsaControl.Api;

/// <summary>
/// Keeps framework-level JSON/body binding failures inside the public ProblemDetails contract.
/// </summary>
public sealed class BadRequestExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
            return false;

        var status = badRequest.StatusCode is >= 400 and < 500
            ? badRequest.StatusCode
            : StatusCodes.Status400BadRequest;
        await Results.Problem(
            title: "The request is invalid.",
            detail: status == StatusCodes.Status413PayloadTooLarge
                ? "The request body is too large."
                : "The request body is invalid.",
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier })
            .ExecuteAsync(httpContext);
        return true;
    }
}
