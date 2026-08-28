namespace ElsaControl.Deployment.Manifest;

public sealed class ManifestResourceMapperRegistry
{
    private readonly Dictionary<string, IManifestResourceMapper> _mappers = new(StringComparer.OrdinalIgnoreCase);

    public ManifestResourceMapperRegistry Add(IManifestResourceMapper mapper)
    {
        if (!_mappers.TryAdd(mapper.SectionName, mapper))
            throw new InvalidOperationException($"A manifest resource mapper for section '{mapper.SectionName}' is already registered.");

        return this;
    }

    public bool TryGet(string sectionName, out IManifestResourceMapper mapper) =>
        _mappers.TryGetValue(sectionName, out mapper!);
}
