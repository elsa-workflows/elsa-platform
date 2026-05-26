import { AlertTriangle, CheckCircle2, ClipboardCheck, GitCompareArrows, RotateCcw } from "lucide-react";
import type { ReactNode } from "react";
import { Badge, Button, SecondaryButton, Select, Table } from "@/components/ui";
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
  canPreview: boolean;
  canDeploy: boolean;
  canRollback: boolean;
  isPreviewing: boolean;
  isQueueingDeployment: boolean;
  isQueueingRollback: boolean;
  notice: string;
  error?: string;
  onSourceEnvironmentChange: (environmentId: string) => void;
  onTargetEnvironmentChange: (environmentId: string) => void;
  onRefreshPreview: () => void;
  onDeploy: () => void;
  onRollback: () => void;
};

export function PromotionPreviewPanel({
  data,
  sourceEnvironmentId,
  targetEnvironmentId,
  comparison,
  canPreview,
  canDeploy,
  canRollback,
  isPreviewing,
  isQueueingDeployment,
  isQueueingRollback,
  notice,
  error,
  onSourceEnvironmentChange,
  onTargetEnvironmentChange,
  onRefreshPreview,
  onDeploy,
  onRollback
}: PromotionPreviewPanelProps) {
  const environmentOptions = data.applications.flatMap((application) => application.environments);
  const blocked = comparison ? hasBlockingValidation(comparison.validations) : true;
  const hasRollbackTarget = Boolean(comparison?.rollbackRevision && comparison.rollbackRevisionId);

  return (
    <div className="space-y-4">
      <div className="grid gap-3 md:grid-cols-[1fr_1fr_auto_auto] md:items-end">
        <label className="text-xs font-medium text-muted-foreground">
          Source revision
          <Select className="mt-1 w-full" value={sourceEnvironmentId} onChange={(event) => onSourceEnvironmentChange(event.target.value)}>
            {environmentOptions.map((environment) => (
              <option key={environment.id} value={environment.id}>{environment.name} r{environment.desiredRevision.revision}</option>
            ))}
          </Select>
        </label>
        <label className="text-xs font-medium text-muted-foreground">
          Target revision
          <Select className="mt-1 w-full" value={targetEnvironmentId} onChange={(event) => onTargetEnvironmentChange(event.target.value)}>
            {environmentOptions.map((environment) => (
              <option key={environment.id} value={environment.id}>{environment.name} r{environment.deployedRevision ?? environment.desiredRevision.revision}</option>
            ))}
          </Select>
        </label>
        <div className="rounded-ui border border-border bg-surface px-3 py-2 text-sm">
          {comparison ? `r${comparison.sourceRevision} -> r${comparison.targetRevision}` : "No comparison"}
        </div>
        <SecondaryButton disabled={!canPreview || isPreviewing} onClick={onRefreshPreview}>
          {isPreviewing ? "Previewing" : "Refresh Preview"}
        </SecondaryButton>
      </div>
      {!canPreview ? <p className="text-sm text-muted-foreground">Promotion preview permission is required for live validation.</p> : null}
      {notice ? <div role="status" className="rounded-ui border border-border bg-muted/40 px-3 py-2 text-sm">{notice}</div> : null}
      {error ? <p role="alert" className="text-sm text-destructive">{error}</p> : null}

      {!comparison ? (
        <RequestStateView state="empty" title="No comparison available" description="Choose a supported source and target environment pair." />
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
          <div className="grid gap-3 lg:grid-cols-[1fr_auto] lg:items-start">
            <ValidationPanel validations={comparison.validations} />
            <div className="rounded-ui border border-border bg-surface p-3">
              <div className="mb-3 text-sm font-medium">Deployment gate</div>
              <div className="flex flex-col gap-2">
                <Button disabled={blocked || !canDeploy || isQueueingDeployment} onClick={onDeploy}>
                  <CheckCircle2 className="h-4 w-4" />
                  {isQueueingDeployment ? "Queueing Deployment" : "Deploy Revision"}
                </Button>
                <SecondaryButton disabled={!hasRollbackTarget || !canRollback || isQueueingRollback} onClick={onRollback}>
                  <RotateCcw className="h-4 w-4" />
                  {isQueueingRollback ? "Queueing Rollback" : `Roll Back to r${comparison.rollbackRevision ?? "-"}`}
                </SecondaryButton>
              </div>
              {blocked ? <p className="mt-3 text-xs text-destructive">Resolve validation blockers before deployment can start.</p> : null}
              {!canDeploy || !canRollback ? <p className="mt-3 text-xs text-muted-foreground">Deployment and rollback actions require execute permissions and single-user confirmation.</p> : null}
            </div>
          </div>
        </>
      )}
    </div>
  );
}

function ValidationPanel({ validations }: { validations: PromotionComparison["validations"] }) {
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
