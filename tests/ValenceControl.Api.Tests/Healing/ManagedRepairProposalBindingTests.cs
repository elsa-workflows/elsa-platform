using ValenceControl.Api.Healing;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Agent;

namespace ValenceControl.Api.Tests.Healing;

public sealed class ManagedRepairProposalBindingTests
{
    [Fact]
    public void Exact_proposal_allows_repository_validation_fields_only()
    {
        var proposal = Proposal();
        var result = Result(proposal) with
        {
            Reproduction = new(true, true, "reproduced", "The failure reproduced.", ["dotnet test"]),
            Classification = RepairAgentClassifications.Reproduced,
            Regression = new(true, "A regression test was added.", ["tests/BrokenTests.cs"]),
            Validation = [new("repository", ".elsa/healing/validate", "passed", "Passed.", TimeSpan.FromSeconds(4))],
            Usage = proposal.Usage with { RepositoryRunDuration = TimeSpan.FromSeconds(4), RepositoryRuns = 1 }
        };

        Assert.True(ControlHealingWorkloadApi.MatchesProposal(result, proposal));
    }

    [Theory]
    [InlineData("patch")]
    [InlineData("summary")]
    [InlineData("confidence")]
    [InlineData("usage")]
    [InlineData("classification")]
    public void Repository_result_cannot_replace_managed_inference_fields(string mutation)
    {
        var proposal = Proposal();
        var result = Result(proposal);
        result = mutation switch
        {
            "patch" => result with
            {
                UnifiedDiff = result.UnifiedDiff + "# injected",
                PatchDigest = RepairAgentGateway.ComputeSha256Digest(result.UnifiedDiff + "# injected")
            },
            "summary" => result with { CausalSummary = "A repository-controlled replacement." },
            "confidence" => result with { Confidence = 1m },
            "usage" => result with { Usage = result.Usage with { InputUnits = result.Usage.InputUnits + 1 } },
            "classification" => result with { Classification = RepairAgentClassifications.RevisionUnverified },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        Assert.False(ControlHealingWorkloadApi.MatchesProposal(result, proposal));
    }

    private static ControlHealingWorkloadApi.StoredManagedProposal Proposal()
    {
        const string patch = "diff --git a/src/A.cs b/src/A.cs\n";
        return new(
            new string('a', 40),
            new string('b', 40),
            RepairAgentClassifications.InferredHighConfidence,
            0.91m,
            "The request binder rejects an inaccessible DTO.",
            patch,
            RepairAgentGateway.ComputeSha256Digest(patch),
            [new("src/A.cs", "modified", "application-code")],
            ["Review request compatibility."],
            "Revert the generated commit.",
            new(120, 40, TimeSpan.FromSeconds(3), TimeSpan.Zero));
    }

    private static RepairResultEnvelope Result(ControlHealingWorkloadApi.StoredManagedProposal proposal)
    {
        var now = DateTimeOffset.Parse("2026-07-16T16:00:00Z");
        return new(
            HealingContractVersions.AgentProtocol,
            Guid.NewGuid(),
            "12345",
            1,
            proposal.BaseRevision,
            proposal.TargetRevision,
            proposal.Classification,
            proposal.Confidence,
            proposal.CausalSummary,
            proposal.UnifiedDiff,
            proposal.PatchDigest,
            proposal.ChangedPaths,
            new(false, false, "not-attempted", "Not reproduced.", []),
            new(false, "No regression test claim.", []),
            [],
            proposal.RiskSuggestions,
            proposal.RollbackSummary,
            proposal.Usage,
            new(now, now),
            now,
            Guid.NewGuid(),
            RepairAgentGateway.ComputeSha256Digest("proposal"));
    }
}
