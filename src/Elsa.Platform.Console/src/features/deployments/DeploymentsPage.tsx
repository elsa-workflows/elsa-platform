import { useQuery } from "@tanstack/react-query";
import {
  Activity,
  AlertTriangle,
  Bot,
  CheckCircle2,
  ClipboardCheck,
  GitCompareArrows,
  History,
  KeyRound,
  RadioTower,
  RefreshCw,
  RotateCcw,
  ShieldCheck,
  XCircle
} from "lucide-react";
import { useMemo, useState } from "react";
import type { ReactNode } from "react";
import { Badge, Button, SecondaryButton, Select, Table } from "@/components/ui";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { getDeploymentCockpit, getDeploymentWorkspaceContext } from "@/features/deployments/deploymentApi";
import {
  type DiffCategory,
  engineLabel,
  environmentLabel,
  hasBlockingValidation,
  supportedControlIds,
  type DeploymentCockpit,
  type DeploymentHealth,
  type DeploymentStatus,
  type DriftStatus,
  type ValidationSeverity,
  type WorkflowEngineRegistration
} from "@/features/deployments/deploymentModels";
import { formatDateTime } from "@/lib/formatters";
import { queryKeys } from "@/lib/query/queryClient";
import { statusToneClass, type StatusTone } from "@/lib/status/statusBadges";
import { cn } from "@/lib/utils";

type ViewId = "fleet" | "engine" | "promotion" | "governance" | "assistant";

const views: Array<{ id: ViewId; label: string }> = [
  { id: "fleet", label: "Environments" },
  { id: "engine", label: "Engine Registration" },
  { id: "promotion", label: "Promotion Diff" },
  { id: "governance", label: "Observability" },
  { id: "assistant", label: "Assistant Review" }
];

