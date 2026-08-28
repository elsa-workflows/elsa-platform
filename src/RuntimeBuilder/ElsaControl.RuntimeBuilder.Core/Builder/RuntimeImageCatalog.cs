using Microsoft.Extensions.Options;
using ElsaControl.RuntimeBuilder.Abstractions;

namespace ElsaControl.RuntimeBuilder.Core.Builder;

public sealed class RuntimeImageCatalog
{
    private readonly IReadOnlyList<RuntimeImage> _images;

    public RuntimeImageCatalog(IOptions<RuntimeBuilderOptions> options)
        : this(options.Value.ToRuntimeImages())
    {
    }

    private RuntimeImageCatalog(IEnumerable<RuntimeImage> images) => _images = [.. images];

    /// <summary>Composes a catalog from images already in hand, for callers that do not read configuration.</summary>
    public static RuntimeImageCatalog Create(IEnumerable<RuntimeImage> images) => new(images);

    public IReadOnlyList<RuntimeImage> ListImages() => _images;

    public RuntimeImage? Find(string slug) =>
        _images.FirstOrDefault(x => string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
