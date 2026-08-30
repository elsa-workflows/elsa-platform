namespace ElsaControl.RuntimeBuilder.Abstractions.Plans;

public static class ResolvedElsaApplicationPlanValidator
{
    public static IReadOnlyList<ResolvedPlanValidationFinding> Validate(ResolvedElsaApplicationPlan? plan)
    {
        if (plan is null)
            return [new("plan.required", "A resolved application plan is required.", "plan")];

        var findings = new List<ResolvedPlanValidationFinding>();
        Required(plan.SchemaVersion, "plan.schema.required", "SchemaVersion is required.", "schemaVersion");
        if (!string.IsNullOrWhiteSpace(plan.SchemaVersion) && plan.SchemaVersion != ResolvedElsaApplicationPlanSchema.CurrentVersion)
            findings.Add(new("plan.schema.unsupported", $"Schema version {plan.SchemaVersion} is not supported.", "schemaVersion"));

        if (plan.Release is null)
            findings.Add(new("release.required", "A release identity is required.", "release"));
        else
        {
            Required(plan.Release.DistributionId, "release.distribution.required", "Release distribution is required.", "release.distributionId");
            Required(plan.Release.ReleaseLine, "release.line.required", "Release line is required.", "release.releaseLine");
            Required(plan.Release.Version, "release.version.required", "Exact release version is required.", "release.version");
            Required(plan.Release.SourceRepository, "release.source.required", "Release source repository is required.", "release.sourceRepository");
            Required(plan.Release.SourceCommit, "release.commit.required", "Release source commit is required.", "release.sourceCommit");
            Required(plan.Release.ReleaseManifestReference, "release.manifest.required", "Release manifest reference is required.", "release.releaseManifestReference");
            Digest(plan.Release.ReleaseManifestDigest, "release.manifestDigest.invalid", "release.releaseManifestDigest", findings);
        }

        var topologyComponents = plan.Topology?.Components;
        if (plan.Topology is null)
            findings.Add(new("topology.required", "A topology is required.", "topology"));
        else
        {
            Required(plan.Topology.Id, "topology.id.required", "Topology identity is required.", "topology.id");
            if (topologyComponents is null || topologyComponents.Count == 0)
                findings.Add(new("topology.components.required", "At least one topology component is required.", "topology.components"));
            Duplicate(topologyComponents, x => x.Id, "topology.component.duplicate", "topology.components");
            foreach (var component in topologyComponents ?? [])
            {
                if (component is null)
                {
                    findings.Add(new("topology.component.null", "Topology components cannot contain null items.", "topology.components"));
                    continue;
                }

                Required(component.Id, "topology.component.id.required", "Component identity is required.", $"topology.components:{component.Id}");
                if (component.Roles is null || component.Roles.Count == 0)
                    findings.Add(new("topology.component.roles.required", "At least one component role is required.", $"component:{component.Id}/roles"));
                if (component.RuntimeKinds is null || component.RuntimeKinds.Count == 0)
                    findings.Add(new("topology.component.runtimeKinds.required", "At least one runtime kind is required.", $"component:{component.Id}/runtimeKinds"));
                ValidateImage(component.Image, findings, $"component:{component.Id}/image");
                if (component.CompanionComponentId is not null && !(topologyComponents ?? []).Any(x => x is not null && string.Equals(x.Id, component.CompanionComponentId, StringComparison.OrdinalIgnoreCase)))
                    findings.Add(new("topology.component.companion.missing", $"Companion component {component.CompanionComponentId} is not present in the topology.", $"component:{component.Id}"));
                foreach (var endpoint in component.Endpoints ?? [])
                {
                    if (endpoint is null)
                    {
                        findings.Add(new("topology.component.endpoint.null", "Topology endpoints cannot contain null items.", $"component:{component.Id}/endpoints"));
                        continue;
                    }
                    ValidateEndpoint(endpoint, findings, $"component:{component.Id}/endpoint:{endpoint.Name}");
                }
            }
        }

        Duplicate(plan.Packages, x => $"{x.SourceId}:{x.PackageId}:{x.Version}", "package.duplicate", "packages");
        foreach (var package in plan.Packages ?? [])
        {
            if (package is null)
            {
                findings.Add(new("package.null", "Packages cannot contain null items.", "packages"));
                continue;
            }

            if (package.SourceId == Guid.Empty)
                findings.Add(new("package.source.required", "Package source identity is required.", $"package:{package.PackageId}"));
            Required(package.PackageId, "package.id.required", "Package identity is required.", "packages");
            Required(package.Version, "package.version.required", "Package version is required.", $"package:{package.PackageId}");
            Digest(package.ManifestDigest, "package.manifestDigest.invalid", $"package:{package.PackageId}/manifestDigest", findings);
            if (package.RuntimeKinds is null || package.RuntimeKinds.Count == 0)
                findings.Add(new("package.runtimeKinds.required", "At least one package runtime kind is required.", $"package:{package.PackageId}/runtimeKinds"));
            Duplicate(package.Features, x => x.Id, "feature.duplicate", $"package:{package.PackageId}");
            foreach (var feature in package.Features ?? [])
            {
                if (feature is null)
                {
                    findings.Add(new("feature.null", "Features cannot contain null items.", $"package:{package.PackageId}/features"));
                    continue;
                }

                Required(feature.Id, "feature.id.required", "Feature identity is required.", $"package:{package.PackageId}/features");
                if (feature.RuntimeKinds is null || feature.RuntimeKinds.Count == 0)
                    findings.Add(new("feature.runtimeKinds.required", "At least one feature runtime kind is required.", $"feature:{feature.Id}/runtimeKinds"));
            }
        }

        if (plan.Configuration is null)
            findings.Add(new("configuration.required", "Configuration shape is required.", "configuration"));
        else
        {
            var validConfigurationKeys = new List<string>();
            foreach (var entry in plan.Configuration.Entries ?? [])
            {
                if (entry is null)
                {
                    findings.Add(new("configuration.entry.null", "Configuration entries cannot contain null items.", "configuration.entries"));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    findings.Add(new("configuration.key.required", "Configuration key is required.", "configuration.entries"));
                    continue;
                }

                if (!ConfigurationKeyPolicy.IsSafe(entry.Key))
                {
                    findings.Add(new("configuration.key.invalid", "Configuration key must be a canonical safe identifier.", "configuration.entries"));
                    continue;
                }

                validConfigurationKeys.Add(entry.Key);
                Required(entry.JsonType, "configuration.type.required", "Configuration JSON type is required.", $"configuration:{entry.Key}");
                if (entry.Secret && entry.Value is not null)
                    findings.Add(new("configuration.secretValue.forbidden", "Secret configuration values must not be embedded in a resolved plan.", $"configuration:{entry.Key}"));
                if (entry.SecretReference is not null && !entry.Secret)
                    findings.Add(new("configuration.nonSecretReference.invalid", "Only secret configuration entries may use a secret reference.", $"configuration:{entry.Key}"));
                if (entry.Secret && entry.SecretReference is not null && !SecretReferencePolicy.IsSafe(entry.SecretReference))
                    findings.Add(new("configuration.secretReference.invalid", SecretReferencePolicy.InvalidReferenceMessage, $"configuration:{entry.Key}"));
                if (entry.Required && entry.Value is null && entry.SecretReference is null)
                    findings.Add(new("configuration.requiredValue.missing", "Required configuration needs a value or secret reference.", $"configuration:{entry.Key}"));
            }

            foreach (var group in validConfigurationKeys
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Where(x => x.Count() > 1))
                findings.Add(new("configuration.key.duplicate", "Configuration contains duplicate setting identities.", "configuration.entries"));
        }

        if (plan.Capacity is null)
            findings.Add(new("capacity.required", "Capacity outcomes are required.", "capacity"));
        else
        {
            Duplicate(plan.Capacity.Components, x => x.ComponentId, "capacity.component.duplicate", "capacity.components");
            foreach (var capacity in plan.Capacity.Components ?? [])
            {
                if (capacity is null)
                {
                    findings.Add(new("capacity.component.null", "Capacity components cannot contain null items.", "capacity.components"));
                    continue;
                }

                Required(capacity.ComponentId, "capacity.component.required", "Capacity component identity is required.", "capacity.components");
                if (!(topologyComponents ?? []).Any(x => x is not null && string.Equals(x.Id, capacity.ComponentId, StringComparison.OrdinalIgnoreCase)))
                    findings.Add(new("capacity.component.unknown", $"Capacity references unknown component {capacity.ComponentId}.", $"capacity:{capacity.ComponentId}"));
                if (capacity.MinReplicas < 0 || capacity.MaxReplicas < capacity.MinReplicas)
                    findings.Add(new("capacity.replicas.invalid", "Replica bounds must be non-negative and max must be at least min.", $"capacity:{capacity.ComponentId}"));
                if (capacity.CpuMillicores <= 0 || capacity.MemoryMiB <= 0)
                    findings.Add(new("capacity.compute.invalid", "CPU and memory capacity must be positive.", $"capacity:{capacity.ComponentId}"));
                if (capacity.EphemeralStorageMiB is < 0)
                    findings.Add(new("capacity.storage.invalid", "Ephemeral storage cannot be negative.", $"capacity:{capacity.ComponentId}"));
            }
            Duplicate(plan.Capacity.Storage, x => x.Name, "capacity.storage.duplicate", "capacity.storage");
            foreach (var storage in plan.Capacity.Storage ?? [])
            {
                if (storage is null)
                {
                    findings.Add(new("capacity.storage.null", "Storage capacities cannot contain null items.", "capacity.storage"));
                    continue;
                }

                Required(storage.Name, "storage.name.required", "Storage identity is required.", "capacity.storage");
                Required(storage.Kind, "storage.kind.required", "Storage kind is required.", $"storage:{storage.Name}");
                if (storage.SizeGiB is < 0)
                    findings.Add(new("storage.size.invalid", "Storage size cannot be negative.", $"storage:{storage.Name}"));
            }
        }

        if (plan.Network is null)
            findings.Add(new("network.required", "Network outcomes are required.", "network"));
        else
        {
            Required(plan.Network.Ingress, "network.ingress.required", "Ingress outcome is required.", "network.ingress");
            Required(plan.Network.Egress, "network.egress.required", "Egress outcome is required.", "network.egress");
            Duplicate(plan.Network.Endpoints, x => $"{x.ComponentId}:{x.Name}", "network.endpoint.duplicate", "network.endpoints");
            foreach (var endpoint in plan.Network.Endpoints ?? [])
            {
                if (endpoint is null)
                {
                    findings.Add(new("network.endpoint.null", "Network endpoints cannot contain null items.", "network.endpoints"));
                    continue;
                }

                if (!(topologyComponents ?? []).Any(x => x is not null && string.Equals(x.Id, endpoint.ComponentId, StringComparison.OrdinalIgnoreCase)))
                    findings.Add(new("network.endpoint.unknownComponent", $"Network endpoint references unknown component {endpoint.ComponentId}.", $"network/endpoint:{endpoint.ComponentId}:{endpoint.Name}"));
                ValidateEndpoint(endpoint, findings, $"network/endpoint:{endpoint.ComponentId}:{endpoint.Name}");
            }
        }

        Required(plan.Isolation, "isolation.required", "Isolation outcome is required.", "isolation");
        if (plan.ReleasePolicy is null)
            findings.Add(new("releasePolicy.required", "Release policy is required.", "releasePolicy"));
        else
        {
            Required(plan.ReleasePolicy.Channel, "releasePolicy.channel.required", "Release channel is required.", "releasePolicy.channel");
            Required(plan.ReleasePolicy.Lifecycle, "releasePolicy.lifecycle.required", "Release lifecycle is required.", "releasePolicy.lifecycle");
            Required(plan.ReleasePolicy.RolloutRing, "releasePolicy.rolloutRing.required", "Rollout ring is required.", "releasePolicy.rolloutRing");
            Required(plan.ReleasePolicy.PatchUpdates, "releasePolicy.patch.required", "Patch update policy is required.", "releasePolicy.patchUpdates");
            Required(plan.ReleasePolicy.MinorUpdates, "releasePolicy.minor.required", "Minor update policy is required.", "releasePolicy.minorUpdates");
            Required(plan.ReleasePolicy.MajorMigrations, "releasePolicy.major.required", "Major migration policy is required.", "releasePolicy.majorMigrations");
        }

        Duplicate(plan.ProviderCapabilities, x => x.Id, "providerCapability.duplicate", "providerCapabilities");
        foreach (var capability in plan.ProviderCapabilities ?? [])
        {
            if (capability is null)
            {
                findings.Add(new("providerCapability.null", "Provider capabilities cannot contain null items.", "providerCapabilities"));
                continue;
            }

            Required(capability.Id, "providerCapability.id.required", "Provider capability identity is required.", "providerCapabilities");
            Required(capability.Description, "providerCapability.description.required", "Provider capability description is required.", $"providerCapability:{capability.Id}");
        }

        foreach (var evidence in plan.Evidence ?? [])
        {
            if (evidence is null)
            {
                findings.Add(new("evidence.null", "Evidence cannot contain null items.", "evidence"));
                continue;
            }

            Required(evidence.Kind, "evidence.kind.required", "Evidence kind is required.", "evidence");
            Required(evidence.Reference, "evidence.reference.required", "Evidence reference is required.", $"evidence:{evidence.Kind}");
            Required(evidence.Description, "evidence.description.required", "Evidence description is required.", $"evidence:{evidence.Kind}");
            if (evidence.Digest is not null)
                Digest(evidence.Digest, "evidence.digest.invalid", $"evidence:{evidence.Kind}", findings);
        }

        return findings;

        void Required(string? value, string code, string message, string scope)
        {
            if (string.IsNullOrWhiteSpace(value))
                findings.Add(new(code, message, scope));
        }

        void Duplicate<T>(IEnumerable<T>? items, Func<T, string> keySelector, string code, string scope)
        {
            if (items is null)
                return;

            var values = items.ToList();
            if (values.Any(x => x is null))
                findings.Add(new($"{code}.null", "Collection cannot contain null items.", scope));

            foreach (var group in values.Where(x => x is not null).Select(x => keySelector(x!)).GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
                findings.Add(new(code, $"Duplicate identity {group.Key} is not allowed.", scope));
        }
    }

    private static void ValidateImage(ResolvedImageIdentity? image, List<ResolvedPlanValidationFinding> findings, string scope)
    {
        if (image is null)
        {
            findings.Add(new("image.required", "An immutable image identity is required.", scope));
            return;
        }

        if (string.IsNullOrWhiteSpace(image.RegistryClass))
            findings.Add(new("image.registryClass.required", "Image registry class is required.", scope));
        if (string.IsNullOrWhiteSpace(image.Repository))
            findings.Add(new("image.repository.required", "Image repository is required.", scope));
        if (string.IsNullOrWhiteSpace(image.Reference) || !image.Reference.Contains("@sha256:", StringComparison.OrdinalIgnoreCase))
            findings.Add(new("image.reference.immutableRequired", "Image reference must use an immutable sha256 digest, not a tag.", scope));
        Digest(image.Digest, "image.digest.invalid", scope, findings);
        if (image.Reference is not null)
        {
            var digestMarker = image.Reference.IndexOf("@sha256:", StringComparison.OrdinalIgnoreCase);
            if (digestMarker >= 0 && !string.Equals(image.Reference[(digestMarker + 1)..], image.Digest, StringComparison.OrdinalIgnoreCase))
                findings.Add(new("image.referenceDigest.mismatch", "Image reference digest must match the image digest field.", scope));
        }
        if (image.PlatformDigests is not null)
        {
            foreach (var platformGroup in image.PlatformDigests.Keys.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
                findings.Add(new("image.platform.duplicate", $"Platform identity {platformGroup.Key} is duplicated case-insensitively.", scope));

            foreach (var (platform, digest) in image.PlatformDigests)
            {
                if (string.IsNullOrWhiteSpace(platform))
                    findings.Add(new("image.platform.required", "Platform identity is required.", scope));
                Digest(digest, "image.platformDigest.invalid", $"{scope}/{platform}", findings);
            }
        }
    }

    private static void ValidateEndpoint(ResolvedElsaEndpoint? endpoint, List<ResolvedPlanValidationFinding> findings, string scope)
    {
        if (endpoint is null)
        {
            findings.Add(new("endpoint.null", "Endpoints cannot contain null items.", scope));
            return;
        }

        if (string.IsNullOrWhiteSpace(endpoint.Name))
            findings.Add(new("endpoint.name.required", "Endpoint name is required.", scope));
        if (string.IsNullOrWhiteSpace(endpoint.Protocol))
            findings.Add(new("endpoint.protocol.required", "Endpoint protocol is required.", scope));
        if (endpoint.Port is < 1 or > 65535)
            findings.Add(new("endpoint.port.invalid", "Endpoint port must be between 1 and 65535.", scope));
        if (string.IsNullOrWhiteSpace(endpoint.Visibility))
            findings.Add(new("endpoint.visibility.required", "Endpoint visibility is required.", scope));
        if (!string.IsNullOrWhiteSpace(endpoint.Path) && !EndpointPathPolicy.IsSafe(endpoint.Path))
            findings.Add(new("endpoint.path.invalid", "Endpoint paths must be safe relative paths.", scope));
    }

    private static void ValidateEndpoint(ResolvedNetworkEndpoint? endpoint, List<ResolvedPlanValidationFinding> findings, string scope)
    {
        if (endpoint is null)
        {
            findings.Add(new("network.endpoint.null", "Network endpoints cannot contain null items.", scope));
            return;
        }

        if (string.IsNullOrWhiteSpace(endpoint.ComponentId))
            findings.Add(new("network.endpoint.component.required", "Network endpoint component identity is required.", scope));
        ValidateEndpoint(new ResolvedElsaEndpoint(endpoint.Name, endpoint.Protocol, endpoint.Port, endpoint.Visibility, endpoint.RequiresTls, endpoint.Path), findings, scope);
    }

    private static void Digest(string? value, string code, string scope, List<ResolvedPlanValidationFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 71 || !value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || value[7..].Any(x => !Uri.IsHexDigit(x)))
            findings.Add(new(code, "A sha256 digest is required.", scope));
    }

}

public sealed record ResolvedPlanValidationFinding(string Code, string Message, string Scope);
