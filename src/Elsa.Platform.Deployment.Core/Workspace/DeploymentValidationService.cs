using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class DeploymentValidationService
{
    public PromotionComparison PreviewPromotion(WorkspacePromotionPreviewRequest request)
    {
        return new PromotionComparison(
            request.SourceEnvironmentId.ToString("D"),
            request.TargetEnvironmentId.ToString("D"),
            0,
            0,
            [],
            [new DeploymentValidation("deployment.preview.not-implemented", ValidationSeverity.Blocker, "Deployment preview", "Promotion preview is not implemented yet.")],
            null);
    }
}