export function DeploymentsPage() {
  const workspaceContext = useQuery({ queryKey: queryKeys.deploymentWorkspaceContext, queryFn: getDeploymentWorkspaceContext });
  const workspaceId = workspaceContext.data?.workspaces[0]?.id ?? "";
  const cockpit = useQuery({
    queryKey: queryKeys.deploymentCockpit(workspaceId),
    queryFn: () => getDeploymentCockpit(workspaceId),
    enabled: Boolean(workspaceId)
  });
  const [activeView, setActiveView] = useState<ViewId>("fleet");
  const [selectedApplicationId, setSelectedApplicationId] = useState("claims-ops");
  const [selectedEnvironmentId, setSelectedEnvironmentId] = useState("claims-stage");
  const [selectedEngineId, setSelectedEngineId] = useState("stage-engine");
  const [sourceEnvironmentId, setSourceEnvironmentId] = useState("claims-stage");
  const [targetEnvironmentId, setTargetEnvironmentId] = useState("claims-prod");
  const [operationNotice, setOperationNotice] = useState("");
  const [assistantOutcome, setAssistantOutcome] = useState<"Proposed" | "Approved" | "Rejected">("Proposed");

  const data = cockpit.data;
  const selectedApplication = data?.applications.find((application) => application.id === selectedApplicationId) ?? data?.applications[0];
  const selectedEngine = data?.engines.find((engine) => engine.id === selectedEngineId) ?? data?.engines[0];

  const comparison = useMemo(() => {
    return data?.comparisons.find(
      (item) => item.sourceEnvironmentId === sourceEnvironmentId && item.targetEnvironmentId === targetEnvironmentId
    );
  }, [data?.comparisons, sourceEnvironmentId, targetEnvironmentId]);

  if (workspaceContext.isLoading || cockpit.isLoading) return <RequestStateView state="loading" title="Loading deployments" />;
  if (workspaceContext.isError) return <RequestStateView state="unexpected" title="Workspace context could not load" />;
  if (!workspaceId) {
    return <RequestStateView state="empty" title="No workspace selected" description="Sign in with a workspace membership to view deployments." />;
  }
  if (cockpit.isError || !data || !selectedApplication || !selectedEngine) {
    return <RequestStateView state="unexpected" title="Deployments could not load" />;
  }

  function inspectEnvironment(environmentId: string) {
    const engineId = data?.engines.find((engine) => engine.environmentId === environmentId)?.id;
    setSelectedEnvironmentId(environmentId);
    if (engineId) setSelectedEngineId(engineId);
    setActiveView("engine");
  }

  return (
    <section className="space-y-5">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <h1 className="text-xl font-semibold">Deployments</h1>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
            Workspace-scoped environment cockpit for workflow applications, engine registrations, desired-state promotion, observability, and assistant plan approvals.
          </p>
        </div>
        <div className="grid gap-2 sm:grid-cols-2">
          <label className="text-xs font-medium text-muted-foreground">
            Workflow application
            <Select
              className="mt-1 w-full"
              value={selectedApplication.id}
              onChange={(event) => setSelectedApplicationId(event.target.value)}
            >
              {data.applications.map((application) => (
                <option key={application.id} value={application.id}>
                  {application.name}
                </option>
              ))}
            </Select>
          </label>
          <div className="rounded-ui border border-border bg-surface px-3 py-2 text-xs text-muted-foreground">
            <div className="font-medium text-foreground">{selectedApplication.workspaceName}</div>
            <div>Workspace tenant boundary</div>
          </div>
        </div>
      </div>

      <div className="flex gap-1 overflow-x-auto border-b border-border">
        {views.map((view) => (
          <button
            key={view.id}
            type="button"
            aria-pressed={activeView === view.id}
            className={cn(
              "whitespace-nowrap border-b-2 px-3 py-2 text-sm transition-colors",
              activeView === view.id
                ? "border-primary text-foreground"
                : "border-transparent text-muted-foreground hover:text-foreground"
            )}
            onClick={() => setActiveView(view.id)}
          >
            {view.label}
          </button>
        ))}
      </div>

      {activeView === "fleet" ? (
        <FleetView
          application={selectedApplication}
          engines={data.engines}
          onInspectEnvironment={inspectEnvironment}
        />
      ) : null}
      {activeView === "engine" ? (
        <EngineView
          data={data}
          selectedEnvironmentId={selectedEnvironmentId}
          selectedEngine={selectedEngine}
          operationNotice={operationNotice}
          onEnvironmentChange={(environmentId) => {
            const nextEngine = data.engines.find((engine) => engine.environmentId === environmentId);
            setSelectedEnvironmentId(environmentId);
            if (nextEngine) setSelectedEngineId(nextEngine.id);
            setOperationNotice("");
          }}
          onEngineChange={(engineId) => {
            const engine = data.engines.find((item) => item.id === engineId);
            if (engine) setSelectedEnvironmentId(engine.environmentId);
            setSelectedEngineId(engineId);
            setOperationNotice("");
          }}
          onRunControl={(label, boundary) => setOperationNotice(`${label} queued as a ${boundary} control for ${selectedEngine.name}.`)}
        />
      ) : null}
      {activeView === "promotion" ? (
        <PromotionView
          data={data}
          sourceEnvironmentId={sourceEnvironmentId}
          targetEnvironmentId={targetEnvironmentId}
          onSourceEnvironmentChange={setSourceEnvironmentId}
          onTargetEnvironmentChange={setTargetEnvironmentId}
          comparison={comparison}
        />
      ) : null}
      {activeView === "governance" ? <GovernanceView data={data} /> : null}
      {activeView === "assistant" ? (
        <AssistantPlanView
          data={data}
          outcome={assistantOutcome}
          onApprove={() => setAssistantOutcome("Approved")}
          onReject={() => setAssistantOutcome("Rejected")}
        />
      ) : null}
    </section>
  );
}

