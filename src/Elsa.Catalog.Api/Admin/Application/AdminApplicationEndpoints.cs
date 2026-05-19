using System.Reflection;
using Elsa.Catalog.Api.Authentication;

namespace Elsa.Catalog.Api.Admin.Application;

public static class AdminApplicationEndpoints
{
    private const string BuildNumberConfigurationKey = "Application:BuildNumber";

    public static IEndpointRouteBuilder MapAdminApplicationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/application", (IConfiguration configuration) => Results.Ok(GetApplicationInfo(configuration)))
            .RequireAuthorization(AdminAuthorization.Policy)
            .WithTags("Admin Application");

        return endpoints;
    }

    private static AdminApplicationResponse GetApplicationInfo(IConfiguration configuration)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var configuredBuildNumber = configuration[BuildNumberConfigurationKey];
        var assemblyBuildNumber = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return new AdminApplicationResponse(
            assembly.GetName().Name ?? "Elsa.Catalog.Api",
            FirstNonEmpty(configuredBuildNumber, assemblyBuildNumber, assembly.GetName().Version?.ToString(), "unknown"));
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "unknown";
}
