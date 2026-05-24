namespace Elsa.Platform.Api.Public.Sources;

public sealed record PublicSourceResponse(
    Guid Id,
    string Name,
    string Url,
    int PackageCount);