function FleetView({
  application,
  engines,
  onInspectEnvironment
}: {
  application: DeploymentCockpit["applications"][number];
  engines: WorkflowEngineRegistration[];
  onInspectEnvironment: (environmentId: string) => void;
}) {
  const applicationEngineIds = new Set(application.environments.flatMap((environment) => environment.engineIds));
  const applicationEngines = engines.filter((engine) => applicationEngineIds.has(engine.id));

  return (
    <div className="space-y-4">
      <div className="grid gap-3 md:grid-cols-4">
        <MetricCard label="Environments" value={String(application.environments.length)} />
        <MetricCard label="Registered engines" value={String(applicationEngines.length)} />
        <MetricCard label="Healthy engines" value={String(applicationEngines.filter((engine) => engine.health === "Healthy").length)} />
        <MetricCard label="Drift detected" value={String(application.environments.filter((environment) => environment.driftStatus === "DriftDetected").length)} />
      </div>
      <Table>
        <table className="min-w-full divide-y divide-border text-sm">
          <thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground">
            <tr>
              <th className="px-3 py-2">Environment</th>
              <th className="px-3 py-2">Health</th>
              <th className="px-3 py-2">Desired revision</th>
              <th className="px-3 py-2">Deployed</th>
              <th className="px-3 py-2">Drift</th>
              <th className="px-3 py-2">Deployment</th>
              <th className="px-3 py-2">Engines</th>
              <th className="px-3 py-2"><span className="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {application.environments.map((environment) => (
              <tr key={environment.id}>
                <td className="px-3 py-3">
                  <div className="font-medium">{environment.name}</div>
                  <div className="text-xs text-muted-foreground">{environment.tier}</div>
                </td>
                <td className="px-3 py-3"><StatusBadge value={environment.health} tone={healthTone(environment.health)} /></td>
                <td className="px-3 py-3">
                  <div>r{environment.desiredRevision.revision}</div>
                  <div className="text-xs text-muted-foreground">{environment.desiredRevision.commit}</div>
                </td>
                <td className="px-3 py-3">{environment.deployedRevision ? `r${environment.deployedRevision}` : "-"}</td>
                <td className="px-3 py-3"><StatusBadge value={driftLabel(environment.driftStatus)} tone={driftTone(environment.driftStatus)} /></td>
                <td className="px-3 py-3"><StatusBadge value={environment.deploymentStatus} tone={deploymentTone(environment.deploymentStatus)} /></td>
                <td className="px-3 py-3 text-muted-foreground">{environment.engineIds.length}</td>
                <td className="px-3 py-3 text-right">
                  <SecondaryButton className="h-8" onClick={() => onInspectEnvironment(environment.id)}>
                    Inspect
                  </SecondaryButton>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Table>
    </div>
  );
}

