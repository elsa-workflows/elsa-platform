namespace Elsa.Platform.Deployment.Manifest;

public sealed class ManifestResourceMapperRegistry
{
    private readonly Dictionary<string, IManifestResourceMapper> _mappers = new(StringComparer.OrdinalIgnoreCase);

    public ManifestResourceMapperRegistry Add(IManifestResourceMapper mapper)
    {
        _mappers[mapper.SectionName] = mapper;
        return this;
    }

    public bool TryGet(string sectionName, out IManifestResourceMapper mapper) =>
        _mappers.TryGetValue(sectionName, out mapper!);
}
