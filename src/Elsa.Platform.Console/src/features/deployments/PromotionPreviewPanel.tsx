import { AlertTriangle, CheckCircle2, ClipboardCheck, GitBranch, GitCompareArrows, RotateCcw } from "lucide-react";
import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { Badge, Button, buttonClassName, SecondaryButton, Select, Table } from "@/components/ui";
import { RequestStateView } from "@/components/states/RequestStateViews";
import {
  type DeploymentCockpit,
  type DiffCategory,
  hasBlockingValidation,
  type PromotionComparison,
  type ValidationSeverity
} from "@/features/deployments/deploymentModels";
import { statusToneClass, type StatusTone } from "@/lib/status/statusBadges";

type PromotionPreviewPanelProps = {
  data: DeploymentCockpit;
  sourceEnvironmentId: string;
  targetEnvironmentId: string;
  comparison: PromotionComparison | undefined;
  readinessIssues: PromotionReadinessIssue[];
  canManageDesiredState: boolean;
  canPreview: boolean;
  canDeploy: boolean;
  hasPromotedTargetRevision: boolean;
  canRollback: boolean;
  rollbackBlockedReason?: string;
  isPreviewing: boolean;
  isPromoting: boolean;
  isQueueingDeployment: boolean;
  isQueueingRollback: boolean;
  notice: string;
  error?: string;
  onSourceEnvironmentChange: (environmentId: string) => void;
  onTargetEnvironmentChange: (environmentId: string) => void;
  onRefreshPreview: () => void;
  onPromote: () => void;
  onDeploy: () => void;
  onRollback: () => void;
};