function EngineView({
  data,
  selectedEnvironmentId,
  selectedEngine,
  operationNotice,
  onEnvironmentChange,
  onEngineChange,
  onRunControl
}: {
  data: DeploymentCockpit;
  selectedEnvironmentId: string;
  selectedEngine: WorkflowEngineRegistration;
  operationNotice: string;
  onEnvironmentChange: (environmentId: string) => void;
  onEngineChange: (engineId: string) => void;
  onRunControl: (label: string, boundary: string) => void;
}) {
  const environmentOptions = data.applications.flatMap((application) => application.environments);
  const environmentEngines = data.engines.filter((engine) => engine.environmentId === selectedEnvironmentId);
  const controls = supportedControlIds(selectedEngine);

  return (
    <div className="space-y-4">
      <div className="grid gap-3 md:grid-cols-2">
        <label className="text-xs font-medium text-muted-foreground">
          Environment
          <Select className="mt-1 w-full" value={selectedEnvironmentId} onChange={(event) => onEnvironmentChange(event.target.value)}>
            {environmentOptions.map((environment) => (
              <option key={environment.id} value={environment.id}>{environment.name}</option>
            ))}
          </Select>
        </label>
        <label className="text-xs font-medium text-muted-foreground">
          Workflow engine
          <Select className="mt-1 w-full" value={selectedEngine.id} onChange={(event) => onEngineChange(event.target.value)}>
            {environmentEngines.map((engine) => (
              <option key={engine.id} value={engine.id}>{engine.name}</option>
            ))}
          </Select>
        </label>
      </div>

      <div className="grid gap-3 lg:grid-cols-[1.2fr_1fr]">
        <Panel title={selectedEngine.name} icon={<RadioTower className="h-4 w-4" />}>
          <dl className="grid gap-3 sm:grid-cols-2">
            <Detail label="Endpoint" value={selectedEngine.endpoint.baseUrl} />
            <Detail label="Region" value={selectedEngine.endpoint.region} />
            <Detail label="Version" value={selectedEngine.endpoint.version} />
            <Detail label="Certificate" value={selectedEngine.endpoint.certificateStatus} />
            <Detail label="Health" value={<StatusBadge value={selectedEngine.health} tone={healthTone(selectedEngine.health)} />} />
            <Detail label="Last heartbeat" value={formatDateTime(selectedEngine.lastHeartbeatAt)} />
          </dl>
        </Panel>
        <Panel title="Credential reference" icon={<KeyRound className="h-4 w-4" />}>
          <dl className="space-y-3">
            <Detail label="Provider" value={selectedEngine.credentialReference.provider} />
            <Detail label="Reference" value={selectedEngine.credentialReference.reference} />
            <Detail label="Verification" value={<StatusBadge value={selectedEngine.credentialReference.verificationStatus} tone={credentialTone(selectedEngine.credentialReference.verificationStatus)} />} />
            <Detail label="Last verified" value={formatDateTime(selectedEngine.credentialReference.lastVerifiedAt)} />
          </dl>
        </Panel>
      </div>

      <div className="grid gap-3 lg:grid-cols-2">
        <Panel title="Advertised capabilities" icon={<ClipboardCheck className="h-4 w-4" />}>
          <div className="flex flex-wrap gap-2">
            {selectedEngine.capabilities.map((capability) => (
              <Badge key={capability.id} className="gap-1">
                <span className="font-medium">{capability.label}</span>
                <span className="text-muted-foreground">{capability.boundary}</span>
              </Badge>
            ))}
          </div>
        </Panel>
        <Panel title="Supported controls" icon={<RefreshCw className="h-4 w-4" />}>
          <div className="space-y-2">
            {controls.map((control) => (
              <div key={control.id} className="flex flex-col gap-2 rounded-ui border border-border p-3 sm:flex-row sm:items-center sm:justify-between">
                <div>
                  <div className="font-medium">{control.label}</div>
                  <div className="text-xs text-muted-foreground">{control.boundary} boundary · {control.description}</div>
                </div>
                <SecondaryButton
                  className="h-8 shrink-0"
                  disabled={selectedEngine.health === "Unreachable"}
                  onClick={() => onRunControl(control.label, control.boundary)}
                >
                  Run
                </SecondaryButton>
              </div>
            ))}
            <p className="text-xs text-muted-foreground">Unavailable controls stay hidden unless a matching engine or hosting capability is advertised.</p>
            {operationNotice ? <div role="status" className="rounded-ui border border-primary/30 bg-primary/10 px-3 py-2 text-sm">{operationNotice}</div> : null}
          </div>
        </Panel>
      </div>
    </div>
  );
}

function PromotionView({
  data,
  sourceEnvironmentId,
  targetEnvironmentId,
  comparison,
  onSourceEnvironmentChange,
  onTargetEnvironmentChange
}: {
  data: DeploymentCockpit;
  sourceEnvironmentId: string;
  targetEnvironmentId: string;
  comparison: DeploymentCockpit["comparisons"][number] | undefined;
  onSourceEnvironmentChange: (environmentId: string) => void;
  onTargetEnvironmentChange: (environmentId: string) => void;
}) {
  const environmentOptions = data.applications.flatMap((application) => application.environments);
  const blocked = comparison ? hasBlockingValidation(comparison.validations) : true;

  return (
    <div className="space-y-4">
      <div className="grid gap-3 md:grid-cols-[1fr_1fr_auto] md:items-end">
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
          {comparison ? `r${comparison.sourceRevision} → r${comparison.targetRevision}` : "No comparison"}
        </div>
      </div>

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
                <Button disabled={blocked}>
                  <CheckCircle2 className="h-4 w-4" />
                  Deploy Revision
                </Button>
                <SecondaryButton disabled={!comparison.rollbackRevision}>
                  <RotateCcw className="h-4 w-4" />
                  Roll Back to r{comparison.rollbackRevision ?? "-"}
                </SecondaryButton>
              </div>
              {blocked ? <p className="mt-3 text-xs text-destructive">Resolve validation blockers before deployment can start.</p> : null}
            </div>
          </div>
        </>
      )}
    </div>
  );
}

