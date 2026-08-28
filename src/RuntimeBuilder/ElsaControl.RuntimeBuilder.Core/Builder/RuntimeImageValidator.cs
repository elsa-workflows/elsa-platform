using ElsaControl.RuntimeBuilder.Abstractions;

namespace ElsaControl.RuntimeBuilder.Core.Builder;

public sealed class RuntimeImageValidator
{
    public IReadOnlyList<RuntimeImageValidationFinding> Validate(IReadOnlyList<RuntimeImage> images)
    {
        var findings = new List<RuntimeImageValidationFinding>();
        // Images are configuration-defined, so a missing or misspelled section would otherwise leave the
        // runtime builder silently offering nothing to build.
        if (images.Count == 0)
            findings.Add(new("runtimeImage.emptyCatalog", $"No runtime images are configured under '{RuntimeBuilderOptions.SectionName}:{nameof(RuntimeBuilderOptions.Images)}'.", "catalog"));

        foreach (var group in images.GroupBy(x => x.Slug, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            findings.Add(new("runtimeImage.duplicateSlug", $"Runtime image slug {group.Key} is duplicated.", $"image:{group.Key}"));

        var slugs = images.Select(x => x.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var image in images)
        {
            if (string.IsNullOrWhiteSpace(image.Slug))
                findings.Add(new("runtimeImage.missingSlug", "Runtime image slug is required.", "image"));
            if (string.IsNullOrWhiteSpace(image.Image))
                findings.Add(new("runtimeImage.missingImage", $"Runtime image {image.Slug} requires a Docker image reference.", $"image:{image.Slug}"));
            if (image.AvailableTags.Count == 0)
                findings.Add(new("runtimeImage.missingTags", $"Runtime image {image.Slug} requires at least one available tag.", $"image:{image.Slug}"));
            if (string.IsNullOrWhiteSpace(image.DefaultTag) || !image.AvailableTags.Contains(image.DefaultTag, StringComparer.OrdinalIgnoreCase))
                findings.Add(new("runtimeImage.invalidDefaultTag", $"Runtime image {image.Slug} default tag must be listed in availableTags.", $"image:{image.Slug}"));
            if (!image.DeploymentHints.SupportsDockerCompose)
                findings.Add(new("runtimeImage.composeUnsupported", $"Runtime image {image.Slug} must support Docker Compose in the first slice.", $"image:{image.Slug}"));

            foreach (var runtimeKind in image.RuntimeKinds)
            {
                if (string.IsNullOrWhiteSpace(runtimeKind))
                    findings.Add(new("runtimeImage.blankRuntimeKind", $"Runtime image {image.Slug} has a blank runtime kind.", $"image:{image.Slug}/runtimeKinds"));
            }

            foreach (var duplicateRuntimeKind in image.RuntimeKinds.Where(x => !string.IsNullOrWhiteSpace(x)).GroupBy(x => x.Trim(), StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
                findings.Add(new("runtimeImage.duplicateRuntimeKind", $"Runtime image {image.Slug} has duplicate runtime kind {duplicateRuntimeKind.Key}.", $"image:{image.Slug}/runtimeKinds:{duplicateRuntimeKind.Key}"));

            foreach (var duplicateEnv in image.EnvVars.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
                findings.Add(new("runtimeImage.duplicateEnvVar", $"Runtime image {image.Slug} has duplicate environment variable {duplicateEnv.Key}.", $"image:{image.Slug}/env:{duplicateEnv.Key}"));

            if (image.DeploymentHints.RequiresCompanionServer)
            {
                if (string.IsNullOrWhiteSpace(image.DeploymentHints.CompanionImageSlug))
                    findings.Add(new("runtimeImage.missingCompanion", $"Runtime image {image.Slug} requires a companion image slug.", $"image:{image.Slug}"));
                else if (!slugs.Contains(image.DeploymentHints.CompanionImageSlug))
                    findings.Add(new("runtimeImage.brokenCompanion", $"Runtime image {image.Slug} companion image {image.DeploymentHints.CompanionImageSlug} is not defined.", $"image:{image.Slug}"));
            }
        }

        return findings;
    }
}

public sealed record RuntimeImageValidationFinding(string Code, string Message, string Scope);
