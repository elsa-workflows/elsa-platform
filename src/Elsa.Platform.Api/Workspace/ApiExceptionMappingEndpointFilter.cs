namespace Elsa.Platform.Api.Workspace;

/// <summary>
/// Maps the service-layer exceptions thrown by workspace endpoints to their conventional
/// HTTP results. Handlers that need a non-standard mapping keep a local catch clause,
/// which runs before this filter.
/// </summary>
public static class ApiExceptionMappingEndpointFilter
{
    public static TBuilder MapCommonApiExceptions<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            try
            {
                return await next(context);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });
}
