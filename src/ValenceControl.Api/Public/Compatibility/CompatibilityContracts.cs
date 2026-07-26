namespace ValenceControl.Api.Public.Compatibility;

public sealed record CompatibilityCheckApiRequest(
    string? ElsaVersion,
    string? DockerImageVersion,
    IReadOnlyList<SelectedPackageVersionApiRequest> Packages,
    IReadOnlyList<string>? Features);

public sealed record SelectedPackageVersionApiRequest(Guid SourceId, string PackageId, string Version);

public sealed record CompatibilityCheckApiResponse(
    bool Compatible,
    IReadOnlyList<CompatibilityFindingApiResponse> Findings);

public sealed record CompatibilityFindingApiResponse(string Severity, string Code, string Message);
