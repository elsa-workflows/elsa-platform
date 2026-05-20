namespace Elsa.Platform.Deployment.Abstractions;

internal static class DeploymentGuard
{
    public static string Require(string value, string parameterName)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? throw new ArgumentException("Value cannot be empty.", parameterName)
            : normalized;
    }
}
