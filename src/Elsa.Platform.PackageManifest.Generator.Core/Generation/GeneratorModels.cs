using Elsa.Platform.PackageManifests.Validation;

namespace Elsa.Platform.PackageManifest.Generator.Core.Generation;

public sealed record GeneratorOptions(
    bool GenerateManifest,
    string OutputPath,
    bool IncludeInPackage,
    string PackagePath,
    string? OverrideFile,
    string ValidationSeverity,
    bool Strict,
    bool FailOnWarnings,
    bool AllowTargetFrameworkDifferences,
    string DiagnosticsVerbosity,
    IReadOnlyList<string> AdditionalFeatureInterfaceTypes);

public sealed record ProjectPackageMetadata(
    string PackageId,
    string Version,
    string? Title,
    string? Description,
    IReadOnlyList<string> Authors,
    string? RepositoryUrl,
    string? PackageProjectUrl,
    IReadOnlyList<string> PackageTags,
    string? PackageLicenseExpression,
    string? PackageReadmeFile,
    string? TargetFramework,
    IReadOnlyList<string> TargetFrameworks);

public sealed record AssemblyInspectionInput(
    string AssemblyPath,
    string? XmlDocumentationPath,
    string? TargetFramework,
    IReadOnlyList<string> ReferenceAssemblyPaths,
    bool NullableMetadataAvailable);

public enum FeatureDiscoverySource
{
    IShellFeature,
    InheritedIShellFeature,
    AdditionalInterface
}

public sealed record DiscoveredFeature(
    string FeatureId,
    string CShellsFeatureName,
    string ClrTypeName,
    string DisplayName,
    string? Description,
    IReadOnlyList<string> Categories,
    FeatureDiscoverySource DiscoverySource,
    bool IsPublic,
    bool IsAbstract,
    bool IsGenericDefinition,
    bool Advanced,
    bool Experimental,
    IReadOnlyList<ManifestDependencyReference> Dependencies,
    IReadOnlyList<ManifestConflictReference> Conflicts,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<ManifestInfrastructureRequirementReference> Infrastructure,
    IReadOnlyDictionary<string, object?> ExtensionMetadata,
    IReadOnlyList<DiscoveredSetting> Settings);

public sealed record DiscoveredSetting(
    string FeatureId,
    string Name,
    string ClrPropertyName,
    string ClrType,
    string JsonType,
    string ConfigurationPath,
    bool Required,
    bool Nullable,
    object? DefaultValue,
    string DisplayName,
    string? Description,
    string? Category,
    string? Group,
    IReadOnlyDictionary<string, object?> ValidationConstraints,
    IReadOnlyList<string> EnumValues,
    bool Secret,
    bool Sensitive,
    bool RestartRequired,
    string? UIHint,
    IReadOnlyList<ManifestUIOptionReference> UIOptions,
    ManifestUIOptionsProviderReference? UIOptionsProvider,
    bool Advanced,
    bool Experimental,
    IReadOnlyDictionary<string, object?> ExtensionMetadata);

public sealed record ManifestUIOptionReference(string Value, string? Label, string? Description);

public sealed record ManifestUIOptionsProviderReference(
    string Provider,
    IReadOnlyList<string> DependsOn,
    IReadOnlyDictionary<string, object?> Parameters);

public sealed record ManifestDependencyReference(string? PackageId, string? VersionRange, string? FeatureId);

public sealed record ManifestConflictReference(string? PackageId, string? VersionRange, string? FeatureId, string? Reason);

public sealed record ManifestInfrastructureRequirementReference(
    string Id,
    string Kind,
    bool Optional,
    string? Reason,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Providers,
    IReadOnlyList<string> ConfigurationKeys,
    IReadOnlyDictionary<string, object?> Extensions);

public sealed record XmlDocumentationEntry(string MemberName, string? Summary, string? Remarks, IReadOnlyList<string> Examples);

public sealed record GeneratedSettingsSchema(
    string Type,
    string? Format,
    bool Nullable,
    bool Required,
    IReadOnlyList<string> Enum,
    object? Items,
    object? AdditionalProperties,
    IReadOnlyDictionary<string, object?> Constraints,
    string? Description,
    object? Default,
    IReadOnlyDictionary<string, object?> Extensions);

public sealed record GeneratedManifestArtifact(
    string IntermediatePath,
    string PackagePath,
    string ManifestJson,
    long ManifestSize,
    ManifestValidationResult ValidationResult,
    bool IncludedInPackage);
