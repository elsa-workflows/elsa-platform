using ValenceControl.Healing.Core;

namespace ValenceControl.Healing.Core.Tests;

public sealed class HealingModelTests
{
    [Fact]
    public void IncidentTransitionRejectsSkippingRequiredRepairStates()
    {
        var incident = new HealingIncident { Status = HealingIncidentStatus.Observed };

        var result = incident.TryTransitionTo(HealingIncidentStatus.PullRequestOpen);

        Assert.False(result.Succeeded);
        Assert.Equal(HealingIncidentStatus.Observed, result.From);
        Assert.Equal(HealingIncidentStatus.PullRequestOpen, result.To);
        Assert.Equal(HealingTransitionReasonCodes.InvalidIncidentTransition, result.ReasonCode);
        Assert.Equal(HealingIncidentStatus.Observed, incident.Status);
    }

    [Theory]
    [InlineData(HealingIncidentStatus.Superseded)]
    [InlineData(HealingIncidentStatus.Waived)]
    public void SuppressedIncidentCanReachAnExplicitTerminalState(HealingIncidentStatus target)
    {
        var incident = new HealingIncident { Status = HealingIncidentStatus.Suppressed };

        var result = incident.TryTransitionTo(target);

        Assert.True(result.Succeeded);
        Assert.Equal(target, incident.Status);
    }
}
