using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Platform.Api.Public.Packages;

public sealed record PublicPackageResponse(
    string PackageId,
    string DisplayName,
    PublicPackageSourceResponse Source,
    string? LatestVersion,
    IReadOnlyList<PublicPackageVersionResponse> Versions);

public sealed record PublicPackageVersionResponse(
    string PackageId,
    string Version,
    PublicPackageSourceResponse Source,
    string? SchemaVersion,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<PublicPackageFeatureResponse> Features);

public sealed record PublicPackageSourceResponse(
    Guid Id,
    string Name,
    string Url);

public sealed record PublicPackageFeatureResponse(
    string FeatureId,
    string TypeName,
    string DisplayName,
    string? Description,
    string? Category,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> RuntimeKinds,
    IReadOnlyList<PublicPackageDependencyResponse> Dependencies,
    IReadOnlyList<PublicPackageConflictResponse> Conflicts,
    IReadOnlyList<PublicPackageInfrastructureRequirementResponse> Infrastructure,
    bool Advanced,
    bool Experimental,
    IReadOnlyDictionary<string, JsonElement> Extensions,
    IReadOnlyList<PublicPackageFeatureSettingResponse> Settings);

public sealed record PublicPackageFeatureSettingResponse(
    string Name,
    string? ClrType,
    string JsonType,
    bool Required,
    JsonElement? DefaultValue,
    string DisplayName,
    string? Description,
    string? Category,
    IReadOnlyDictionary<string, JsonElement> Validation,
    bool Secret,
    bool RestartRequired,
    string? EnvironmentVariable,
    [property: JsonPropertyName("ui")] IReadOnlyDictionary<string, JsonElement> UI,
    IReadOnlyDictionary<string, JsonElement> Extensions);

public sealed record PublicPackageDependencyResponse(
    string? PackageId,
    string? VersionRange,
    string? FeatureId,
    bool Optional,
    string? Reason);

public sealed record PublicPackageConflictResponse(
    string? PackageId,
    string? VersionRange,
    string? FeatureId,
    string? Reason);

public sealed record PublicPackageInfrastructureRequirementResponse(
    string Id,
    string Kind,
    bool Optional,
    string? Reason,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Providers,
    IReadOnlyList<string> ConfigurationKeys,
    IReadOnlyDictionary<string, JsonElement> Extensions);
