using ValenceControl.Healing.Abstractions;

namespace ValenceControl.Healing.Abstractions.Tests;

public class HealingBoundaryTests
{
    [Fact]
    public void PublishedContractVersionsRemainAtV1()
    {
        Assert.Equivalent(
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
            },
            HealingContractVersions.All);
    }

    [Fact]
    public void AbstractionsAssemblyHasNoProviderOrInfrastructureDependency()
    {
        var referenceNames = typeof(HealingSignal).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(referenceNames, name =>
            new[] { "GitHub", "Octokit", "AspNetCore", "EntityFrameworkCore" }
                .Any(forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)));

        Assert.DoesNotContain(typeof(HealingSignal).Assembly.GetExportedTypes().Select(type => type.FullName ?? string.Empty), name =>
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
        Assert.Equal(expected, HealingContractVersion.IsCompatible(HealingContractVersions.SignalProfile, candidate));
    }

    [Fact]
    public void SignalContractCannotCarryRepositoryMutationAuthority()
    {
        var propertyNames = typeof(HealingSignal).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain(propertyNames, name =>
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

        Assert.DoesNotContain(propertyNames, name =>
            new[] { "Secret", "AccessToken", "RefreshToken", "PrivateKey", "Credential", "ConnectionString" }
                .Any(forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void WorkflowDispatchContractContainsNoEvidenceOrCredentials()
    {
        var propertyNames = typeof(RepairWorkflowDispatchRequest).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain(propertyNames, name =>
            new[] { "Exception", "Stack", "Evidence", "Diff", "Patch", "Token", "Secret", "Prompt" }
                .Any(forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void WorkloadCapabilityIsLimitedToTheFiveProtocolOperations()
    {
        Assert.Equivalent(
            new[]
            {
                "evidence.read",
                "proposal.create",
                "proposal.finalize",
                "attempt.heartbeat",
                "result.upload"
            },
            WorkloadCapabilityScopes.All);

        var propertyNames = typeof(WorkloadCapabilityGrant).GetProperties().Select(property => property.Name);
        Assert.All(new[] { "ProtocolVersion", "AttemptId", "AllowedScopes", "ExpiresAt" }, propertyName => Assert.Contains(propertyName, propertyNames));

        Assert.IsNotAssignableFrom<HashSet<string>>(WorkloadCapabilityScopes.All);
        Assert.IsNotAssignableFrom<HashSet<string>>(HealingPermissions.All);
        Assert.IsNotAssignableFrom<HashSet<string>>(HealingActorTypes.All);
        Assert.IsNotAssignableFrom<HashSet<string>>(HealingHumanCommands.All);
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

        Assert.DoesNotContain(propertyNames, name =>
            new[] { "InstallationToken", "ProviderToken", "RepositoryCredential", "MergePermission" }
                .Any(forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void DeploymentAndAuditWireContractsAreExplicitlyVersioned()
    {
        Assert.DoesNotContain(
            new[]
            {
                typeof(DeploymentObservationRequest),
                typeof(RepairVerificationFailedSignal),
                typeof(HealingAuditEventContract),
                typeof(HealingAuditQuery)
            },
            type => type.GetProperty("ProtocolVersion") is null);
    }

    [Fact]
    public void AuditPortIsAppendOnly()
    {
        Assert.Equivalent(new[] { "AppendAsync" }, typeof(IHealingAuditSink).GetMethods().Select(method => method.Name));

        Assert.Equivalent(new[] { "ListAsync" }, typeof(IHealingAuditQuery).GetMethods().Select(method => method.Name));
    }

    [Fact]
    public void PolicyEvaluationCarriesEveryGateState()
    {
        Assert.Equivalent(new[] { "Pass", "Block", "Unknown", "Stale" }, Enum.GetNames<PolicyGateState>());

        Assert.NotNull(typeof(PolicyEvaluationSnapshot).GetProperty("Gates"));
    }
}
