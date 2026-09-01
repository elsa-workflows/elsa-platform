using System.Text.Json;

namespace ElsaControl.Deployment.Proof;

internal static class DeploymentProofWorkflowProbeOutput
{
    public static string Failure(string code, string message) =>
        JsonSerializer.Serialize(new
        {
            outcome = "failed",
            code = DeploymentProofEvidence.SanitizeMessage(code),
            message = DeploymentProofEvidence.SanitizeMessage(message)
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
