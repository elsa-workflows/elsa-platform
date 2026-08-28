using System.Xml;
using ElsaControl.PackageCatalog.Core.Packages;

namespace ElsaControl.PackageCatalog.Core.Sources;

public sealed class PackageSourceValidator
{
    public PackageSourceValidationResult Validate(PackageSource source)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(source.Name))
            errors.Add("Source name is required.");

        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            errors.Add("Source URL must be an absolute HTTP or HTTPS URL.");
        else if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            errors.Add("Source URL must not contain embedded credentials.");

        if (source.Type != PackageSourceType.NuGetFeed)
            errors.Add("Only NuGet feed sources are supported.");

        if (source.IncludePatterns.Count == 0 || source.IncludePatterns.All(string.IsNullOrWhiteSpace))
            errors.Add("At least one include pattern is required.");

        if (!string.IsNullOrWhiteSpace(source.PollingInterval))
        {
            try
            {
                _ = XmlConvert.ToTimeSpan(source.PollingInterval);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                errors.Add("Polling interval must be an ISO 8601 duration, for example PT30M.");
            }
        }

        return errors.Count == 0 ? PackageSourceValidationResult.Valid : PackageSourceValidationResult.Invalid(errors);
    }
}

public sealed record PackageSourceValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static PackageSourceValidationResult Valid { get; } = new(true, []);
    public static PackageSourceValidationResult Invalid(IReadOnlyList<string> errors) => new(false, errors);
}
