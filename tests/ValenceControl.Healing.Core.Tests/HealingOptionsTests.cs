using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Configuration;

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

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Matches(".*MaxRepairAttempts.*2.*", exception.Message);
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

        Assert.Equal(new HealingGateResult(false, HealingGateReasonCodes.ControlKillSwitch), result);
    }

    [Fact]
    public void WorkspaceKillSwitchBlocksRepairForAnEnabledApplication()
    {
        var guard = new HealingKillSwitch(new HealingOptions { RepairDispatchEnabled = true });
        var workspace = new HealingWorkspaceConfiguration { WorkspaceKillSwitch = true };
        var application = new HealingConfiguration { RepairEnabled = true };

        var result = guard.CanDispatchRepair(workspace, application);

        Assert.Equal(new HealingGateResult(false, HealingGateReasonCodes.WorkspaceKillSwitch), result);
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
        Assert.Equal(new HealingGateResult(true, HealingGateReasonCodes.Allowed), result);
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

        Assert.True(guard.CanDiscover(new HealingWorkspaceConfiguration(), new HealingConfiguration { DiscoveryEnabled = true }).Allowed);
        Assert.Equal(HealingGateResult.Block(HealingGateReasonCodes.StageDisabled), guard.CanReviewIncidents());
        Assert.True(guard.CanVerify().Allowed);
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

        Assert.True(guard.CanReviewIncidents().Allowed);
        Assert.True(guard.CanDispatchRepair(
            new HealingWorkspaceConfiguration(),
            new HealingConfiguration { RepairEnabled = true }).Allowed);
        Assert.Equal(HealingGateResult.Block(HealingGateReasonCodes.StageDisabled), guard.CanVerify());
    }

    [Fact]
    public void ValidationRejectsAnIdleDelayThatCouldCreateAHotLoop()
    {
        var options = new HealingOptions { IdleDelay = TimeSpan.Zero };

        var act = options.Validate;

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Matches(".*IdleDelay.*100 milliseconds.*", exception.Message);
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

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Matches(".*LeaseSafetyMargin.*less than LeaseDuration.*", exception.Message);
    }
}
