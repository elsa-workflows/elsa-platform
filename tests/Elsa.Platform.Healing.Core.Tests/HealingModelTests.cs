using Elsa.Platform.Healing.Core;
using FluentAssertions;

namespace Elsa.Platform.Healing.Core.Tests;

public sealed class HealingModelTests
{
    [Fact]
    public void IncidentTransitionRejectsSkippingRequiredRepairStates()
    {
        var incident = new HealingIncident { Status = HealingIncidentStatus.Observed };

        var result = incident.TryTransitionTo(HealingIncidentStatus.PullRequestOpen);

        result.Succeeded.Should().BeFalse();
        result.From.Should().Be(HealingIncidentStatus.Observed);
        result.To.Should().Be(HealingIncidentStatus.PullRequestOpen);
        result.ReasonCode.Should().Be(HealingTransitionReasonCodes.InvalidIncidentTransition);
        incident.Status.Should().Be(HealingIncidentStatus.Observed);
    }

    [Theory]
    [InlineData(HealingIncidentStatus.Superseded)]
    [InlineData(HealingIncidentStatus.Waived)]
    public void SuppressedIncidentCanReachAnExplicitTerminalState(HealingIncidentStatus target)
    {
        var incident = new HealingIncident { Status = HealingIncidentStatus.Suppressed };

        var result = incident.TryTransitionTo(target);

        result.Succeeded.Should().BeTrue();
        incident.Status.Should().Be(target);
    }
}
