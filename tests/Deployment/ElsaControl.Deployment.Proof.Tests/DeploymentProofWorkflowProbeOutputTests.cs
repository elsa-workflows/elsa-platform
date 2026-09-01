using System.Text.Json;
using ElsaControl.Deployment.Proof;

namespace ElsaControl.Deployment.Proof.Tests;

public sealed class DeploymentProofWorkflowProbeOutputTests
{
    [Fact]
    public void Failure_preserves_stable_code_and_redacts_message_values()
    {
        var json = DeploymentProofWorkflowProbeOutput.Failure(
            "azure.proof.workflow.absenceCheckFailed",
            "token=do-not-leak");
        using var document = JsonDocument.Parse(json);

        Assert.Equal("failed", document.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("azure.proof.workflow.absenceCheckFailed", document.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("do-not-leak", json, StringComparison.Ordinal);
        Assert.Contains("<redacted>", document.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
    }
}
