using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Platform.PackageManifests.Compatibility;

namespace Elsa.Platform.PackageManifests.Validation;

public sealed class ManifestValidator
{
    public const int MaxManifestBytes = 1_048_576;

    private static readonly Regex SimpleVersionRangePattern = new(@"^[A-Za-z0-9\s\.\-\+\*\[\]\(\),<>=]+$", RegexOptions.Compiled);

    public ManifestValidationResult Validate(string manifestJson, string? expectedPackageId = null, string? expectedVersion = null)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(manifestJson) > MaxManifestBytes)
            return Error("$", "manifest.size", "Manifest exceeds the 1 MB size limit.");

        ElsaPackageManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ElsaPackageManifest>(manifestJson, ManifestJsonSerializerOptions.Default);
        }
        catch (JsonException ex)
        {
            return Error("$", "manifest.json", ex.Message);
        }

        if (manifest is null)
            return Error("$", "manifest.empty", "Manifest JSON did not produce a manifest object.");

        var errors = new List<ManifestValidationFinding>();
        var warnings = new List<ManifestValidationFinding>();

        Required(manifest.SchemaVersion, "$.schemaVersion", "schemaVersion.required", "Schema version is required.", errors);
        if (!string.Equals(manifest.SchemaVersion, ManifestSchemaVersions.Current, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ManifestValidationFinding("$.schemaVersion", "schemaVersion.unsupported", $"Schema version '{manifest.SchemaVersion}' is not supported.", ManifestValidationSeverity.Error));
        }

        if (manifest.Package is null)
        {
            errors.Add(new ManifestValidationFinding("$.package", "package.required", "Package identity is required.", ManifestValidationSeverity.Error));
        }
        else
        {
            Required(manifest.Package.Id, "$.package.id", "package.id.required", "Package ID is required.", errors);
            Required(manifest.Package.Version, "$.package.version", "package.version.required", "Package version is required.", errors);
        }

        Required(manifest.DisplayName, "$.displayName", "displayName.required", "Display name is required.", errors);

        if (expectedPackageId is not null && manifest.Package is not null && !string.Equals(expectedPackageId, manifest.Package.Id, StringComparison.OrdinalIgnoreCase))
            errors.Add(new ManifestValidationFinding("$.package.id", "package.id.mismatch", "Manifest package ID does not match the NuGet package ID.", ManifestValidationSeverity.Error));

        if (expectedVersion is not null && manifest.Package is not null && !string.Equals(expectedVersion, manifest.Package.Version, StringComparison.OrdinalIgnoreCase))
            errors.Add(new ManifestValidationFinding("$.package.version", "package.version.mismatch", "Manifest package version does not match the NuGet package version.", ManifestValidationSeverity.Error));

        ValidateRange(manifest.Compatibility?.ElsaVersionRange, "$.compatibility.elsaVersionRange", errors);
        ValidateRange(manifest.Compatibility?.DockerImageVersionRange, "$.compatibility.dockerImageVersionRange", errors);
        ValidateRuntimeKinds(manifest.Compatibility?.RuntimeKinds, "$.compatibility.runtimeKinds", errors);

        if (manifest.Features is null)
        {
            errors.Add(new ManifestValidationFinding("$.features", "features.invalid", "Features must be an array.", ManifestValidationSeverity.Error));
        }

        var features = manifest.Features ?? [];
        for (var i = 0; i < features.Count; i++)
        {
            var feature = features[i];
            Required(feature.Id, $"$.features[{i}].id", "feature.id.required", "Feature ID is required.", errors);
            Required(feature.TypeName, $"$.features[{i}].typeName", "feature.typeName.required", "Feature CLR type name is required.", errors);
            Required(feature.DisplayName, $"$.features[{i}].displayName", "feature.displayName.required", "Feature display name is required.", errors);
            ValidateCategories(feature.Categories, $"$.features[{i}].categories", errors);
            ValidateRuntimeKinds(feature.Compatibility?.RuntimeKinds, $"$.features[{i}].compatibility.runtimeKinds", errors);

            if (feature.Infrastructure is null)
            {
                errors.Add(new ManifestValidationFinding($"$.features[{i}].infrastructure", "feature.infrastructure.invalid", "Feature infrastructure must be an array.", ManifestValidationSeverity.Error));
                continue;
            }

            for (var requirementIndex = 0; requirementIndex < feature.Infrastructure.Count; requirementIndex++)
            {
                var requirement = feature.Infrastructure[requirementIndex];
                Required(requirement.Id, $"$.features[{i}].infrastructure[{requirementIndex}].id", "infrastructure.id.required", "Infrastructure requirement ID is required.", errors);
                Required(requirement.Kind, $"$.features[{i}].infrastructure[{requirementIndex}].kind", "infrastructure.kind.required", "Infrastructure requirement kind is required.", errors);
            }
        }

        if (manifest.ExtensionData.Count > 0)
            warnings.Add(new ManifestValidationFinding("$", "manifest.unknownFields", "Manifest contains unknown fields that were preserved as extension data.", ManifestValidationSeverity.Warning));

        var status = errors.Any(e => e.RuleId == "schemaVersion.unsupported")
            ? ManifestValidationStatus.UnsupportedSchema
            : errors.Count == 0 ? ManifestValidationStatus.Valid : ManifestValidationStatus.Invalid;

        return new ManifestValidationResult(status, errors, warnings);
    }

    private static void Required(string? value, string path, string ruleId, string message, ICollection<ManifestValidationFinding> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add(new ManifestValidationFinding(path, ruleId, message, ManifestValidationSeverity.Error));
    }

    private static void ValidateRange(string? value, string path, ICollection<ManifestValidationFinding> errors)
    {
        if (!string.IsNullOrWhiteSpace(value) && !SimpleVersionRangePattern.IsMatch(value))
            errors.Add(new ManifestValidationFinding(path, "versionRange.invalid", "Version range contains unsupported characters.", ManifestValidationSeverity.Error));
    }

    private static void ValidateCategories(IReadOnlyList<string>? categories, string path, ICollection<ManifestValidationFinding> errors)
    {
        if (categories is null)
        {
            errors.Add(new ManifestValidationFinding(path, "feature.categories.invalid", "Feature categories must be an array.", ManifestValidationSeverity.Error));
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
        {
            var category = categories[categoryIndex];
            if (string.IsNullOrWhiteSpace(category))
            {
                errors.Add(new ManifestValidationFinding($"{path}[{categoryIndex}]", "feature.category.required", "Feature category cannot be empty.", ManifestValidationSeverity.Error));
                continue;
            }

            if (!seen.Add(category.Trim()))
                errors.Add(new ManifestValidationFinding($"{path}[{categoryIndex}]", "feature.category.duplicate", "Feature categories must be unique.", ManifestValidationSeverity.Error));
        }
    }

    private static void ValidateRuntimeKinds(IReadOnlyList<string>? runtimeKinds, string path, ICollection<ManifestValidationFinding> errors)
    {
        if (runtimeKinds is null)
            return;

        if (runtimeKinds.Count == 0)
        {
            errors.Add(new ManifestValidationFinding(path, "runtimeKinds.empty", "Runtime kinds must include at least one value when specified.", ManifestValidationSeverity.Error));
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < runtimeKinds.Count; i++)
        {
            var runtimeKind = runtimeKinds[i];
            var itemPath = $"{path}[{i}]";
            if (!RuntimeKindCompatibility.IsValid(runtimeKind))
            {
                errors.Add(new ManifestValidationFinding(itemPath, "runtimeKind.invalid", "Runtime kind must be a non-empty machine-readable identifier without whitespace.", ManifestValidationSeverity.Error));
                continue;
            }

            if (!seen.Add(runtimeKind))
                errors.Add(new ManifestValidationFinding(itemPath, "runtimeKind.duplicate", $"Runtime kind '{runtimeKind}' is duplicated.", ManifestValidationSeverity.Error));
        }
    }

    private static ManifestValidationResult Error(string path, string ruleId, string message) =>
        ManifestValidationResult.Invalid(new ManifestValidationFinding(path, ruleId, message, ManifestValidationSeverity.Error));
}
