using ValenceControl.Deployment.Abstractions.Diagnostics;
using ValenceControl.Deployment.Abstractions.Resources;
using FluentAssertions;

namespace ValenceControl.Deployment.Abstractions.Tests;

public class DiagnosticContractTests
{
    private readonly DeploymentResourceId _workflow = new("workflowDefinition", "order-approval");

    [Fact]
    public void DiagnosticCapturesMachineCodeSeverityMessageAndResource()
    {
        var diagnostic = new DeploymentDiagnostic(
            "workflow.invalid",
            DeploymentDiagnosticSeverity.Error,
            "Workflow definition is invalid.",
            _workflow,
            new Dictionary<string, string> { ["path"] = "workflows/order-approval.json" });

        diagnostic.Code.Should().Be("workflow.invalid");
        diagnostic.Severity.Should().Be(DeploymentDiagnosticSeverity.Error);
        diagnostic.Message.Should().Be("Workflow definition is invalid.");
        diagnostic.ResourceId.Should().Be(_workflow);
        diagnostic.Details.Should().ContainKey("path");
    }

    [Theory]
    [InlineData("", "message")]
    [InlineData("code", " ")]
    public void DiagnosticRejectsEmptyCodeOrMessage(string code, string message)
    {
        var act = () => new DeploymentDiagnostic(code, DeploymentDiagnosticSeverity.Error, message);

        act.Should().Throw<ArgumentException>();
    }
}
