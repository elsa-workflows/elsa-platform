using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Catalog.Api.Public.Features;

public sealed record PublicFeatureResponse(
    string FeatureId,
    string PackageId,
    string PackageVersion,
    PublicFeatureSourceResponse Source,
    string TypeName,
    string DisplayName,
    string? Description,
    string? Category,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<PublicFeatureDependencyResponse> Dependencies,
    IReadOnlyList<PublicFeatureConflictResponse> Conflicts,
    IReadOnlyList<PublicFeatureInfrastructureRequirementResponse> Infrastructure,
    bool Advanced,
    bool Experimental,
    IReadOnlyDictionary<string, JsonElement> Extensions,
    IReadOnlyList<PublicFeatureSettingResponse> Settings);

public sealed record PublicFeatureSourceResponse(
    Guid Id,
    string Name,
    string Url);

public sealed record PublicFeatureSettingResponse(
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

public sealed record PublicFeatureDependencyResponse(
    string? PackageId,
    string? VersionRange,
    string? FeatureId,
    bool Optional,
    string? Reason);

public sealed record PublicFeatureConflictResponse(
    string? PackageId,
    string? VersionRange,
    string? FeatureId,
    string? Reason);

public sealed record PublicFeatureInfrastructureRequirementResponse(
    string Id,
    string Kind,
    bool Optional,
    string? Reason,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Providers,
    IReadOnlyList<string> ConfigurationKeys,
    IReadOnlyDictionary<string, JsonElement> Extensions);
