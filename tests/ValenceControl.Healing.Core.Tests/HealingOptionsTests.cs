using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Configuration;
using FluentAssertions;

namespace ValenceControl.Healing.Core.Tests;

public sealed class HealingOptionsTests
{
    [Fact]
    public void ValidationRejectsRepairAttemptBudgetsAboveTheSafetyMaximum()
    {
        var options = new HealingOptions
        {
            Budgets = new HealingBudgetOptions { MaxRepairAttempts = 3 }
        };

        var act = options.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxRepairAttempts*2*");
    }

    [Fact]
    public void ControlKillSwitchBlocksRepairEvenWhenApplicationAndStageAreEnabled()
    {
        var guard = new HealingKillSwitch(new HealingOptions
        {
            ControlKillSwitch = true,
            RepairDispatchEnabled = true
        });
        var application = new HealingConfiguration { RepairEnabled = true };

        var result = guard.CanDispatchRepair(new HealingWorkspaceConfiguration(), application);

        result.Should().Be(new HealingGateResult(false, HealingGateReasonCodes.ControlKillSwitch));
    }

    [Fact]
    public void WorkspaceKillSwitchBlocksRepairForAnEnabledApplication()
    {
        var guard = new HealingKillSwitch(new HealingOptions { RepairDispatchEnabled = true });
        var workspace = new HealingWorkspaceConfiguration { WorkspaceKillSwitch = true };
        var application = new HealingConfiguration { RepairEnabled = true };

        var result = guard.CanDispatchRepair(workspace, application);

        result.Should().Be(new HealingGateResult(false, HealingGateReasonCodes.WorkspaceKillSwitch));
    }

    [Fact]
    public void AutomaticMergeCanRemainEnabledWhileNewRepairDispatchIsDisabled()
    {
        var options = new HealingOptions
        {
            RepairDispatchEnabled = false,
            AutomaticMergeEnabled = true
        };
        var guard = new HealingKillSwitch(options);
        var application = new HealingConfiguration
        {
            RepairEnabled = false,
            AutomaticMergeEnabled = true
        };

        var result = guard.CanAutomaticallyMerge(new HealingWorkspaceConfiguration(), application);

        options.Validate();
        result.Should().Be(new HealingGateResult(true, HealingGateReasonCodes.Allowed));
    }

    [Fact]
    public void IncidentReviewCanRemainDisabledWhileDiscoveryAndVerificationAreEnabled()
    {
        var options = new HealingOptions
        {
            DiscoveryEnabled = true,
            IncidentReviewEnabled = false,
            VerificationEnabled = true
        };
        var guard = new HealingKillSwitch(options);

        guard.CanDiscover(new HealingWorkspaceConfiguration(), new HealingConfiguration { DiscoveryEnabled = true })
            .Allowed.Should().BeTrue();
        guard.CanReviewIncidents().Should().Be(
            HealingGateResult.Block(HealingGateReasonCodes.StageDisabled));
        guard.CanVerify().Allowed.Should().BeTrue();
    }

    [Fact]
    public void VerificationCanRemainDisabledWhileIncidentReviewAndRepairDispatchAreEnabled()
    {
        var options = new HealingOptions
        {
            IncidentReviewEnabled = true,
            RepairDispatchEnabled = true,
            VerificationEnabled = false
        };
        var guard = new HealingKillSwitch(options);

        guard.CanReviewIncidents().Allowed.Should().BeTrue();
        guard.CanDispatchRepair(
                new HealingWorkspaceConfiguration(),
                new HealingConfiguration { RepairEnabled = true })
            .Allowed.Should().BeTrue();
        guard.CanVerify().Should().Be(
            HealingGateResult.Block(HealingGateReasonCodes.StageDisabled));
    }

    [Fact]
    public void ValidationRejectsAnIdleDelayThatCouldCreateAHotLoop()
    {
        var options = new HealingOptions { IdleDelay = TimeSpan.Zero };

        var act = options.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IdleDelay*100 milliseconds*");
    }

    [Fact]
    public void ValidationKeepsTheHandlerDeadlineInsideTheLease()
    {
        var options = new HealingOptions
        {
            LeaseDuration = TimeSpan.FromSeconds(10),
            LeaseSafetyMargin = TimeSpan.FromSeconds(10)
        };

        var act = options.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*LeaseSafetyMargin*less than LeaseDuration*");
    }
}
