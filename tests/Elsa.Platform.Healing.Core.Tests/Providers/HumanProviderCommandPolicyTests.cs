using System.Collections.Frozen;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core.Providers;
using FluentAssertions;

namespace Elsa.Platform.Healing.Core.Tests.Providers;

public sealed class HumanProviderCommandPolicyTests
{
    [Theory]
    [InlineData(HealingHumanCommands.Retry, HealingPermissions.RetryRepair)]
    [InlineData(HealingHumanCommands.Stop, HealingPermissions.StopRepair)]
    [InlineData(HealingHumanCommands.RequestEvidence, HealingPermissions.ElevateEvidence)]
    [InlineData(HealingHumanCommands.WaiveEnvironment, HealingPermissions.WaiveVerification)]
    public void Requires_both_provider_and_linked_workspace_authority(string command, string permission)
    {
        var context = Context(command);
        HumanProviderCommandPolicy.Evaluate(context, Authorization(provider: false, linked: true, permission))
            .ReasonCode.Should().Be("provider-permission-denied");
        HumanProviderCommandPolicy.Evaluate(context, Authorization(provider: true, linked: false, permission))
            .ReasonCode.Should().Be("platform-identity-link-missing");
        HumanProviderCommandPolicy.Evaluate(context, Authorization(provider: true, linked: true, "different"))
            .ReasonCode.Should().Be("workspace-permission-denied");
    }

    [Theory]
    [InlineData("read")]
    [InlineData("triage")]
    [InlineData("none")]
    public void Rejects_non_maintainer_repository_roles(string providerPermission)
    {
        var decision = HumanProviderCommandPolicy.Evaluate(Context(HealingHumanCommands.Retry),
            Authorization(true, true, HealingPermissions.RetryRepair) with { ProviderPermission = providerPermission });

        decision.ReasonCode.Should().Be("provider-permission-denied");
    }

    [Fact]
    public void Retry_cannot_exceed_the_platform_attempt_cap()
    {
        var decision = HumanProviderCommandPolicy.Evaluate(Context(HealingHumanCommands.Retry, attempts: 2, maximum: 2),
            Authorization(true, true, HealingPermissions.RetryRepair));

        decision.Should().Match<HumanProviderCommandDecision>(x => !x.Authorized && x.ReasonCode == "maximum-attempts-reached");
    }

    [Theory]
    [InlineData(HealingHumanCommands.Stop, HealingPermissions.StopRepair)]
    [InlineData(HealingHumanCommands.WaiveEnvironment, HealingPermissions.WaiveVerification)]
    public void Destructive_commands_remain_authorized_but_unexecuted_without_confirmation(string command, string permission)
    {
        var decision = HumanProviderCommandPolicy.Evaluate(Context(command, hasTarget: true), Authorization(true, true, permission));

        decision.Should().Match<HumanProviderCommandDecision>(x => x.Authorized && !x.Executed &&
            x.Status == HumanCommandStatus.Authorized && x.ReasonCode == "confirmation-required");
    }

    [Fact]
    public void Fully_authorized_retry_executes()
    {
        var decision = HumanProviderCommandPolicy.Evaluate(Context(HealingHumanCommands.Retry),
            Authorization(true, true, HealingPermissions.RetryRepair));

        decision.Should().Match<HumanProviderCommandDecision>(x => x.Authorized && x.Executed && x.Status == HumanCommandStatus.Executed);
    }

    [Fact]
    public void Stop_is_rejected_after_the_repair_has_moved_to_verification()
    {
        var context = Context(HealingHumanCommands.Stop) with { IncidentStatus = HealingIncidentStatus.Verifying };
        var decision = HumanProviderCommandPolicy.Evaluate(context,
            Authorization(true, true, HealingPermissions.StopRepair) with
            {
                ConfirmationId = Guid.NewGuid(), ConfirmationValid = true
            });

        decision.ReasonCode.Should().Be("stop-not-applicable");
    }

    [Fact]
    public void Provider_waiver_request_never_executes_without_platform_collected_details()
    {
        var context = Context(HealingHumanCommands.WaiveEnvironment, hasTarget: true);
        var decision = HumanProviderCommandPolicy.Evaluate(context,
            Authorization(true, true, HealingPermissions.WaiveVerification) with
            {
                ConfirmationId = Guid.NewGuid(), ConfirmationValid = true
            });

        decision.Should().Match<HumanProviderCommandDecision>(x => x.Authorized && !x.Executed &&
            x.ReasonCode == "environment-waiver-details-required");
    }

    private static HumanProviderCommandContext Context(string command, int attempts = 0, int maximum = 2, bool hasTarget = false) =>
        new(new HumanCommand { Id = Guid.NewGuid(), Command = command }, attempts, maximum, HealingIncidentStatus.NeedsHuman, hasTarget);

    private static HumanProviderCommandAuthorization Authorization(bool provider, bool linked, string permission) =>
        new(provider, "write", linked ? Guid.NewGuid() : null,
            new[] { permission }.ToFrozenSet(StringComparer.Ordinal));
}
