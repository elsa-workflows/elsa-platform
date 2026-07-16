using Elsa.Platform.Healing.Abstractions;
using FluentAssertions;

namespace Elsa.Platform.Healing.Abstractions.Tests;

public class HealingBoundaryTests
{
    [Fact]
    public void PublishedContractVersionsRemainAtV1()
    {
        HealingContractVersions.All.Should().BeEquivalentTo(
            new Dictionary<string, string>
            {
                ["signal-profile"] = "1.0",
                ["component-manifest"] = "1.0",
                ["provider-protocol"] = "1.0",
                ["agent-protocol"] = "1.0",
                ["workload-protocol"] = "1.0",
                ["policy-protocol"] = "1.0",
                ["deployment-protocol"] = "1.0",
                ["audit-protocol"] = "1.0"
            });
    }

    [Fact]
    public void AbstractionsAssemblyHasNoProviderOrInfrastructureDependency()
    {
        var referenceNames = typeof(HealingSignal).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        referenceNames.Should().NotContain(name =>
            new[] { "GitHub", "Octokit", "AspNetCore", "EntityFrameworkCore" }
                .Any(forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)));

        typeof(HealingSignal).Assembly.GetExportedTypes().Select(type => type.FullName ?? string.Empty)
            .Should().NotContain(name =>
                name.Contains("GitHub", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Octokit", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("1.0", true)]
    [InlineData("1.99", true)]
    [InlineData("2.0", false)]
    [InlineData("0.9", false)]
    [InlineData("1", false)]
    [InlineData("1.0.0", false)]
    public void SignalProfileCompatibilityAcceptsSupportedMinorVersionsOnly(string candidate, bool expected)
    {
        HealingContractVersion.IsCompatible(HealingContractVersions.SignalProfile, candidate).Should().Be(expected);
    }

    [Fact]
    public void SignalContractCannotCarryRepositoryMutationAuthority()
    {
        var propertyNames = typeof(HealingSignal).GetProperties().Select(property => property.Name).ToArray();

        propertyNames.Should().NotContain(name =>
            new[] { "Repository", "WorkflowIdentity", "TargetBranch", "ProviderConnection", "MergePolicy" }
                .Any(forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ComponentManifestContractCannotCarrySecretsOrProviderCredentials()
    {
        var propertyNames = new[] { typeof(ComponentManifestDocument), typeof(ComponentManifestEntry) }
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToArray();

        propertyNames.Should().NotContain(name =>
            new[] { "Secret", "AccessToken", "RefreshToken", "PrivateKey", "Credential", "ConnectionString" }
                .Any(forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void WorkflowDispatchContractContainsNoEvidenceOrCredentials()
    {
        var propertyNames = typeof(RepairWorkflowDispatchRequest).GetProperties().Select(property => property.Name).ToArray();

        propertyNames.Should().NotContain(name =>
            new[] { "Exception", "Stack", "Evidence", "Diff", "Patch", "Token", "Secret", "Prompt" }
                .Any(forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void WorkloadCapabilityIsLimitedToTheThreeProtocolOperations()
    {
        WorkloadCapabilityScopes.All.Should().BeEquivalentTo(
            new[]
            {
                "evidence.read",
                "attempt.heartbeat",
                "result.upload"
            });

        typeof(WorkloadCapabilityGrant).GetProperties().Select(property => property.Name)
            .Should().Contain(new[] { "ProtocolVersion", "AttemptId", "AllowedScopes", "ExpiresAt" });

        WorkloadCapabilityScopes.All.Should().NotBeAssignableTo<HashSet<string>>();
        HealingPermissions.All.Should().NotBeAssignableTo<HashSet<string>>();
        HealingActorTypes.All.Should().NotBeAssignableTo<HashSet<string>>();
        HealingHumanCommands.All.Should().NotBeAssignableTo<HashSet<string>>();
    }

    [Fact]
    public void AgentAndWorkloadInputsCannotCarryProviderMutationCredentials()
    {
        var propertyNames = new[]
            {
                typeof(RepairAgentRequest),
                typeof(RepairEvidenceBundle),
                typeof(WorkloadIdentityExchangeRequest),
                typeof(WorkloadHeartbeatRequest),
                typeof(WorkloadResultUploadRequest)
            }
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToArray();

        propertyNames.Should().NotContain(name =>
            new[] { "InstallationToken", "ProviderToken", "RepositoryCredential", "MergePermission" }
                .Any(forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void DeploymentAndAuditWireContractsAreExplicitlyVersioned()
    {
        new[]
            {
                typeof(DeploymentObservationRequest),
                typeof(RepairVerificationFailedSignal),
                typeof(HealingAuditEventContract),
                typeof(HealingAuditQuery)
            }
            .Where(type => type.GetProperty("ProtocolVersion") is null)
            .Should().BeEmpty();
    }

    [Fact]
    public void AuditPortIsAppendOnly()
    {
        typeof(IHealingAuditSink).GetMethods().Select(method => method.Name)
            .Should().BeEquivalentTo(new[] { "AppendAsync" });

        typeof(IHealingAuditQuery).GetMethods().Select(method => method.Name)
            .Should().BeEquivalentTo(new[] { "ListAsync" });
    }

    [Fact]
    public void PolicyEvaluationCarriesEveryGateState()
    {
        Enum.GetNames<PolicyGateState>().Should().BeEquivalentTo(
            new[] { "Pass", "Block", "Unknown", "Stale" });

        typeof(PolicyEvaluationSnapshot).GetProperty("Gates").Should().NotBeNull();
    }
}
