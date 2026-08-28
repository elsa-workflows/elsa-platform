using ElsaControl.Deployment.Abstractions.Diagnostics;
using ElsaControl.Deployment.Abstractions.Resources;

namespace ElsaControl.Deployment.Abstractions.Tests;

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

        Assert.Equal("workflow.invalid", diagnostic.Code);
        Assert.Equal(DeploymentDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Workflow definition is invalid.", diagnostic.Message);
        Assert.Equal(_workflow, diagnostic.ResourceId);
        Assert.Contains("path", diagnostic.Details);
    }

    [Theory]
    [InlineData("", "message")]
    [InlineData("code", " ")]
    public void DiagnosticRejectsEmptyCodeOrMessage(string code, string message)
    {
        var act = () => new DeploymentDiagnostic(code, DeploymentDiagnosticSeverity.Error, message);

        Assert.Throws<ArgumentException>(act);
    }
}
