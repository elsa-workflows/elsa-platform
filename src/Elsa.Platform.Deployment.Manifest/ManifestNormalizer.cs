using System.Text.Json.Nodes;
using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.Resources;

namespace Elsa.Platform.Deployment.Manifest;

public sealed class ManifestNormalizer : IManifestNormalizer
{
    public NormalizedManifest Normalize(EnvironmentManifest manifest, ManifestResourceMapperRegistry? mapperRegistry = null)
    {
        var diagnostics = ManifestValidator.ValidateManifestHeader(manifest).ToList();
        var resources = new List<DeploymentResource>();
        AddWorkflows(manifest.Resources.Workflows, resources, diagnostics);
        AddVariables(manifest.Resources.Variables, resources, diagnostics);
        AddFeatures(manifest.Resources.Features, resources, diagnostics);
        AddPackages(manifest.Resources.Packages, resources, diagnostics);
        AddRecipes(manifest.Resources.Recipes, resources, diagnostics);
        AddExtensions(manifest, mapperRegistry, resources, diagnostics);
        AddDuplicateDiagnostics(resources, diagnostics);
        return new NormalizedManifest(manifest, resources, diagnostics);
    }

    private static void AddWorkflows(
        IEnumerable<WorkflowManifestEntry> entries,
        ICollection<DeploymentResource> resources,
        ICollection<DeploymentDiagnostic> diagnostics)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                diagnostics.Add(ManifestValidator.Error(ManifestDiagnosticCodes.ResourceIdentityRequired, "Workflow id is required."));
                continue;
            }

            var id = new DeploymentResourceId(DeploymentManifestConstants.WorkflowDefinitionResourceType, entry.Id);
            var pathDiagnostic = ManifestPathValidator.Validate(entry.Path, id);
            if (pathDiagnostic is not null)
                diagnostics.Add(pathDiagnostic);

            resources.Add(new DeploymentResource(
                id,
                entry.Version,
                ManifestResourceHasher.Hash(DeploymentManifestConstants.WorkflowDefinitionResourceType, entry),
                MapDependencies(entry.Dependencies),
                metadata: WithPath(entry.Metadata, entry.Path, ("activation", entry.Activation))));
        }
    }

    private static void AddVariables(
        IEnumerable<VariableManifestEntry> entries,
        ICollection<DeploymentResource> resources,
        ICollection<DeploymentDiagnostic> diagnostics)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                diagnostics.Add(ManifestValidator.Error(ManifestDiagnosticCodes.ResourceIdentityRequired, "Variable key is required."));
                continue;
            }

            resources.Add(new DeploymentResource(
                new DeploymentResourceId(DeploymentManifestConstants.VariableResourceType, entry.Key, entry.Scope),
                desiredStateHash: ManifestResourceHasher.Hash(DeploymentManifestConstants.VariableResourceType, entry),
                dependencies: MapDependencies(entry.Dependencies),
                metadata: entry.Metadata));
        }
    }

    private static void AddFeatures(
        IEnumerable<FeatureManifestEntry> entries,
        ICollection<DeploymentResource> resources,
        ICollection<DeploymentDiagnostic> diagnostics)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                diagnostics.Add(ManifestValidator.Error(ManifestDiagnosticCodes.ResourceIdentityRequired, "Feature id is required."));
                continue;
            }

            resources.Add(new DeploymentResource(
                new DeploymentResourceId(DeploymentManifestConstants.FeatureResourceType, entry.Id),
                desiredStateHash: ManifestResourceHasher.Hash(DeploymentManifestConstants.FeatureResourceType, entry),
                dependencies: MapDependencies(entry.Dependencies),
                metadata: WithValue(entry.Metadata, "state", entry.State)));
        }
    }

    private static void AddPackages(
        IEnumerable<PackageManifestEntry> entries,
        ICollection<DeploymentResource> resources,
        ICollection<DeploymentDiagnostic> diagnostics)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                diagnostics.Add(ManifestValidator.Error(ManifestDiagnosticCodes.ResourceIdentityRequired, "Package id is required."));
                continue;
            }

            resources.Add(new DeploymentResource(
                new DeploymentResourceId(DeploymentManifestConstants.PackageResourceType, entry.Id),
                entry.Version,
                ManifestResourceHasher.Hash(DeploymentManifestConstants.PackageResourceType, entry),
                MapDependencies(entry.Dependencies),
                metadata: entry.Metadata));
        }
    }

    private static void AddRecipes(
        IEnumerable<RecipeManifestEntry> entries,
        ICollection<DeploymentResource> resources,
        ICollection<DeploymentDiagnostic> diagnostics)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                diagnostics.Add(ManifestValidator.Error(ManifestDiagnosticCodes.ResourceIdentityRequired, "Recipe id is required."));
                continue;
            }

            var id = new DeploymentResourceId(DeploymentManifestConstants.RecipeResourceType, entry.Id);
            if (!string.IsNullOrWhiteSpace(entry.Path))
            {
                var pathDiagnostic = ManifestPathValidator.Validate(entry.Path, id);
                if (pathDiagnostic is not null)
                    diagnostics.Add(pathDiagnostic);
            }

            resources.Add(new DeploymentResource(
                id,
                entry.Version,
                ManifestResourceHasher.Hash(DeploymentManifestConstants.RecipeResourceType, entry),
                MapDependencies(entry.Dependencies),
                metadata: WithPath(entry.Metadata, entry.Path)));
        }
    }

    private static void AddExtensions(
        EnvironmentManifest manifest,
        ManifestResourceMapperRegistry? mapperRegistry,
        ICollection<DeploymentResource> resources,
        ICollection<DeploymentDiagnostic> diagnostics)
    {
        var context = new ManifestNormalizationContext(manifest, (IList<DeploymentDiagnostic>)diagnostics);
        foreach (var section in manifest.Resources.Extensions)
        {
            if (mapperRegistry?.TryGet(section.Key, out var mapper) == true)
            {
                foreach (var resource in mapper.Map(section.Value, context))
                    resources.Add(resource);
                continue;
            }

            diagnostics.Add(new DeploymentDiagnostic(
                ManifestDiagnosticCodes.ResourceUnsupported,
                DeploymentDiagnosticSeverity.Error,
                $"Resource section '{section.Key}' is not supported."));
        }
    }

    private static void AddDuplicateDiagnostics(
        IEnumerable<DeploymentResource> resources,
        ICollection<DeploymentDiagnostic> diagnostics)
    {
        foreach (var group in resources.GroupBy(x => x.Id).Where(x => x.Count() > 1))
        {
            diagnostics.Add(new DeploymentDiagnostic(
                ManifestDiagnosticCodes.ResourceDuplicate,
                DeploymentDiagnosticSeverity.Error,
                $"Resource '{group.Key}' is declared more than once.",
                group.Key));
        }
    }

    private static IReadOnlyCollection<DeploymentResourceId> MapDependencies(IEnumerable<ManifestResourceReference> dependencies) =>
        dependencies
            .Where(x => !string.IsNullOrWhiteSpace(x.Type) && !string.IsNullOrWhiteSpace(x.Id))
            .Select(x => new DeploymentResourceId(x.Type!, x.Id!, x.Scope))
            .ToArray();

    private static IReadOnlyDictionary<string, string> WithPath(
        IReadOnlyDictionary<string, string> metadata,
        string? path,
        params (string Key, string? Value)[] values)
    {
        var result = new Dictionary<string, string>(metadata);
        if (!string.IsNullOrWhiteSpace(path))
            result["path"] = path;
        foreach (var (key, value) in values)
            if (!string.IsNullOrWhiteSpace(value))
                result[key] = value;
        return result;
    }

    private static IReadOnlyDictionary<string, string> WithValue(IReadOnlyDictionary<string, string> metadata, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return metadata;

        var result = new Dictionary<string, string>(metadata)
        {
            [key] = value
        };
        return result;
    }
}
