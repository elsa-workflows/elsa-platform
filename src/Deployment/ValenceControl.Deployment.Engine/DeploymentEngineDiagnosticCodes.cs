namespace ValenceControl.Deployment.Engine;

public static class DeploymentEngineDiagnosticCodes
{
    public const string HandlerMissing = "deployment.engine.handler.missing";
    public const string HandlerDuplicate = "deployment.engine.handler.duplicate";
    public const string ResourceDuplicate = "deployment.engine.resource.duplicate";
    public const string ArtifactInvalid = "deployment.engine.artifact.invalid";
    public const string PlanInvalid = "deployment.engine.plan.invalid";
    public const string ValidateFailed = "deployment.engine.validate.failed";
    public const string ReadFailed = "deployment.engine.read.failed";
    public const string DiffFailed = "deployment.engine.diff.failed";
    public const string DryRunFailed = "deployment.engine.dry-run.failed";
    public const string ApplyFailed = "deployment.engine.apply.failed";
    public const string HistoryFailed = "deployment.engine.history.failed";
    public const string PruneDisabled = "deployment.engine.prune.disabled";
}
