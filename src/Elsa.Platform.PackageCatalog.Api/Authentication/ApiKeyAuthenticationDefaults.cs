namespace Elsa.Platform.PackageCatalog.Api.Authentication;

public static class ApiKeyAuthenticationDefaults
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string ConfigurationKey = "Authentication:ApiKey";
}