export function PromotionPreviewPanel({
  data,
  sourceEnvironmentId,
  targetEnvironmentId,
  comparison,
  readinessIssues,
  canManageDesiredState,
  canPreview,
  canDeploy,
  hasPromotedTargetRevision,
  canRollback,
  rollbackBlockedReason,
  isPreviewing,
  isPromoting,
  isQueueingDeployment,
  isQueueingRollback,
  notice,
  error,
  onSourceEnvironmentChange,
  onTargetEnvironmentChange,
  onRefreshPreview,
  onPromote,
  onDeploy,
  onRollback
}: PromotionPreviewPanelProps) {
  const environmentOptions = data.applications.flatMap((application) => application.environments);
  const blocked = comparison ? hasBlockingValidation(comparison.validations) : true;
  const hasRollbackTarget = Boolean(comparison?.rollbackRevision && comparison.rollbackRevisionId);
  const previewBlocked = readinessIssues.some((issue) => issue.severity === "Blocker");
  const sourceApplication = data.applications.find((application) => application.environments.some((environment) => environment.id === sourceEnvironmentId));
  const observabilityRevisionPath = sourceApplication
    ? `/admin/deployments/applications/${encodeURIComponent(sourceApplication.id)}/environments/${encodeURIComponent(sourceEnvironmentId)}/revisions/new?includeObservability=1`
    : undefined;

  return (
    <div className="space-y-4">
      <div className="grid gap-3 lg:grid-cols-3">
        <FlowStep
          index="1"
          title="Create or choose source"
          description="Create a new desired-state revision in the source environment when workflow or artifact content changes."
        />
        <FlowStep
          index="2"
          title="Promote into target"
          description="Preview validation, then create a target revision from the selected source revision."
        />
        <FlowStep
          index="3"
          title="Deploy target revision"
          description="After the target revision exists and validation passes, queue deployment to the target engine."
        />
      </div>
      <div className="grid gap-3 md:grid-cols-[1fr_1fr_auto_auto] md:items-end">
        <label className="text-xs font-medium text-muted-foreground">
          Promote from
          <Select className="mt-1 w-full" value={sourceEnvironmentId} onChange={(event) => onSourceEnvironmentChange(event.target.value)}>
            {environmentOptions.map((environment) => (
              <option key={environment.id} value={environment.id}>{environment.name} r{environment.desiredRevision.revision}</option>
            ))}
          </Select>
        </label>
        <label className="text-xs font-medium text-muted-foreground">
          Promote into
          <Select className="mt-1 w-full" value={targetEnvironmentId} onChange={(event) => onTargetEnvironmentChange(event.target.value)}>
            {environmentOptions.map((environment) => (
              <option key={environment.id} value={environment.id}>{environment.name} r{environment.deployedRevision ?? environment.desiredRevision.revision}</option>
            ))}
          </Select>
        </label>
        <div className="rounded-ui border border-border bg-surface px-3 py-2 text-sm">
          {comparison ? `r${comparison.sourceRevision} -> r${comparison.targetRevision}` : "No comparison"}
        </div>
        <SecondaryButton disabled={previewBlocked || isPreviewing} onClick={onRefreshPreview}>
          {isPreviewing ? "Previewing" : "Preview promotion"}
        </SecondaryButton>
      </div>
      <p className="text-sm text-muted-foreground">
        Promotion creates a new desired-state revision in the target environment. Deployment is a separate action so the target revision can be reviewed before it is applied.
      </p>
      {!canManageDesiredState ? <p className="text-sm text-muted-foreground">Desired-state management permission is required to create promoted revisions.</p> : null}
      {readinessIssues.length > 0 ? <PromotionRequirements issues={readinessIssues} /> : null}
      {notice ? <div role="status" className="rounded-ui border border-border bg-muted/40 px-3 py-2 text-sm">{notice}</div> : null}
      {error ? <p role="alert" className="text-sm text-destructive">{error}</p> : null}

      {!comparison ? (
        <>
          <RequestStateView
            state="empty"
            title="No comparison available"
            description={previewBlocked
              ? "Resolve the promotion requirements, then refresh the preview."
              : "Choose a source and target environment, then preview promotion. When validation passes, create the target revision and deploy it."}
          />
          <div className="rounded-ui border border-border bg-surface p-3">
            <div className="mb-3 text-sm font-medium">Deployment gate</div>
            <div className="flex flex-col gap-2 sm:flex-row">
              <Button disabled>
                <GitBranch className="h-4 w-4" />
                Create Target Revision
              </Button>
              <Button disabled>
                <CheckCircle2 className="h-4 w-4" />
                Deploy Target Revision
              </Button>
            </div>
            <p className="mt-3 text-xs text-muted-foreground">Preview promotion before creating a target revision.</p>
          </div>
        </>
      ) : (
        <>
          <Panel title="Desired-state changes" icon={<GitCompareArrows className="h-4 w-4" />}>
            <Table>
              <table className="min-w-full divide-y divide-border text-sm">
                <thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2">Category</th>
                    <th className="px-3 py-2">Resource</th>
                    <th className="px-3 py-2">Source</th>
                    <th className="px-3 py-2">Target</th>
                    <th className="px-3 py-2">Impact</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {comparison.diff.map((item) => (
                    <tr key={item.id}>
                      <td className="px-3 py-3">{diffCategoryLabel(item.category)}</td>
                      <td className="px-3 py-3 font-medium">{item.name}</td>
                      <td className="px-3 py-3 text-muted-foreground">{item.sourceValue}</td>
                      <td className="px-3 py-3 text-muted-foreground">{item.targetValue}</td>
                      <td className="px-3 py-3"><StatusBadge value={item.impact} tone={item.impact === "Removed" ? "warning" : "neutral"} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Table>
          </Panel>
          {comparison.artifacts.length > 0 ? <ArtifactPanel artifacts={comparison.artifacts} /> : null}
          <div className="grid gap-3 lg:grid-cols-[1fr_auto] lg:items-start">
            <ValidationPanel
              validations={comparison.validations}
              observabilityRevisionPath={observabilityRevisionPath}
              canManageDesiredState={canManageDesiredState}
            />
            <div className="rounded-ui border border-border bg-surface p-3">
              <div className="mb-3 text-sm font-medium">Deployment gate</div>
              <div className="flex flex-col gap-2">
                <Button disabled={previewBlocked || blocked || !canManageDesiredState || isPromoting} onClick={onPromote}>
                  <GitBranch className="h-4 w-4" />
                  {isPromoting ? "Creating Target Revision" : "Create Target Revision"}
                </Button>
                <Button disabled={blocked || !canDeploy || !hasPromotedTargetRevision || isQueueingDeployment} onClick={onDeploy}>
                  <CheckCircle2 className="h-4 w-4" />
                  {isQueueingDeployment ? "Queueing Deployment" : "Deploy Target Revision"}
                </Button>
                <SecondaryButton disabled={!hasRollbackTarget || !canRollback || isQueueingRollback} onClick={onRollback}>
                  <RotateCcw className="h-4 w-4" />
                  {isQueueingRollback ? "Queueing Rollback" : `Roll Back to r${comparison.rollbackRevision ?? "-"}`}
                </SecondaryButton>
              </div>
              {blocked ? <p className="mt-3 text-xs text-destructive">Resolve validation blockers before promotion or deployment can start.</p> : null}
              {!hasPromotedTargetRevision ? <p className="mt-3 text-xs text-muted-foreground">Create the target revision before deployment can be queued.</p> : null}
              {rollbackBlockedReason ? <p className="mt-3 text-xs text-muted-foreground">{rollbackBlockedReason}</p> : null}
              {!canDeploy || !canRollback ? <p className="mt-3 text-xs text-muted-foreground">Deployment and rollback actions require execute permissions and single-user confirmation.</p> : null}
            </div>
          </div>
        </>
      )}
    </div>
  );
}

function FlowStep({ index, title, description }: { index: string; title: string; description: string }) {
  return (
    <div className="rounded-ui border border-border bg-muted/30 p-3">
      <div className="flex items-center gap-2 text-sm font-medium">
        <span className="flex h-6 w-6 items-center justify-center rounded-full border border-border bg-surface text-xs">{index}</span>
        {title}
      </div>
      <p className="mt-2 text-xs leading-5 text-muted-foreground">{description}</p>
    </div>
  );
}

type PromotionReadinessIssue = {
  id: string;
  severity: "Blocker" | "Warning";
  scope: string;
  message: string;
  action?: {
    label: string;
    to: string;
    description?: string;
  };
};

function PromotionRequirements({ issues }: { issues: PromotionReadinessIssue[] }) {
  return (
    <section className="rounded-ui border border-border bg-background p-3">
      <h3 className="text-sm font-medium">Promotion requirements</h3>
      <div className="mt-2 space-y-2">
        {issues.map((issue) => (
          <div key={issue.id} className="flex gap-2 text-sm">
            <AlertTriangle className={issue.severity === "Blocker" ? "mt-0.5 h-4 w-4 shrink-0 text-destructive" : "mt-0.5 h-4 w-4 shrink-0 text-warning"} />
            <div>
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-medium">{issue.scope}</span>
                <StatusBadge value={issue.severity} tone={issue.severity === "Blocker" ? "destructive" : "warning"} />
              </div>
              <p className="mt-1 text-muted-foreground">{issue.message}</p>
              {issue.action ? (
                <div className="mt-2 flex flex-wrap items-center gap-2">
                  <Link to={issue.action.to} className={buttonClassName("secondary", "h-8 px-2 text-xs")}>
                    {issue.action.label}
                  </Link>
                  {issue.action.description ? <span className="text-xs text-muted-foreground">{issue.action.description}</span> : null}
                </div>
              ) : null}
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}

function ArtifactPanel({ artifacts }: { artifacts: PromotionComparison["artifacts"] }) {
  return (
    <Panel title="Artifact references" icon={<GitCompareArrows className="h-4 w-4" />}>
      <Table>
        <table className="min-w-full divide-y divide-border text-sm">
          <thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground">
            <tr>
              <th className="px-3 py-2">Artifact</th>
              <th className="px-3 py-2">Identity</th>
              <th className="px-3 py-2">Type</th>
              <th className="px-3 py-2">Digest</th>
              <th className="px-3 py-2">Metadata</th>
              <th className="px-3 py-2">Runtime</th>
              <th className="px-3 py-2">Impact</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {artifacts.map((artifact) => {
              const reference = artifact.source ?? artifact.target;
              return (
                <tr key={artifact.name}>
                  <td className="px-3 py-3 font-medium">{artifact.name}</td>
                  <td className="px-3 py-3 text-muted-foreground">{reference?.artifactId ?? "Unavailable"}</td>
                  <td className="px-3 py-3 text-muted-foreground">{reference?.artifactTypeId ?? "Unknown"}</td>
                  <td className="px-3 py-3 text-muted-foreground">{digestLabel(reference?.contentDigest)}</td>
                  <td className="px-3 py-3 text-muted-foreground">{propertiesLabel(reference?.metadata)}</td>
                  <td className="px-3 py-3">
                    <RuntimeCompatibility validations={artifact.runtimeCompatibility} />
                  </td>
                  <td className="px-3 py-3"><StatusBadge value={artifact.impact} tone={artifact.impact === "Removed" ? "warning" : "neutral"} /></td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </Table>
    </Panel>
  );
}

function RuntimeCompatibility({ validations }: { validations: PromotionComparison["artifacts"][number]["runtimeCompatibility"] }) {
  if (validations.length === 0) return <span className="text-muted-foreground">Not evaluated</span>;
  const blocker = validations.find((validation) => validation.severity === "Blocker");
  const warning = validations.find((validation) => validation.severity === "Warning");
  const selected = blocker ?? warning ?? validations[0];
  return <StatusBadge value={selected.severity} tone={validationTone(selected.severity)} />;
}

function ValidationPanel({
  validations,
  observabilityRevisionPath,
  canManageDesiredState
}: {
  validations: PromotionComparison["validations"];
  observabilityRevisionPath?: string;
  canManageDesiredState: boolean;
}) {
  return (
    <Panel title="Validations" icon={<ClipboardCheck className="h-4 w-4" />}>
      <div className="space-y-2">
        {validations.map((validation) => (
          <div key={validation.id} className="flex gap-2 rounded-ui border border-border p-3 text-sm">
            {validation.severity === "Blocker" ? (
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-destructive" />
            ) : validation.severity === "Warning" ? (
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-warning" />
            ) : (
              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-success" />
            )}
            <div>
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-medium">{validation.scope}</span>
                <StatusBadge value={validation.severity} tone={validationTone(validation.severity)} />
              </div>
              <p className="mt-1 text-muted-foreground">{validation.message}</p>
              {validation.id === "deployment.tier.observability-required" ? (
                <div className="mt-3 space-y-3 rounded-ui border border-border bg-muted/30 p-3 text-xs text-muted-foreground">
                  <p>
                    Production promotion requires the source revision to declare where runtime telemetry will be sent.
                    Add at least one logs, metrics, traces, or console binding with a provider and scope.
                  </p>
                  {observabilityRevisionPath ? (
                    <Link
                      to={observabilityRevisionPath}
                      className={buttonClassName("secondary", !canManageDesiredState ? "pointer-events-none opacity-50" : undefined)}
                      aria-disabled={!canManageDesiredState}
                    >
                      <GitBranch className="h-4 w-4" />
                      Add binding to new revision
                    </Link>
                  ) : null}
                </div>
              ) : null}
            </div>
          </div>
        ))}
      </div>
    </Panel>
  );
}

function Panel({ title, icon, children }: { title: string; icon: ReactNode; children: ReactNode }) {
  return (
    <section className="rounded-ui border border-border bg-surface p-4">
      <h2 className="mb-3 flex items-center gap-2 text-sm font-semibold">{icon}{title}</h2>
      {children}
    </section>
  );
}

function StatusBadge({ value, tone }: { value: string; tone: StatusTone }) {
  return <Badge className={statusToneClass(tone)}>{value}</Badge>;
}

function validationTone(status: ValidationSeverity): StatusTone {
  if (status === "Pass") return "success";
  if (status === "Warning") return "warning";
  return "destructive";
}

function digestLabel(digest: { algorithm: string; value: string } | null | undefined) {
  return digest ? `${digest.algorithm}:${digest.value}` : "Missing";
}

function propertiesLabel(properties: Record<string, string> | null | undefined) {
  if (!properties || Object.keys(properties).length === 0) return "None";
  return Object.entries(properties)
    .map(([key, value]) => `${key}=${value}`)
    .join(", ");
}

function diffCategoryLabel(category: DiffCategory) {
  switch (category) {
    case "ShellConfiguration":
      return "Shell configuration";
    case "RuntimeConfiguration":
      return "Runtime configuration";
    case "SecretReferences":
      return "Secret references";
    case "EngineBindings":
      return "Engine bindings";
    default:
      return category;
  }
}