function GovernanceView({ data }: { data: DeploymentCockpit }) {
  return (
    <div className="space-y-4">
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        {data.observabilityBindings.map((binding) => (
          <Panel key={binding.id} title={binding.kind} icon={<Activity className="h-4 w-4" />}>
            <div className="space-y-2 text-sm">
              <div className="flex items-center justify-between gap-2">
                <span className="font-medium">{binding.provider}</span>
                <StatusBadge value={binding.status} tone={binding.status === "Connected" ? "success" : binding.status === "Degraded" ? "warning" : "destructive"} />
              </div>
              <p className="text-muted-foreground">{binding.scope}</p>
              <p className="text-xs text-muted-foreground">Revision r{binding.correlatedRevision} · {binding.sample}</p>
            </div>
          </Panel>
        ))}
      </div>

      <div className="grid gap-4 xl:grid-cols-2">
        <Panel title="Deployment history" icon={<History className="h-4 w-4" />}>
          <Table>
            <table className="min-w-full divide-y divide-border text-sm">
              <thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground">
                <tr>
                  <th className="px-3 py-2">Revision</th>
                  <th className="px-3 py-2">Status</th>
                  <th className="px-3 py-2">Actor</th>
                  <th className="px-3 py-2">Target</th>
                  <th className="px-3 py-2">Validation</th>
                  <th className="px-3 py-2">When</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {data.history.map((event) => (
                  <tr key={event.id}>
                    <td className="px-3 py-3">
                      r{event.revision}
                      {event.rollbackSourceRevision ? <div className="text-xs text-muted-foreground">from r{event.rollbackSourceRevision}</div> : null}
                    </td>
                    <td className="px-3 py-3"><StatusBadge value={event.status} tone={deploymentTone(event.status)} /></td>
                    <td className="px-3 py-3">{event.actor}</td>
                    <td className="px-3 py-3 text-muted-foreground">{environmentLabel(event.environmentId, data.applications)} / {engineLabel(event.engineId, data.engines)}</td>
                    <td className="px-3 py-3">{event.validationOutcome}</td>
                    <td className="px-3 py-3">{formatDateTime(event.occurredAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Table>
        </Panel>
        <Panel title="Drift report" icon={<AlertTriangle className="h-4 w-4" />}>
          <div className="space-y-2">
            {data.driftReport.map((item) => (
              <div key={item.id} className="rounded-ui border border-border p-3 text-sm">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-medium">{item.area}</span>
                  <StatusBadge value={item.action} tone={item.action === "Redeploy" ? "warning" : "neutral"} />
                </div>
                <div className="mt-2 grid gap-2 text-xs text-muted-foreground sm:grid-cols-2">
                  <div>Desired: {item.desired}</div>
                  <div>Observed: {item.observed}</div>
                </div>
                <div className="mt-2 text-xs text-muted-foreground">{environmentLabel(item.environmentId, data.applications)} / {engineLabel(item.engineId, data.engines)}</div>
              </div>
            ))}
          </div>
        </Panel>
      </div>
    </div>
  );
}

function AssistantPlanView({
  data,
  outcome,
  onApprove,
  onReject
}: {
  data: DeploymentCockpit;
  outcome: "Proposed" | "Approved" | "Rejected";
  onApprove: () => void;
  onReject: () => void;
}) {
  const plan = data.assistantPlans[0];
  const displayedStatus = outcome === "Proposed" ? plan.status : outcome;
  const blocked = hasBlockingValidation(plan.validations);

  return (
    <div className="grid gap-4 xl:grid-cols-[1.2fr_1fr]">
      <Panel title={`Immutable plan ${plan.id} v${plan.version}`} icon={<Bot className="h-4 w-4" />}>
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge value={displayedStatus} tone={displayedStatus === "Approved" ? "success" : displayedStatus === "Rejected" ? "destructive" : "warning"} />
            <Badge>{plan.allOrNothing ? "All-or-nothing execution" : "Partial execution"}</Badge>
            <Badge>Created {formatDateTime(plan.createdAt)}</Badge>
          </div>
          <p className="text-sm text-muted-foreground">{plan.summary}</p>
          <dl className="grid gap-3 sm:grid-cols-3">
            <Detail label="Workspace" value={plan.workspaceName} />
            <Detail label="Environment" value={environmentLabel(plan.targetEnvironmentId, data.applications)} />
            <Detail label="Engine" value={engineLabel(plan.targetEngineId, data.engines)} />
          </dl>
          <div className="grid gap-3 md:grid-cols-2">
            <ActionList title="Proposed actions" actions={plan.proposedActions} icon={<ClipboardCheck className="h-4 w-4 text-warning" />} />
            <ActionList title="Executed actions" actions={plan.executedActions.length > 0 ? plan.executedActions : ["No platform mutations executed."]} icon={<ShieldCheck className="h-4 w-4 text-success" />} muted={plan.executedActions.length === 0} />
          </div>
          <div className="rounded-ui border border-border bg-muted/30 p-3 text-sm">
            <span className="font-medium">Rollback path: </span>
            <span className="text-muted-foreground">{plan.rollbackPath}</span>
          </div>
        </div>
      </Panel>

      <div className="space-y-4">
        <ValidationPanel validations={plan.validations} />
        <Panel title="Decision" icon={<ShieldCheck className="h-4 w-4" />}>
          <div className="space-y-3">
            <p className="text-sm text-muted-foreground">
              Approval records the exact plan artifact, deciding account, workspace, environment, validations, and final outcome before execution is allowed.
            </p>
            <div className="flex flex-col gap-2 sm:flex-row">
              <Button disabled={blocked || outcome !== "Proposed"} onClick={onApprove}>
                <CheckCircle2 className="h-4 w-4" />
                Approve Plan
              </Button>
              <SecondaryButton disabled={outcome !== "Proposed"} onClick={onReject}>
                <XCircle className="h-4 w-4" />
                Reject Plan
              </SecondaryButton>
            </div>
            {blocked ? <p className="text-xs text-destructive">This proposed plan cannot execute while validation blockers remain.</p> : null}
            {outcome !== "Proposed" ? <div role="status" className="rounded-ui border border-border bg-muted/40 px-3 py-2 text-sm">Plan marked {outcome}; no executed actions were added by this local review.</div> : null}
          </div>
        </Panel>
      </div>
    </div>
  );
}

function ValidationPanel({ validations }: { validations: DeploymentCockpit["comparisons"][number]["validations"] }) {
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

function ActionList({ title, actions, icon, muted = false }: { title: string; actions: string[]; icon: ReactNode; muted?: boolean }) {
  return (
    <div className="rounded-ui border border-border p-3">
      <div className="mb-2 flex items-center gap-2 text-sm font-medium">{icon}{title}</div>
      <ul className={cn("space-y-1 text-sm", muted ? "text-muted-foreground" : "")}>
        {actions.map((action) => <li key={action}>{action}</li>)}
      </ul>
    </div>
  );
}

function MetricCard({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-ui border border-border bg-surface p-3">
      <div className="text-2xl font-semibold">{value}</div>
      <div className="text-xs text-muted-foreground">{label}</div>
    </div>
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

function Detail({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-1 break-words text-sm">{value || "-"}</dd>
    </div>
  );
}

function StatusBadge({ value, tone }: { value: string; tone: StatusTone }) {
  return <Badge className={statusToneClass(tone)}>{value}</Badge>;
}

function healthTone(health: DeploymentHealth): StatusTone {
  if (health === "Healthy") return "success";
  if (health === "Degraded") return "warning";
  return "destructive";
}

function driftTone(status: DriftStatus): StatusTone {
  if (status === "InSync") return "success";
  if (status === "DriftDetected") return "warning";
  return "neutral";
}

function deploymentTone(status: DeploymentStatus): StatusTone {
  if (status === "Succeeded") return "success";
  if (status === "Running" || status === "RolledBack") return "warning";
  return "destructive";
}

function credentialTone(status: string): StatusTone {
  if (status === "Verified") return "success";
  if (status === "Unverified") return "warning";
  return "destructive";
}

function validationTone(status: ValidationSeverity): StatusTone {
  if (status === "Pass") return "success";
  if (status === "Warning") return "warning";
  return "destructive";
}

function driftLabel(status: DriftStatus) {
  return status === "InSync" ? "In sync" : status === "DriftDetected" ? "Drift detected" : "Unknown";
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
