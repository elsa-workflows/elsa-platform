using System.Collections.Frozen;
using System.Text.Json;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.ComponentManifest;
using Elsa.Platform.Healing.Core.Configuration;
using Elsa.Platform.Healing.Core.Manifests;
using Elsa.Platform.Healing.Core.Ownership;
using Elsa.Platform.Healing.Core.Security;
using FluentAssertions;
using ComponentManifestEntity = Elsa.Platform.Healing.Core.ComponentManifest;

namespace Elsa.Platform.Healing.Core.Tests.Ownership;

public sealed class SourceOwnershipServiceTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _applicationId = Guid.NewGuid();

    [Fact]
    public async Task Configuration_applies_environment_overrides_and_rejects_cross_application_authority()
    {
        var store = new FakeOwnershipStore();
        var service = new HealingConfigurationService(store, Audit(store), TimeProvider(_now));
        var environmentId = Guid.NewGuid();
        var configuration = Configuration(environmentId);

        var saved = await service.SaveAsync(configuration, Owner());
        var effective = await service.GetEffectiveAsync(_workspaceId, _applicationId, environmentId);
        var denied = await service.SaveAsync(configuration, Owner(applicationId: Guid.NewGuid()));

        saved.Succeeded.Should().BeTrue();
        effective.Should().BeEquivalentTo(new EffectiveHealingConfiguration(
            DiscoveryEnabled: false,
            RepairEnabled: true,
            AutomaticMergeEnabled: false,
            OccurrenceThreshold: 7,
            DebounceWindow: TimeSpan.FromMinutes(3),
            ApplicationKillSwitch: false,
            EnvironmentKillSwitch: false));
        denied.Succeeded.Should().BeFalse();
        denied.ReasonCode.Should().Be(HealingOwnershipReasonCodes.Unauthorized);
    }

    [Fact]
    public async Task Enabling_automatic_merge_requires_its_distinct_permission()
    {
        var store = new FakeOwnershipStore();
        var service = new HealingConfigurationService(store, Audit(store), TimeProvider(_now));
        var configuration = Configuration();
        configuration.AutomaticMergeEnabled = true;

        var denied = await service.SaveAsync(configuration, Owner(permissions: new[] { HealingPermissions.Configure }.ToFrozenSet(StringComparer.Ordinal)));
        var allowed = await service.SaveAsync(configuration, Owner());

        denied.ReasonCode.Should().Be(HealingOwnershipReasonCodes.AutomaticMergePermissionRequired);
        allowed.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Authority_profile_with_automatic_merge_requires_its_distinct_permission()
    {
        var store = new FakeOwnershipStore();
        var service = new HealingAdministrationService(
            store, store, new FakeProviderValidator("repository-1"), Audit(store), TimeProvider(_now));
        var request = AuthorityRequest(automaticMergeEnabled: true);

        var denied = await service.CreateProfileAsync(
            _workspaceId,
            _applicationId,
            request,
            Owner(permissions: new[] { HealingPermissions.Configure }.ToFrozenSet(StringComparer.Ordinal)));
        var allowed = await service.CreateProfileAsync(
            _workspaceId,
            _applicationId,
            request,
            Owner());

        denied.ReasonCode.Should().Be(HealingOwnershipReasonCodes.AutomaticMergePermissionRequired);
        allowed.Succeeded.Should().BeTrue();
        allowed.Value!.MergePolicy.AutomaticMergeEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Suspended_provider_can_only_reactivate_through_successful_revalidation()
    {
        var store = new FakeOwnershipStore();
        var provider = SeedAuthority(store, "repository-1");
        provider.Status = ProviderConnectionStatus.Suspended;
        provider.Version = [1];
        var validator = new FakeProviderValidator("repository-1");
        var service = new HealingAdministrationService(
            store, store, validator, Audit(store), TimeProvider(_now));

        var direct = await service.TransitionProviderAsync(
            _workspaceId, _applicationId, provider.Id, ProviderConnectionStatus.Active, provider.Version, Owner());
        var revalidated = await service.ValidateProviderAsync(
            _workspaceId, _applicationId, provider.Id, provider.Version, Owner());

        direct.ReasonCode.Should().Be(HealingOwnershipReasonCodes.InvalidBindingTransition);
        revalidated.Succeeded.Should().BeTrue();
        revalidated.Value!.Status.Should().Be(ProviderConnectionStatus.Active);
        validator.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Unrelated_configuration_changes_do_not_reauthorize_unchanged_automatic_merge_and_emergency_stop_is_governed()
    {
        var store = new FakeOwnershipStore();
        var service = new HealingConfigurationService(store, Audit(store), TimeProvider(_now));
        var configuration = Configuration();
        configuration.AutomaticMergeEnabled = true;
        (await service.SaveAsync(configuration, Owner())).Succeeded.Should().BeTrue();
        configuration.InferenceBudget++;

        var updated = await service.SaveAsync(
            configuration,
            Owner(permissions: new[] { HealingPermissions.Configure }.ToFrozenSet(StringComparer.Ordinal)));
        var stopped = await service.EmergencyStopAsync(
            _workspaceId,
            _applicationId,
            Owner(permissions: new[] { HealingPermissions.Configure }.ToFrozenSet(StringComparer.Ordinal)));
        stopped.Succeeded.Should().BeTrue();
        stopped.Value!.ApplicationKillSwitch.Should().BeTrue();
        var resumed = await service.ResumeAsync(
            _workspaceId,
            _applicationId,
            Owner(permissions: new[] { HealingPermissions.Configure }.ToFrozenSet(StringComparer.Ordinal)));

        updated.Succeeded.Should().BeTrue();
        resumed.Succeeded.Should().BeTrue();
        resumed.Value!.ApplicationKillSwitch.Should().BeFalse();
        store.AuditEvents.Select(x => x.EventType).Should().Contain(["emergency-stop-activated", "emergency-stop-cleared"]);
    }

    [Fact]
    public async Task Configuration_cannot_exceed_platform_budget_maxima()
    {
        var store = new FakeOwnershipStore();
        var service = new HealingConfigurationService(store, Audit(store), TimeProvider(_now));
        Action<HealingConfiguration>[] mutations =
        {
            x => x.DefaultAttemptLimit = HealingBudgetOptions.MaximumRepairAttempts + 1,
            x => x.TimeBudget = HealingBudgetOptions.MaximumTimeBudget + TimeSpan.FromSeconds(1),
            x => x.ConcurrencyBudget = HealingBudgetOptions.MaximumConcurrency + 1,
            x => x.InferenceBudget = HealingBudgetOptions.MaximumInferenceUnits + 1,
            x => x.RepositoryRunBudget = HealingBudgetOptions.MaximumRepositoryRuns + 1
        };

        foreach (var mutate in mutations)
        {
            var configuration = Configuration();
            mutate(configuration);
            var result = await service.SaveAsync(configuration, Owner());
            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be(HealingOwnershipReasonCodes.InvalidConfiguration);
        }
    }

    [Fact]
    public async Task Manifest_registration_is_revision_immutable_and_trust_is_owner_controlled_and_revocable()
    {
        var store = new FakeOwnershipStore();
        var service = new ComponentManifestService(store, Audit(store), TimeProvider(_now));
        var manifest = Manifest(repositoryUrl: "https://github.com/acme/workflows");

        var registered = await service.RegisterAsync(manifest, Owner());
        var replay = await service.RegisterAsync(CloneManifest(manifest), Owner());
        var conflict = await service.RegisterAsync(Manifest(
            "https://github.com/acme/workflows", manifest.RevisionId, "2.4.2"), Owner());
        var deniedTrust = await service.VerifyByOwnerAsync(
            _workspaceId, _applicationId, manifest.Id, Owner(isOwner: false));
        var verified = await service.VerifyByOwnerAsync(
            _workspaceId, _applicationId, manifest.Id, Owner());
        var revoked = await service.RevokeAsync(_workspaceId, _applicationId, manifest.Id, Owner());

        registered.Succeeded.Should().BeTrue();
        replay.IsReplay.Should().BeTrue();
        conflict.ReasonCode.Should().Be(HealingOwnershipReasonCodes.ImmutableRevisionConflict);
        deniedTrust.ReasonCode.Should().Be(HealingOwnershipReasonCodes.OwnerApprovalRequired);
        verified.Succeeded.Should().BeTrue();
        verified.Value!.VerificationMethod.Should().Be("workspace-owner-verification");
        ComponentManifestService.IsAutomationAuthoritative(verified.Value).Should().BeFalse();
        revoked.Succeeded.Should().BeTrue();
        (await store.GetManifestAsync(_workspaceId, _applicationId, manifest.Id, default))!.TrustState
            .Should().Be(ComponentManifestTrustState.Revoked);
    }

    [Fact]
    public async Task Manifest_registration_idempotency_key_is_bound_to_the_request_payload()
    {
        var store = new FakeOwnershipStore();
        var service = new ComponentManifestService(store, Audit(store), TimeProvider(_now));
        var manifest = Manifest(repositoryUrl: "https://github.com/acme/workflows");

        var registered = await service.RegisterAsync(manifest, "delivery-42", "sha256:payload-a", Owner());
        var replay = await service.RegisterAsync(CloneManifest(manifest), "delivery-42", "sha256:payload-a", Owner());
        var conflict = await service.RegisterAsync(CloneManifest(manifest), "delivery-42", "sha256:payload-b", Owner());

        registered.Succeeded.Should().BeTrue();
        registered.IsReplay.Should().BeFalse();
        replay.Succeeded.Should().BeTrue();
        replay.IsReplay.Should().BeTrue();
        replay.Manifest!.Id.Should().Be(registered.Manifest!.Id);
        conflict.Succeeded.Should().BeFalse();
        conflict.ReasonCode.Should().Be(HealingOwnershipReasonCodes.IdempotencyConflict);
    }

    [Fact]
    public async Task Manifest_registration_projects_authority_fields_only_from_the_canonical_document()
    {
        var store = new FakeOwnershipStore();
        var service = new ComponentManifestService(store, Audit(store), TimeProvider(_now));
        var manifest = Manifest(repositoryUrl: "https://github.com/acme/workflows");
        manifest.SchemaVersion = "evil";
        manifest.SourceRevision = new string('f', 40);
        manifest.BuildId = "evil-build";
        manifest.ManifestDigest = $"sha256:{new string('f', 64)}";
        manifest.CreatedAt = _now.AddYears(1);
        manifest.Entries.Single().PackageId = "Evil.Package";
        manifest.Entries.Single().AssemblyName = "Evil.Assembly";
        manifest.Entries.Single().RepositoryUrl = "https://github.com/evil/repository";
        manifest.Entries.Single().ContentHash = $"sha256:{new string('f', 64)}";

        var result = await service.RegisterAsync(manifest, Owner());

        result.Succeeded.Should().BeTrue();
        result.Manifest!.SchemaVersion.Should().Be("1.0");
        result.Manifest.SourceRevision.Should().Be(new string('a', 40));
        result.Manifest.BuildId.Should().Be("build-1");
        result.Manifest.CreatedAt.Should().Be(_now);
        result.Manifest.ManifestDigest.Should().NotBe($"sha256:{new string('f', 64)}");
        var component = result.Manifest.Entries.Should().ContainSingle().Subject;
        component.PackageId.Should().Be("Acme.Workflows");
        component.AssemblyName.Should().BeNull();
        component.RepositoryUrl.Should().Be("https://github.com/acme/workflows");
        SourceOwnershipService.Matches(Binding(SourceSelectorKind.Package, "Evil.Package"), component).Should().BeFalse();
        SourceOwnershipService.Matches(Binding(SourceSelectorKind.Assembly, "Evil.Assembly"), component).Should().BeFalse();
    }

    [Fact]
    public async Task Owner_cannot_self_assert_an_automation_authoritative_trust_method()
    {
        var store = new FakeOwnershipStore();
        var service = new ComponentManifestService(store, Audit(store), TimeProvider(_now));
        var manifest = Manifest();
        (await service.RegisterAsync(manifest, Owner())).Succeeded.Should().BeTrue();

        var result = await service.VerifyAsync(
            _workspaceId, _applicationId, manifest.Id,
            ManifestTrustMethod.PlatformManagedBuildAttestation, Owner());

        result.ReasonCode.Should().Be(HealingOwnershipReasonCodes.TrustedAttestationRequired);
        store.Manifests.Single().TrustState.Should().Be(ComponentManifestTrustState.Unverified);
    }

    [Fact]
    public async Task Trusted_attestation_boundary_can_establish_automation_authoritative_trust()
    {
        var store = new FakeOwnershipStore();
        var authority = new FakeAttestationAuthority();
        var service = new ComponentManifestService(store, Audit(store), TimeProvider(_now), authority);
        var manifest = Manifest();
        (await service.RegisterAsync(manifest, Owner())).Succeeded.Should().BeTrue();
        (await service.VerifyByOwnerAsync(_workspaceId, _applicationId, manifest.Id, Owner()))
            .Value!.VerificationMethod.Should().Be("workspace-owner-verification");

        var evidence = new ComponentManifestAttestationEvidence(manifest.ManifestDigest, "build-1");
        var result = await service.VerifyAttestedAsync(_workspaceId, _applicationId, manifest.Id, evidence);

        result.Succeeded.Should().BeTrue();
        result.Value!.VerificationMethod.Should().Be("platform-managed-build-attestation");
        ComponentManifestService.IsAutomationAuthoritative(result.Value).Should().BeTrue();
        authority.Request.Should().BeEquivalentTo(new ComponentManifestAttestationRequest(
            _workspaceId,
            _applicationId,
            manifest.RevisionId,
            new string('a', 40),
            "https://github.com/acme/workflow-host",
            "build-1",
            _now,
            manifest.ManifestDigest,
            manifest.CanonicalJson));
        authority.Evidence.Should().Be(evidence);
        store.AuditEvents.Last().ActorType.Should().Be(HealingActorTypes.Platform);
        store.AuditEvents.Last().ActorId.Should().Be("build-attestor");
    }

    [Fact]
    public async Task Trusted_attestation_boundary_rejects_owner_verification_disguised_as_attestation()
    {
        var store = new FakeOwnershipStore();
        var authority = new FakeAttestationAuthority
        {
            Decision = new ComponentManifestAttestationDecision(
                true,
                ManifestTrustMethod.WorkspaceOwnerVerification,
                HealingActorTypes.Human,
                "owner",
                HealingOwnershipReasonCodes.Succeeded)
        };
        var service = new ComponentManifestService(store, Audit(store), TimeProvider(_now), authority);
        var manifest = Manifest();
        (await service.RegisterAsync(manifest, Owner())).Succeeded.Should().BeTrue();

        var result = await service.VerifyAttestedAsync(
            _workspaceId,
            _applicationId,
            manifest.Id,
            new ComponentManifestAttestationEvidence(manifest.ManifestDigest, "build-1"));

        result.ReasonCode.Should().Be(HealingOwnershipReasonCodes.AttestationRejected);
        store.Manifests.Single().TrustState.Should().Be(ComponentManifestTrustState.Unverified);
    }

    [Fact]
    public async Task Owner_verified_manifest_is_inspectable_but_not_eligible_for_automatic_resolution()
    {
        var store = TrustedStore(verificationMethod: "workspace-owner-verification");
        var authority = SeedAuthority(store);
        var binding = Binding(SourceSelectorKind.Package, "Acme.*", authority: authority);
        TrustBinding(store, binding);
        binding.Status = SourceOwnershipBindingStatus.Active;
        store.Bindings.Add(binding);
        var service = new SourceOwnershipService(store, Audit(store), TimeProvider(_now));

        var resolution = await service.ResolveAsync(
            _workspaceId, _applicationId, store.Manifests.Single().Entries.Single(), Owner());

        resolution.Status.Should().Be(SourceOwnershipResolutionStatus.ManifestNotTrusted);
        (await new ComponentManifestService(store, Audit(store), TimeProvider(_now)).ListAsync(
            _workspaceId, _applicationId, Owner())).Should().ContainSingle();
    }

    [Fact]
    public async Task Manifest_registration_accepts_a_zero_assembly_metapackage_without_inventing_a_component_path()
    {
        var store = new FakeOwnershipStore();
        var service = new ComponentManifestService(store, Audit(store), TimeProvider(_now));
        var manifest = Manifest(includeAssemblies: false);

        var result = await service.RegisterAsync(manifest, Owner());

        result.Succeeded.Should().BeTrue();
        result.Manifest!.Entries.Single().Assemblies.Should().BeEmpty();
        result.Manifest.Entries.Single().RelativePath.Should().BeNull();
    }

    [Fact]
    public async Task Repository_metadata_is_only_a_suggestion_and_never_mutation_authority()
    {
        var store = TrustedStore();
        var service = new SourceOwnershipService(store, Audit(store), TimeProvider(_now));
        var manifest = store.Manifests.Single();

        var suggestions = await service.SuggestAsync(_workspaceId, _applicationId, manifest.Id, Owner());
        var resolution = await service.ResolveAsync(
            _workspaceId, _applicationId, manifest.Entries.Single(), Owner());

        suggestions.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new SourceOwnershipSuggestion("https://github.com/acme/workflows", [manifest.Entries.Single().Id], false));
        resolution.Status.Should().Be(SourceOwnershipResolutionStatus.Unauthorized);
        resolution.SelectedBinding.Should().BeNull();
    }

    [Fact]
    public void Unknown_component_kind_remains_visible_but_cannot_match_any_authority_selector()
    {
        var component = new Elsa.Platform.Healing.Core.ComponentManifestEntry
        {
            Id = Guid.NewGuid(), WorkspaceId = _workspaceId, ApplicationId = _applicationId,
            ComponentKey = "future:Acme.Special:1", Kind = ComponentKind.Unknown, KindName = "future-kind",
            Name = "Acme.Special", PackageId = "Acme.Special", AssemblyName = "Acme.Special"
        };

        SourceOwnershipService.Matches(Binding(SourceSelectorKind.ComponentKey, "*"), component).Should().BeFalse();
        SourceOwnershipService.Matches(Binding(SourceSelectorKind.Package, "*"), component).Should().BeFalse();
        component.KindName.Should().Be("future-kind");
    }

    [Fact]
    public void Assembly_selector_matches_any_losslessly_persisted_component_assembly()
    {
        var component = Manifest().Entries.Single();
        component.AssemblyName = null;

        SourceOwnershipService.Matches(
            Binding(SourceSelectorKind.Assembly, "Acme.Workflows.Contracts"), component).Should().BeTrue();
    }

    [Fact]
    public async Task Active_binding_matches_package_and_assembly_selectors_deterministically()
    {
        var store = TrustedStore();
        var authority = SeedAuthority(store);
        var service = new SourceOwnershipService(store, Audit(store), TimeProvider(_now));
        var packageBinding = Binding(SourceSelectorKind.Package, "Acme.*", priority: 10, authority);
        var assemblyBinding = Binding(SourceSelectorKind.Assembly, "Acme.Workflows", priority: 1, authority);
        TrustBinding(store, packageBinding);
        TrustBinding(store, assemblyBinding);

        (await service.ActivateAsync(packageBinding, Owner())).Succeeded.Should().BeTrue();
        (await service.ActivateAsync(assemblyBinding, Owner())).Succeeded.Should().BeTrue();
        var resolution = await service.ResolveAsync(
            _workspaceId, _applicationId, store.Manifests.Single().Entries.Single(), Owner());

        resolution.Status.Should().Be(SourceOwnershipResolutionStatus.Selected);
        resolution.SelectedBinding!.Id.Should().Be(packageBinding.Id);
        resolution.MatchingBindings.Select(x => x.Id).Should().Equal(packageBinding.Id, assemblyBinding.Id);
    }

    [Fact]
    public async Task Different_active_repair_authorities_are_ambiguous_even_when_priorities_differ()
    {
        var store = TrustedStore();
        var firstAuthority = SeedAuthority(store);
        var secondAuthority = SeedAuthority(store, "repo-2", "alternate-workflows");
        var service = new SourceOwnershipService(store, Audit(store), TimeProvider(_now));
        var first = Binding(SourceSelectorKind.Package, "Acme.*", 100, firstAuthority);
        var second = Binding(SourceSelectorKind.Assembly, "Acme.Workflows", 1, secondAuthority);
        TrustBinding(store, first);
        TrustBinding(store, second);
        first.Status = SourceOwnershipBindingStatus.Active;
        second.Status = SourceOwnershipBindingStatus.Active;
        store.Bindings.Add(first);
        store.Bindings.Add(second);

        var resolution = await service.ResolveAsync(
            _workspaceId, _applicationId, store.Manifests.Single().Entries.Single(), Owner());

        resolution.Status.Should().Be(SourceOwnershipResolutionStatus.Ambiguous);
        resolution.SelectedBinding.Should().BeNull();
        resolution.ReasonCodes.Should().ContainSingle(HealingOwnershipReasonCodes.AmbiguousAuthority);
    }

    [Fact]
    public async Task Resolution_immediately_loses_authority_when_provider_is_revoked()
    {
        var store = TrustedStore();
        var authority = SeedAuthority(store);
        var binding = Binding(SourceSelectorKind.Package, "Acme.*", authority: authority);
        TrustBinding(store, binding);
        var service = new SourceOwnershipService(store, Audit(store), TimeProvider(_now));
        (await service.ActivateAsync(binding, Owner())).Succeeded.Should().BeTrue();
        authority.Status = ProviderConnectionStatus.Revoked;

        var resolution = await service.ResolveAsync(
            _workspaceId, _applicationId, store.Manifests.Single().Entries.Single(), Owner());

        resolution.Status.Should().Be(SourceOwnershipResolutionStatus.Unauthorized);
        resolution.SelectedBinding.Should().BeNull();
        resolution.ReasonCodes.Should().Contain(HealingOwnershipReasonCodes.ProviderNotAuthorized);
    }

    [Fact]
    public async Task Read_only_workspace_member_can_resolve_effective_ownership_without_mutation_authority()
    {
        var store = TrustedStore();
        var authority = SeedAuthority(store);
        var binding = Binding(SourceSelectorKind.Package, "Acme.*", authority: authority);
        TrustBinding(store, binding);
        var service = new SourceOwnershipService(store, Audit(store), TimeProvider(_now));
        (await service.ActivateAsync(binding, Owner())).Succeeded.Should().BeTrue();
        var reader = new HealingAuthorization(
            _workspaceId,
            _applicationId,
            "reader",
            false,
            new[] { HealingPermissions.Read }.ToFrozenSet(StringComparer.Ordinal));

        var resolution = await service.ResolveAsync(
            _workspaceId, _applicationId, store.Manifests.Single().Entries.Single(), reader);

        resolution.Status.Should().Be(SourceOwnershipResolutionStatus.Selected);
        resolution.SelectedBinding!.Id.Should().Be(binding.Id);
    }

    [Fact]
    public async Task Resolution_requires_exact_trusted_manifest_membership_and_ignores_forged_component_fields()
    {
        var store = TrustedStore();
        var authority = SeedAuthority(store);
        var binding = Binding(SourceSelectorKind.Package, "Acme.*", authority: authority);
        TrustBinding(store, binding);
        var service = new SourceOwnershipService(store, Audit(store), TimeProvider(_now));
        (await service.ActivateAsync(binding, Owner())).Succeeded.Should().BeTrue();
        var persisted = store.Manifests.Single().Entries.Single();
        var forged = new Elsa.Platform.Healing.Core.ComponentManifestEntry
        {
            Id = persisted.Id,
            ManifestId = persisted.ManifestId,
            WorkspaceId = persisted.WorkspaceId,
            ApplicationId = persisted.ApplicationId,
            ComponentKey = "nuget:Evil.Package:1.0.0",
            Kind = ComponentKind.Package,
            KindName = "package",
            Name = "Evil.Package",
            PackageId = "Evil.Package"
        };
        var wrongEntry = new Elsa.Platform.Healing.Core.ComponentManifestEntry
        {
            Id = Guid.NewGuid(),
            ManifestId = persisted.ManifestId,
            WorkspaceId = persisted.WorkspaceId,
            ApplicationId = persisted.ApplicationId
        };

        var forgedResolution = await service.ResolveAsync(_workspaceId, _applicationId, forged, Owner());
        var wrongEntryResolution = await service.ResolveAsync(_workspaceId, _applicationId, wrongEntry, Owner());

        forgedResolution.Status.Should().Be(SourceOwnershipResolutionStatus.Selected);
        forgedResolution.SelectedBinding!.Id.Should().Be(binding.Id);
        wrongEntryResolution.Status.Should().Be(SourceOwnershipResolutionStatus.ManifestNotTrusted);
    }

    [Fact]
    public async Task Activation_fails_closed_without_owner_provider_repository_and_policy_trust()
    {
        var store = TrustedStore();
        var authority = SeedAuthority(store);
        var service = new SourceOwnershipService(store, Audit(store), TimeProvider(_now));
        var binding = Binding(SourceSelectorKind.Package, "Acme.*", authority: authority);

        var notOwner = await service.ActivateAsync(binding, Owner(isOwner: false));
        store.ProviderConnections.Single().Status = ProviderConnectionStatus.Revoked;
        var revokedProvider = await service.ActivateAsync(binding, Owner());
        store.ProviderConnections.Single().Status = ProviderConnectionStatus.Active;
        binding.RepositoryProviderId = "different-repository";
        var wrongRepository = await service.ActivateAsync(binding, Owner());
        binding.RepositoryProviderId = authority.RepositoryProviderId;
        store.PathPolicies.Clear();
        var missingPolicy = await service.ActivateAsync(binding, Owner());

        notOwner.ReasonCode.Should().Be(HealingOwnershipReasonCodes.OwnerApprovalRequired);
        revokedProvider.ReasonCode.Should().Be(HealingOwnershipReasonCodes.ProviderNotAuthorized);
        wrongRepository.ReasonCode.Should().Be(HealingOwnershipReasonCodes.ProviderRepositoryMismatch);
        missingPolicy.ReasonCode.Should().Be(HealingOwnershipReasonCodes.PolicyNotTrusted);
        binding.Status.Should().Be(SourceOwnershipBindingStatus.Draft);
    }

    [Fact]
    public async Task Activation_blocks_known_selector_overlap_with_a_different_authority()
    {
        var store = TrustedStore();
        var firstAuthority = SeedAuthority(store);
        var secondAuthority = SeedAuthority(store, "repo-2", "alternate-workflows");
        var existing = Binding(SourceSelectorKind.Package, "Acme.Workflows", authority: firstAuthority);
        existing.Status = SourceOwnershipBindingStatus.Active;
        store.Bindings.Add(existing);
        var service = new SourceOwnershipService(store, Audit(store), TimeProvider(_now));
        var candidate = Binding(SourceSelectorKind.Assembly, "Acme.*", authority: secondAuthority);
        TrustBinding(store, candidate);

        var result = await service.ActivateAsync(candidate, Owner());

        result.ReasonCode.Should().Be(HealingOwnershipReasonCodes.AmbiguousAuthority);
        candidate.Status.Should().Be(SourceOwnershipBindingStatus.Draft);
    }

    [Fact]
    public async Task Draft_update_suspend_and_revoke_use_governed_state_transitions()
    {
        var store = TrustedStore();
        var authority = SeedAuthority(store);
        var service = new SourceOwnershipService(store, Audit(store), TimeProvider(_now));
        var binding = Binding(SourceSelectorKind.Package, "Acme.*", authority: authority);
        TrustBinding(store, binding);

        var draft = await service.SaveDraftAsync(binding, Owner(isOwner: false));
        draft.Value!.Status.Should().Be(SourceOwnershipBindingStatus.Draft);
        var active = await service.ActivateAsync(binding, Owner());
        active.Succeeded.Should().BeTrue();
        var suspended = await service.SuspendAsync(_workspaceId, _applicationId, binding.Id, Owner());
        suspended.Value!.Status.Should().Be(SourceOwnershipBindingStatus.Suspended);
        var revoked = await service.RevokeAsync(_workspaceId, _applicationId, binding.Id, Owner());
        revoked.Value!.Status.Should().Be(SourceOwnershipBindingStatus.Revoked);
        var reactivate = await service.ActivateAsync(binding, Owner());

        reactivate.ReasonCode.Should().Be(HealingOwnershipReasonCodes.InvalidConfiguration);
    }

    [Fact]
    public async Task Successful_authority_decisions_emit_only_registered_safe_audit_fields()
    {
        var store = TrustedStore();
        var authority = SeedAuthority(store);
        var service = new SourceOwnershipService(store, Audit(store), TimeProvider(_now));
        var binding = Binding(SourceSelectorKind.Package, "Acme.*", authority: authority);
        TrustBinding(store, binding);

        await service.ActivateAsync(binding, Owner());
        await service.ResolveAsync(_workspaceId, _applicationId, store.Manifests.Single().Entries.Single(), Owner());

        store.AuditEvents.Should().ContainSingle();
        foreach (var auditEvent in store.AuditEvents)
        {
            var details = JsonSerializer.Deserialize<Dictionary<string, string?>>(auditEvent.SafeDetailJson)!;
            details.Keys.Should().OnlyContain(key =>
                key == "status" || key == "gateReason" || key == "repositoryOwner" || key == "repositoryName");
            auditEvent.WorkspaceId.Should().Be(_workspaceId);
        }
    }

    private HealingConfiguration Configuration(Guid? environmentId = null) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = _workspaceId,
        ApplicationId = _applicationId,
        DiscoveryEnabled = true,
        RepairEnabled = true,
        AutomaticMergeEnabled = false,
        SignalProfileVersion = "1.0",
        DefaultAttemptLimit = 2,
        VerificationWindow = TimeSpan.FromHours(2),
        TimeBudget = TimeSpan.FromMinutes(30),
        ConcurrencyBudget = 1,
        InferenceBudget = 10_000,
        RepositoryRunBudget = 10,
        Environments = environmentId is null ? [] :
        [
            new HealingEnvironmentConfiguration
            {
                Id = Guid.NewGuid(), WorkspaceId = _workspaceId, ApplicationId = _applicationId,
                EnvironmentId = environmentId.Value, DiscoveryEnabled = false, OccurrenceThreshold = 7,
                DebounceWindow = TimeSpan.FromMinutes(3)
            }
        ]
    };

    private ComponentManifestEntity Manifest(
        string? repositoryUrl = null,
        Guid? revisionId = null,
        string version = "2.4.1",
        bool includeAssemblies = true)
    {
        var componentKey = $"nuget:Acme.Workflows:{version}";
        var contentHash = $"sha256:{new string(version == "2.4.1" ? '1' : '2', 64)}";
        var document = new HealingComponentManifest(
            "1.0",
            new ComponentManifestApplication("Acme.WorkflowHost", "2.4.1", "net10.0", "linux-x64"),
            new ComponentManifestRevision(new string('a', 40), "https://github.com/acme/workflow-host", "build-1", _now),
        [
            new Elsa.Platform.Healing.ComponentManifest.ComponentManifestEntry(
                componentKey, "package", "Acme.Workflows", version, contentHash, repositoryUrl, null, true,
                includeAssemblies ?
                [
                    new ComponentManifestAssembly("Acme.Workflows", version + ".0", null, "lib/net10.0/Acme.Workflows.dll", contentHash),
                    new ComponentManifestAssembly("Acme.Workflows.Contracts", version + ".0", null, "lib/net10.0/Acme.Workflows.Contracts.dll", contentHash)
                ] : [],
                [])
        ]);
        var canonicalJson = ComponentManifestSerializer.Serialize(document);
        var canonicalDocument = ComponentManifestSerializer.Deserialize(canonicalJson);
        return new ComponentManifestEntity
        {
            Id = Guid.NewGuid(), WorkspaceId = _workspaceId, ApplicationId = _applicationId, RevisionId = revisionId ?? Guid.NewGuid(),
            SchemaVersion = canonicalDocument.SchemaVersion, SourceRevision = canonicalDocument.Revision.SourceRevision,
            ManifestDigest = canonicalDocument.ManifestDigest!, CanonicalJson = canonicalJson,
            TrustState = ComponentManifestTrustState.Unverified, CreatedAt = _now,
            Entries =
            [
                new Elsa.Platform.Healing.Core.ComponentManifestEntry
                {
                    Id = Guid.NewGuid(), WorkspaceId = _workspaceId, ApplicationId = _applicationId,
                    ComponentKey = componentKey, Kind = ComponentKind.Package, KindName = "package", Name = "Acme.Workflows",
                    Version = version, PackageId = "Acme.Workflows", PackageVersion = version,
                    AssemblyName = includeAssemblies ? "Acme.Workflows" : null, ContentHash = contentHash,
                    RelativePath = includeAssemblies ? "lib/net10.0/Acme.Workflows.dll" : null,
                    RepositoryUrl = repositoryUrl, IsDirectDependency = true,
                    Assemblies = includeAssemblies ?
                    [
                        new ComponentManifestAssemblyArtifact
                        {
                            Id = Guid.NewGuid(), Name = "Acme.Workflows", Version = version + ".0",
                            RelativePath = "lib/net10.0/Acme.Workflows.dll", ContentHash = contentHash
                        },
                        new ComponentManifestAssemblyArtifact
                        {
                            Id = Guid.NewGuid(), Name = "Acme.Workflows.Contracts", Version = version + ".0",
                            RelativePath = "lib/net10.0/Acme.Workflows.Contracts.dll", ContentHash = contentHash
                        }
                    ] : []
                }
            ]
        };
    }

    private static ComponentManifestEntity CloneManifest(ComponentManifestEntity source, string? digest = null) => new()
    {
        Id = source.Id, WorkspaceId = source.WorkspaceId, ApplicationId = source.ApplicationId, RevisionId = source.RevisionId,
        SchemaVersion = source.SchemaVersion, SourceRevision = source.SourceRevision, ManifestDigest = digest ?? source.ManifestDigest,
        CanonicalJson = source.CanonicalJson, TrustState = source.TrustState, CreatedAt = source.CreatedAt,
        Entries = source.Entries.Select(x => new Elsa.Platform.Healing.Core.ComponentManifestEntry
        {
            Id = x.Id, WorkspaceId = x.WorkspaceId, ApplicationId = x.ApplicationId, ComponentKey = x.ComponentKey,
            Kind = x.Kind, KindName = x.KindName, Name = x.Name, Version = x.Version, PackageId = x.PackageId, PackageVersion = x.PackageVersion,
            AssemblyName = x.AssemblyName, ContentHash = x.ContentHash, RelativePath = x.RelativePath, RepositoryUrl = x.RepositoryUrl
            , IsDirectDependency = x.IsDirectDependency,
            Assemblies = x.Assemblies.Select(assembly => new ComponentManifestAssemblyArtifact
            {
                Id = assembly.Id, ManifestId = assembly.ManifestId, ComponentEntryId = assembly.ComponentEntryId,
                WorkspaceId = assembly.WorkspaceId, ApplicationId = assembly.ApplicationId, Name = assembly.Name,
                Version = assembly.Version, PublicKeyToken = assembly.PublicKeyToken, RelativePath = assembly.RelativePath,
                ContentHash = assembly.ContentHash
            }).ToList()
        }).ToList()
    };

    private FakeOwnershipStore TrustedStore(string verificationMethod = "platform-managed-build-attestation")
    {
        var store = new FakeOwnershipStore();
        var manifest = Manifest("https://github.com/acme/workflows");
        manifest.TrustState = ComponentManifestTrustState.Verified;
        manifest.VerificationMethod = verificationMethod;
        foreach (var entry in manifest.Entries)
        {
            entry.ManifestId = manifest.Id;
            foreach (var assembly in entry.Assemblies)
            {
                assembly.ManifestId = manifest.Id;
                assembly.ComponentEntryId = entry.Id;
                assembly.WorkspaceId = manifest.WorkspaceId;
                assembly.ApplicationId = manifest.ApplicationId;
            }
        }
        store.Manifests.Add(manifest);
        return store;
    }

    private ProviderConnection SeedAuthority(FakeOwnershipStore store, string repositoryProviderId = "repo-1", string repositoryName = "workflows")
    {
        var connection = new ProviderConnection
        {
            Id = Guid.NewGuid(), WorkspaceId = _workspaceId, Provider = "github", InstallationId = "install-1",
            RepositoryProviderId = repositoryProviderId, RepositoryOwner = "acme", RepositoryName = repositoryName,
            CredentialReference = "secret-ref", Status = ProviderConnectionStatus.Active
        };
        store.ProviderConnections.Add(connection);
        store.PathPolicies.Add(Policy<PathPolicy>());
        store.EvidencePolicies.Add(Policy<EvidencePolicy>());
        store.MergePolicies.Add(Policy<MergePolicy>());
        return connection;
    }

    private T Policy<T>() where T : HealingPolicyDefinition, new() => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = _workspaceId, ApplicationId = _applicationId,
        Name = typeof(T).Name, PolicyVersion = "1", PolicyHash = "sha256:policy", CreatedAt = _now
    };

    private SourceOwnershipBinding Binding(SourceSelectorKind kind, string pattern, int priority = 0, ProviderConnection? authority = null)
    {
        authority ??= new ProviderConnection
        {
            Id = Guid.NewGuid(), WorkspaceId = _workspaceId, RepositoryProviderId = "repo-1",
            RepositoryOwner = "acme", RepositoryName = "workflows"
        };
        return new SourceOwnershipBinding
        {
            Id = Guid.NewGuid(), WorkspaceId = _workspaceId, ApplicationId = _applicationId, Name = Guid.NewGuid().ToString("N"),
            SelectorKind = kind, SelectorPattern = pattern, Priority = priority, ProviderConnectionId = authority.Id,
            RepositoryProviderId = authority.RepositoryProviderId, RepositoryOwner = authority.RepositoryOwner,
            RepositoryName = authority.RepositoryName, TargetBranch = "main", WorkflowIdentity = ".github/workflows/healing.yml",
            WorkflowRevision = new string('b', 40), PathPolicyId = Guid.NewGuid(), EvidencePolicyId = Guid.NewGuid(),
            MergePolicyId = Guid.NewGuid(), Status = SourceOwnershipBindingStatus.Draft, CreatedAt = _now, UpdatedAt = _now
        };
    }

    private static CreateHealingAuthorityProfile AuthorityRequest(bool automaticMergeEnabled) => new(
        "Default authority", "42", "acme", "claims", Guid.NewGuid(),
        ["src", "tests"], [".github"], 20, 1_000, 1_000_000,
        false, true, 0.9m, automaticMergeEnabled, [], null,
        ["workflow", "credentials"], true);

    private static void TrustBinding(FakeOwnershipStore store, SourceOwnershipBinding binding)
    {
        binding.PathPolicyId = store.PathPolicies.Last().Id;
        binding.EvidencePolicyId = store.EvidencePolicies.Last().Id;
        binding.MergePolicyId = store.MergePolicies.Last().Id;
    }

    private HealingAuthorization Owner(
        bool isOwner = true,
        Guid? applicationId = null,
        IReadOnlySet<string>? permissions = null) => new(
            _workspaceId, applicationId ?? _applicationId, "owner", isOwner,
            permissions ?? new[] { HealingPermissions.Configure, HealingPermissions.ConfigureAutoMerge }.ToFrozenSet(StringComparer.Ordinal));

    private static HealingAuditService Audit(FakeOwnershipStore store) => new(store);

    private static TimeProvider TimeProvider(DateTimeOffset value) => new FixedTimeProvider(value);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class FakeAttestationAuthority : IComponentManifestAttestationAuthority
    {
        public ComponentManifestAttestationRequest? Request { get; private set; }
        public ComponentManifestAttestationEvidence? Evidence { get; private set; }
        public ComponentManifestAttestationDecision Decision { get; init; } = new(
            true,
            ManifestTrustMethod.PlatformManagedBuildAttestation,
            HealingActorTypes.Platform,
            "build-attestor",
            HealingOwnershipReasonCodes.Succeeded);

        public ValueTask<ComponentManifestAttestationDecision> VerifyAsync(
            ComponentManifestAttestationRequest request,
            ComponentManifestAttestationEvidence evidence,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            Evidence = evidence;
            return ValueTask.FromResult(Decision);
        }
    }

    private sealed class FakeProviderValidator(string repositoryProviderId) : IProviderConnectionValidator
    {
        public int CallCount { get; private set; }

        public ValueTask<ProviderConnectionValidationResult> ValidateAsync(ProviderConnection connection, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(ProviderConnectionValidationResult.Valid(repositoryProviderId));
        }
    }

    private sealed class FakeOwnershipStore : IHealingOwnershipStore, IHealingAdministrationStore, IHealingAuditStore
    {
        public HealingConfiguration? Configuration { get; private set; }
        public List<ComponentManifestEntity> Manifests { get; } = [];
        public List<SourceOwnershipBinding> Bindings { get; } = [];
        public List<ProviderConnection> ProviderConnections { get; } = [];
        public List<PathPolicy> PathPolicies { get; } = [];
        public List<EvidencePolicy> EvidencePolicies { get; } = [];
        public List<MergePolicy> MergePolicies { get; } = [];
        public List<HealingAuditEvent> AuditEvents { get; } = [];
        private Dictionary<(Guid WorkspaceId, Guid ApplicationId, Guid RevisionId, string IdempotencyKey), (string PayloadHash, Guid ManifestId)> ManifestRegistrations { get; } = [];

        public ValueTask<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, ValueTask<T>> operation, CancellationToken cancellationToken = default) =>
            operation(cancellationToken);

        public ValueTask<HealingConfiguration?> GetConfigurationAsync(Guid workspaceId, Guid applicationId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Configuration is { } value && value.WorkspaceId == workspaceId && value.ApplicationId == applicationId ? value : null);

        public ValueTask<HealingConfiguration> SaveConfigurationAsync(HealingConfiguration configuration, CancellationToken cancellationToken)
        {
            Configuration = configuration;
            return ValueTask.FromResult(configuration);
        }

        public ValueTask<OwnershipWriteResult<ComponentManifestEntity>> AddManifestAsync(ComponentManifestEntity manifest, CancellationToken cancellationToken)
        {
            var existing = Manifests.SingleOrDefault(x => x.WorkspaceId == manifest.WorkspaceId && x.ApplicationId == manifest.ApplicationId && x.RevisionId == manifest.RevisionId);
            if (existing is not null)
                return ValueTask.FromResult(new OwnershipWriteResult<ComponentManifestEntity>(existing, true, existing.ManifestDigest == manifest.ManifestDigest));
            Manifests.Add(manifest);
            return ValueTask.FromResult(new OwnershipWriteResult<ComponentManifestEntity>(manifest, false, true));
        }

        public async ValueTask<ManifestRegistrationWriteResult> RegisterManifestAsync(
            ComponentManifestEntity manifest,
            string idempotencyKey,
            string payloadHash,
            CancellationToken cancellationToken)
        {
            var key = (manifest.WorkspaceId, manifest.ApplicationId, manifest.RevisionId, idempotencyKey);
            if (ManifestRegistrations.TryGetValue(key, out var registration))
            {
                var registeredManifest = Manifests.Single(x => x.Id == registration.ManifestId);
                return new ManifestRegistrationWriteResult(
                    registeredManifest,
                    true,
                    registration.PayloadHash == payloadHash ? null : HealingOwnershipReasonCodes.IdempotencyConflict);
            }
            var persisted = await AddManifestAsync(manifest, cancellationToken);
            if (persisted.IsConsistentReplay)
                ManifestRegistrations[key] = (payloadHash, persisted.Value.Id);
            return new ManifestRegistrationWriteResult(
                persisted.Value,
                persisted.IsReplay,
                persisted.IsConsistentReplay ? null : HealingOwnershipReasonCodes.ImmutableRevisionConflict);
        }

        public ValueTask<ComponentManifestEntity?> GetManifestAsync(Guid workspaceId, Guid applicationId, Guid manifestId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Manifests.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.Id == manifestId));

        public ValueTask<IReadOnlyList<ComponentManifestEntity>> ListManifestsAsync(Guid workspaceId, Guid applicationId, bool trustedOnly, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ComponentManifestEntity>>(Manifests.Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && (!trustedOnly || x.TrustState == ComponentManifestTrustState.Verified)).ToList());

        public ValueTask<bool> TransitionManifestTrustAsync(Guid workspaceId, Guid applicationId, Guid manifestId, ComponentManifestTrustState expected, ComponentManifestTrustState target, string actorId, string method, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var manifest = Manifests.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.Id == manifestId && x.TrustState == expected);
            if (manifest is null) return ValueTask.FromResult(false);
            manifest.TrustState = target;
            manifest.VerifiedBy = target == ComponentManifestTrustState.Verified ? actorId : manifest.VerifiedBy;
            manifest.VerifiedAt = target == ComponentManifestTrustState.Verified ? now : manifest.VerifiedAt;
            manifest.VerificationMethod = target == ComponentManifestTrustState.Verified ? method : manifest.VerificationMethod;
            return ValueTask.FromResult(true);
        }

        public ValueTask<IReadOnlyList<SourceOwnershipBinding>> ListBindingsAsync(Guid workspaceId, Guid applicationId, bool activeOnly, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<SourceOwnershipBinding>>(Bindings.Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && (!activeOnly || x.Status == SourceOwnershipBindingStatus.Active)).ToList());

        public ValueTask<SourceOwnershipBinding?> GetBindingAsync(Guid workspaceId, Guid applicationId, Guid bindingId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Bindings.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.Id == bindingId));

        public ValueTask<ProviderConnection?> GetProviderConnectionAsync(Guid workspaceId, Guid providerConnectionId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ProviderConnections.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.Id == providerConnectionId));

        public ValueTask<IReadOnlyList<ProviderConnection>> ListProviderConnectionsAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ProviderConnection>>(ProviderConnections.Where(x => x.WorkspaceId == workspaceId).ToList());

        public ValueTask<IReadOnlyList<PathPolicy>> ListPathPoliciesAsync(Guid workspaceId, Guid applicationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<PathPolicy>>(PathPolicies.Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId).ToList());

        public ValueTask<IReadOnlyList<EvidencePolicy>> ListEvidencePoliciesAsync(Guid workspaceId, Guid applicationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<EvidencePolicy>>(EvidencePolicies.Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId).ToList());

        public ValueTask<IReadOnlyList<MergePolicy>> ListMergePoliciesAsync(Guid workspaceId, Guid applicationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<MergePolicy>>(MergePolicies.Where(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId).ToList());

        public ValueTask<ProviderConnection> SaveProviderConnectionAsync(ProviderConnection connection, CancellationToken cancellationToken = default)
        {
            var index = ProviderConnections.FindIndex(x => x.Id == connection.Id);
            if (index < 0) ProviderConnections.Add(connection); else ProviderConnections[index] = connection;
            return ValueTask.FromResult(connection);
        }

        public ValueTask SavePoliciesAsync(PathPolicy pathPolicy, EvidencePolicy evidencePolicy, MergePolicy mergePolicy, CancellationToken cancellationToken = default)
        {
            PathPolicies.Add(pathPolicy);
            EvidencePolicies.Add(evidencePolicy);
            MergePolicies.Add(mergePolicy);
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> PoliciesAreTrustedAsync(Guid workspaceId, Guid applicationId, Guid pathPolicyId, Guid evidencePolicyId, Guid mergePolicyId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(PathPolicies.Any(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.Id == pathPolicyId) &&
                                 EvidencePolicies.Any(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.Id == evidencePolicyId) &&
                                 MergePolicies.Any(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.Id == mergePolicyId));

        public ValueTask<SourceOwnershipBinding> SaveBindingAsync(SourceOwnershipBinding binding, CancellationToken cancellationToken)
        {
            var index = Bindings.FindIndex(x => x.Id == binding.Id);
            if (index < 0) Bindings.Add(binding); else Bindings[index] = binding;
            return ValueTask.FromResult(binding);
        }

        public ValueTask<HealingAuditEvent> AppendAsync(HealingAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            auditEvent.Sequence = AuditEvents.Count + 1;
            AuditEvents.Add(auditEvent);
            return ValueTask.FromResult(auditEvent);
        }

        public ValueTask<IReadOnlyList<HealingAuditEvent>> QueryAsync(Elsa.Platform.Healing.Core.Security.HealingAuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<HealingAuditEvent>>(AuditEvents.Where(x => x.WorkspaceId == query.WorkspaceId).ToList());
    }
}
