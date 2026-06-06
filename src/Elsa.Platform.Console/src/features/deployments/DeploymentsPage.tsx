import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Activity,
  AlertTriangle,
  ArrowLeft,
  Bot,
  CheckCircle2,
  ClipboardCheck,
  GitBranch,
  KeyRound,
  Pencil,
  Plus,
  RadioTower,
  RefreshCw,
  Rocket,
  Save,
  Search,
  Settings2,
  ShieldCheck,
  XCircle
} from "lucide-react";
import { useMemo, useState } from "react";
import type { ReactNode } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { Badge, Button, buttonClassName, EmptyState, Input, SecondaryButton, Select, Table } from "@/components/ui";
import { RequestStateView } from "@/components/states/RequestStateViews";
import {
  createActionConfirmation,
  createDeploymentApplication,
  createDeploymentEnvironment,
  createDesiredStateRevision,
  getDeploymentCockpit,
  getDeploymentPermissions,
  getDeploymentTiers,
  previewPromotion,
  promoteRevision,
  queueDeploymentRun,
  queueRollbackRun,
  registerDeploymentEngine,
  runRuntimeControl,
  updateDeploymentApplication,
  updateDeploymentEngine,
  updateDeploymentEnvironment,
  verifyDeploymentEngine
} from "@/features/deployments/deploymentApi";
import { listWorkspaceArtifacts } from "@/features/artifacts/artifactApi";
import type { WorkspaceArtifact } from "@/features/artifacts/artifactModels";
import {
  CredentialReferenceInput,
  DeploymentSetupPanel,
  engineRegistrationRequest,
  setupEngineRequest,
  type CredentialReferenceOption,
  type DeploymentSetupValues,
  type EngineRegistrationValues
} from "@/features/deployments/DeploymentSetupPanel";
import { DeploymentRunsPanel } from "@/features/deployments/DeploymentRunsPanel";
import { PromotionPreviewPanel } from "@/features/deployments/PromotionPreviewPanel";
import { RuntimeControlsPanel } from "@/features/deployments/RuntimeControlsPanel";
import {
  deploymentTierCapabilities,
  engineLabel,
  environmentLabel,
  hasBlockingValidation,
  type DeploymentCockpit,
  type DeploymentHealth,
  type DeploymentStatus,
  type DriftStatus,
  type EnvironmentSummary,
  type ObservabilityBinding,
  type WorkspaceDesiredStateRecordRequest,
  type RuntimeControl,
  type ValidationSeverity,
  type WorkspaceDeploymentTier,
  type WorkflowEngineRegistration
} from "@/features/deployments/deploymentModels";
import { useWorkspaceContext } from "@/app/WorkspaceContextProvider";
import { formatDateTime } from "@/lib/formatters";
import { queryKeys } from "@/lib/query/queryClient";
import { statusToneClass, type StatusTone } from "@/lib/status/statusBadges";
import { cn } from "@/lib/utils";

type DeploymentContext = {
  workspaceId: string;
  data: DeploymentCockpit;
  tiers: WorkspaceDeploymentTier[];
  activeTiers: WorkspaceDeploymentTier[];
  credentialOptions: CredentialReferenceOption[];
  canManageSetup: boolean;
  canManageDesiredState: boolean;
  canPreviewPromotion: boolean;
  canExecuteDeployment: boolean;
  canExecuteRollback: boolean;
  canExecuteControls: boolean;
  refreshDeploymentCockpit: () => Promise<unknown>;
};

type EnvironmentFormValues = {
  name: string;
  tierId: string | null;
  tier: EnvironmentSummary["tier"];
};

type DeploymentBlocker = {
  id: string;
  validationId?: string;
  scope: string;
  message: string;
  severity: ValidationSeverity;
  source: string;
  actionPath?: string;
  actionLabel?: string;
};

type PromotionReadinessIssue = {
  id: string;
  scope: string;
  message: string;
  severity: "Blocker" | "Warning";
  action?: {
    label: string;
    to: string;
    description?: string;
  };
};

type ApplicationSort = "name" | "health" | "environments" | "engines" | "drift";
type EnvironmentSort = "name" | "tier" | "health" | "deployment" | "drift";
type EngineSort = "name" | "health" | "verification" | "heartbeat";

const applicationSorts: { value: ApplicationSort; label: string }[] = [
  { value: "name", label: "Application" },
  { value: "health", label: "Health" },
  { value: "environments", label: "Environments" },
  { value: "engines", label: "Engines" },
  { value: "drift", label: "Drift" }
];

const environmentSorts: { value: EnvironmentSort; label: string }[] = [
  { value: "name", label: "Environment" },
  { value: "tier", label: "Tier" },
  { value: "health", label: "Health" },
  { value: "deployment", label: "Deployment" },
  { value: "drift", label: "Drift" }
];

const engineSorts: { value: EngineSort; label: string }[] = [
  { value: "name", label: "Engine" },
  { value: "health", label: "Health" },
  { value: "verification", label: "Credential verification" },
  { value: "heartbeat", label: "Last heartbeat" }
];

export function DeploymentsPage() {
  const context = useDeploymentContext();
  if (context.status !== "ready") return context.state;

  const { data, canManageSetup } = context.value;
  const totals = deploymentTotals(data);
  const healthyEngines = data.engines.filter((engine) => engine.health === "Healthy").length;

  return (
    <section className="space-y-5">
      <PageHeader
        title="Deployment overview"
        description="Dashboard view for workspace deployment posture, drift, health, recent activity, and operational shortcuts."
        actions={
          <>
            <Link to="/admin/deployments/applications" className={buttonClassName("secondary")}>
              <Rocket className="h-4 w-4" />
              Applications
            </Link>
            <Link to="/admin/deployments/new" className={buttonClassName("primary", !canManageSetup ? "pointer-events-none opacity-50" : undefined)} aria-disabled={!canManageSetup}>
              <Plus className="h-4 w-4" />
              New application setup
            </Link>
            <Link to="/admin/deployments/tiers" className={buttonClassName("secondary")}>
              <Settings2 className="h-4 w-4" />
              Workspace tiers
            </Link>
          </>
        }
      />

      <div className="grid gap-3 md:grid-cols-4">
        <MetricCard label="Applications" value={String(data.applications.length)} />
        <MetricCard label="Environments" value={String(totals.environments)} />
        <MetricCard label="Registered engines" value={String(data.engines.length)} />
        <MetricCard label="Drift detected" value={String(totals.drift)} />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Panel title="Deployment posture" icon={<ShieldCheck className="h-4 w-4" />}>
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            <Detail label="Healthy engines" value={`${healthyEngines} of ${data.engines.length}`} />
            <Detail label="Blocked environments" value={String(data.applications.flatMap((application) => application.environments).filter((environment) => environment.deploymentStatus === "Blocked").length)} />
            <Detail label="Production tiers" value={String(data.applications.flatMap((application) => application.environments).filter((environment) => environment.tierCapabilities?.includes("deployment.tier.production-like")).length)} />
            <Detail label="Assistant plans" value={String(data.assistantPlans.length)} />
          </dl>
        </Panel>

        <Panel title="Recent activity" icon={<Activity className="h-4 w-4" />}>
          {data.history[0] ? (
            <div className="space-y-2 text-sm">
              <div className="flex items-center justify-between gap-2">
                <span className="font-medium">r{data.history[0].revision}</span>
                <StatusBadge value={data.history[0].status} tone={deploymentTone(data.history[0].status as DeploymentStatus)} />
              </div>
              <p className="text-muted-foreground">{environmentLabel(data.history[0].environmentId, data.applications)} / {engineLabel(data.history[0].engineId, data.engines)}</p>
              <p className="text-xs text-muted-foreground">{formatDateTime(data.history[0].occurredAt)}</p>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">No deployment history has been recorded.</p>
          )}
        </Panel>
      </div>

      <Panel title="Operational shortcuts" icon={<Settings2 className="h-4 w-4" />}>
        <div className="grid gap-3 md:grid-cols-3">
          <Link to="/admin/deployments/applications" className="rounded-ui border border-border bg-background p-3 text-sm transition-colors hover:bg-muted">
            <div className="font-medium">Applications</div>
            <p className="mt-1 text-xs text-muted-foreground">Manage application, environment, and engine hierarchy.</p>
          </Link>
          <Link to="/admin/deployments/tiers" className="rounded-ui border border-border bg-background p-3 text-sm transition-colors hover:bg-muted">
            <div className="font-medium">Workspace tiers</div>
            <p className="mt-1 text-xs text-muted-foreground">Configure tier capabilities and environment policy.</p>
          </Link>
          <Link to="/admin/deployments/new" className={cn("rounded-ui border border-border bg-background p-3 text-sm transition-colors hover:bg-muted", !canManageSetup ? "pointer-events-none opacity-50" : "")} aria-disabled={!canManageSetup}>
            <div className="font-medium">New application setup</div>
            <p className="mt-1 text-xs text-muted-foreground">Create an application with its first environment and engine.</p>
          </Link>
        </div>
      </Panel>
    </section>
  );
}

export function DeploymentApplicationsPage() {
  const context = useDeploymentContext();
  if (context.status !== "ready") return context.state;
  return <DeploymentApplicationsReady context={context.value} />;
}

function DeploymentApplicationsReady({ context }: { context: DeploymentContext }) {
  const { data, canManageSetup } = context;
  const [query, setQuery] = useState("");
  const [sort, setSort] = useState<ApplicationSort>("name");
  const applications = useMemo(
    () => sortApplications(filterApplications(data.applications, data, query), data, sort),
    [data, query, sort]
  );

  return (
    <section className="space-y-5">
      <Breadcrumbs items={[{ label: "Deployments", to: "/admin/deployments" }, { label: "Applications" }]} />
      <PageHeader
        title="Applications"
        description="Workflow applications registered for deployment management in this workspace."
        actions={
          <Link to="/admin/deployments/new" className={buttonClassName("primary", !canManageSetup ? "pointer-events-none opacity-50" : undefined)} aria-disabled={!canManageSetup}>
            <Plus className="h-4 w-4" />
            New application setup
          </Link>
        }
      />

      <div className="grid gap-3 lg:grid-cols-[minmax(16rem,26rem)_auto] lg:items-center">
        <label className="relative block">
          <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input value={query} onChange={(event) => setQuery(event.target.value)} className="pl-9" placeholder="Search applications" />
        </label>
        <Select value={sort} onChange={(event) => setSort(event.target.value as ApplicationSort)} aria-label="Sort applications">
          {applicationSorts.map((item) => (
            <option key={item.value} value={item.value}>{item.label}</option>
          ))}
        </Select>
      </div>

      {data.applications.length === 0 ? (
        <EmptyState
          title="No deployment setup"
          description="Create a workflow application, first environment, and first engine registration to start managing deployments."
          action={
            <Link to="/admin/deployments/new" className={buttonClassName()}>
              <Plus className="h-4 w-4" />
              New application setup
            </Link>
          }
        />
      ) : applications.length === 0 ? (
        <EmptyState title="No matching applications" description="Clear the search to see all workflow applications." />
      ) : (
        <section className="space-y-3">
          <SectionHeader title="Workflow applications" description="Open an application to manage its environments and deployment operations." />
          <ApplicationTable applications={applications} data={data} />
        </section>
      )}
    </section>
  );
}
export function NewDeploymentSetupPage() {
  const context = useDeploymentContext();
  if (context.status !== "ready") return context.state;
  return <NewDeploymentSetupReady context={context.value} />;
}

function NewDeploymentSetupReady({ context }: { context: DeploymentContext }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { workspaceId, activeTiers, credentialOptions, canManageSetup } = context;
  const setup = useMutation({
    mutationFn: async (values: DeploymentSetupValues) => {
      const application = await createDeploymentApplication(workspaceId, { name: values.applicationName, description: null });
      const environment = await createDeploymentEnvironment(workspaceId, application.id, {
        name: values.environmentName,
        tier: values.environmentTier,
        tierId: values.environmentTierId
      });
      await registerDeploymentEngine(workspaceId, environment.id, setupEngineRequest(values));
      return { applicationId: application.id, environmentId: environment.id };
    },
    onSuccess: async (created) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.deploymentCockpit(workspaceId) });
      navigate(environmentPath(created.applicationId, created.environmentId));
    }
  });

  return (
    <FormPageShell
      title="New application setup"
      description="Create a workflow application with its first environment and engine registration."
      breadcrumbs={[
        { label: "Deployments", to: "/admin/deployments" },
        { label: "Applications", to: "/admin/deployments/applications" },
        { label: "New application setup" }
      ]}
    >
      <DeploymentSetupPanel
        canManageSetup={canManageSetup}
        tiers={activeTiers}
        credentialOptions={credentialOptions}
        isSubmitting={setup.isPending}
        error={setup.error instanceof Error ? setup.error.message : undefined}
        onSubmit={(values) => setup.mutate(values)}
      />
    </FormPageShell>
  );
}

export function DeploymentApplicationPage() {
  const context = useDeploymentContext();
  const { applicationId = "" } = useParams();
  if (context.status !== "ready") return context.state;
  return <DeploymentApplicationReady context={context.value} applicationId={applicationId} />;
}

function DeploymentApplicationReady({ context, applicationId }: { context: DeploymentContext; applicationId: string }) {
  const { data, canManageSetup } = context;
  const application = findApplication(data, applicationId);
  if (!application) return <RequestStateView state="not-found" title="Application not found" />;

  const [environmentQuery, setEnvironmentQuery] = useState("");
  const [environmentSort, setEnvironmentSort] = useState<EnvironmentSort>("name");
  const environments = useMemo(
    () => sortEnvironments(filterEnvironments(application.environments, environmentQuery), environmentSort),
    [application.environments, environmentQuery, environmentSort]
  );

  return (
    <section className="space-y-5">
      <Breadcrumbs
        items={[
          { label: "Deployments", to: "/admin/deployments" },
          { label: "Applications", to: "/admin/deployments/applications" },
          { label: application.name }
        ]}
      />
      <PageHeader
        title={application.name}
        description="Manage environments for this workflow application."
        actions={
          <>
            <Link to={`/admin/deployments/applications/${application.id}/edit`} className={buttonClassName("secondary", !canManageSetup ? "pointer-events-none opacity-50" : undefined)} aria-disabled={!canManageSetup}>
              <Pencil className="h-4 w-4" />
              Edit application
            </Link>
            <Link to={`/admin/deployments/applications/${application.id}/environments/new`} className={buttonClassName("primary", !canManageSetup ? "pointer-events-none opacity-50" : undefined)} aria-disabled={!canManageSetup}>
              <Plus className="h-4 w-4" />
              Add environment
            </Link>
          </>
        }
      />

      <div className="grid gap-3 lg:grid-cols-[minmax(16rem,26rem)_auto] lg:items-center">
        <label className="relative block">
          <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input value={environmentQuery} onChange={(event) => setEnvironmentQuery(event.target.value)} className="pl-9" placeholder="Search environments" />
        </label>
        <Select value={environmentSort} onChange={(event) => setEnvironmentSort(event.target.value as EnvironmentSort)} aria-label="Sort environments">
          {environmentSorts.map((item) => (
            <option key={item.value} value={item.value}>{item.label}</option>
          ))}
        </Select>
      </div>

      <section className="space-y-3">
        <SectionHeader title="Environments" description="Open an environment to inspect engines, promotions, runs, drift, and approvals." />
        {application.environments.length === 0 ? (
          <EmptyState title="No environments registered" description="Add an environment and its first workflow engine registration." />
        ) : environments.length === 0 ? (
          <EmptyState title="No matching environments" description="Clear the search to see all environments." />
        ) : (
          <EnvironmentTable application={application} environments={environments} data={data} />
        )}
      </section>
    </section>
  );
}

export function DeploymentApplicationEditPage() {
  const context = useDeploymentContext();
  const { applicationId = "" } = useParams();
  if (context.status !== "ready") return context.state;
  return <DeploymentApplicationEditReady context={context.value} applicationId={applicationId} />;
}

function DeploymentApplicationEditReady({ context, applicationId }: { context: DeploymentContext; applicationId: string }) {
  const navigate = useNavigate();
  const { workspaceId, data } = context;
  const application = findApplication(data, applicationId);
  if (!application) return <RequestStateView state="not-found" title="Application not found" />;

  const updateApplication = useMutation({
    mutationFn: (name: string) => updateDeploymentApplication(workspaceId, application.id, { name, description: null }),
    onSuccess: async () => {
      await context.refreshDeploymentCockpit();
      navigate(applicationPath(application.id));
    }
  });

  return (
    <FormPageShell
      title="Edit application"
      description="Update the workflow application name."
      breadcrumbs={[
        { label: "Deployments", to: "/admin/deployments" },
        { label: "Applications", to: "/admin/deployments/applications" },
        { label: application.name, to: applicationPath(application.id) },
        { label: "Edit" }
      ]}
    >
      <ApplicationEditPanel
        application={application}
        isSubmitting={updateApplication.isPending}
        error={updateApplication.error instanceof Error ? updateApplication.error.message : undefined}
        onCancel={() => navigate(applicationPath(application.id))}
        onSubmit={(name) => updateApplication.mutate(name)}
      />
    </FormPageShell>
  );
}

export function DeploymentEnvironmentCreatePage() {
  const context = useDeploymentContext();
  const { applicationId = "" } = useParams();
  if (context.status !== "ready") return context.state;
  return <DeploymentEnvironmentCreateReady context={context.value} applicationId={applicationId} />;
}

function DeploymentEnvironmentCreateReady({ context, applicationId }: { context: DeploymentContext; applicationId: string }) {
  const navigate = useNavigate();
  const { workspaceId, data, activeTiers, credentialOptions, canManageSetup } = context;
  const application = findApplication(data, applicationId);
  if (!application) return <RequestStateView state="not-found" title="Application not found" />;

  const createEnvironment = useMutation({
    mutationFn: async (values: DeploymentSetupValues) => {
      const environment = await createDeploymentEnvironment(workspaceId, application.id, {
        name: values.environmentName,
        tier: values.environmentTier,
        tierId: values.environmentTierId
      });
      await registerDeploymentEngine(workspaceId, environment.id, setupEngineRequest(values));
      return environment;
    },
    onSuccess: async (environment) => {
      await context.refreshDeploymentCockpit();
      navigate(environmentPath(application.id, environment.id));
    }
  });

  return (
    <FormPageShell
      title="Add environment"
      description="Create an environment and register its first workflow engine."
      breadcrumbs={[
        { label: "Deployments", to: "/admin/deployments" },
        { label: "Applications", to: "/admin/deployments/applications" },
        { label: application.name, to: applicationPath(application.id) },
        { label: "Add environment" }
      ]}
    >
      <DeploymentSetupPanel
        fixedApplicationName={application.name}
        canManageSetup={canManageSetup}
        tiers={activeTiers}
        credentialOptions={credentialOptions}
        submitLabel="Add environment"
        isSubmitting={createEnvironment.isPending}
        error={createEnvironment.error instanceof Error ? createEnvironment.error.message : undefined}
        onSubmit={(values) => createEnvironment.mutate(values)}
      />
    </FormPageShell>
  );
}

export function DeploymentEnvironmentPage() {
  const context = useDeploymentContext();
  const { applicationId = "", environmentId = "" } = useParams();
  if (context.status !== "ready") return context.state;
  return <DeploymentEnvironmentReady context={context.value} applicationId={applicationId} environmentId={environmentId} />;
}

function DeploymentEnvironmentReady({
  context,
  applicationId,
  environmentId
}: {
  context: DeploymentContext;
  applicationId: string;
  environmentId: string;
}) {
  const {
    data,
    canManageSetup,
    canManageDesiredState,
    canPreviewPromotion,
    canExecuteDeployment,
    canExecuteRollback,
    workspaceId
  } = context;
  const resolved = resolveEnvironment(data, applicationId, environmentId);
  if (!resolved.application) return <RequestStateView state="not-found" title="Application not found" />;
  if (!resolved.environment) return <RequestStateView state="not-found" title="Environment not found" />;

  const { application, environment } = resolved;
  const environmentEngines = enginesForEnvironment(data, environment.id);
  const defaultComparison =
    data.comparisons.find((comparison) => comparison.targetEnvironmentId === environment.id) ??
    data.comparisons.find((comparison) => comparison.sourceEnvironmentId === environment.id) ??
    data.comparisons[0];
  const [sourceEnvironmentId, setSourceEnvironmentId] = useState(defaultComparison?.sourceEnvironmentId ?? application.environments[0]?.id ?? environment.id);
  const [targetEnvironmentId, setTargetEnvironmentId] = useState(defaultComparison?.targetEnvironmentId ?? environment.id);
  const [previewComparison, setPreviewComparison] = useState<DeploymentCockpit["comparisons"][number] | null>(null);
  const [promotedTargetRevisionId, setPromotedTargetRevisionId] = useState<string | null>(null);
  const [promotionNotice, setPromotionNotice] = useState("");
  const [assistantOutcome, setAssistantOutcome] = useState<"Proposed" | "Approved" | "Rejected">("Proposed");
  const [engineQuery, setEngineQuery] = useState("");
  const [engineSort, setEngineSort] = useState<EngineSort>("name");
  const visibleEngines = useMemo(
    () => sortEngines(filterEngines(environmentEngines, engineQuery), engineSort),
    [engineQuery, engineSort, environmentEngines]
  );
  const deploymentBlockers = useMemo(
    () => collectDeploymentBlockers(data, environment, environmentEngines),
    [data, environment, environmentEngines]
  );
  const artifactList = useQuery({
    queryKey: queryKeys.artifacts(workspaceId),
    queryFn: () => listWorkspaceArtifacts(workspaceId),
    enabled: Boolean(workspaceId)
  });
  const hasValidArtifacts = Boolean(artifactList.data?.items.some(isValidArtifactForRevision));

  const comparison = useMemo(() => {
    const cockpitComparison = data.comparisons.find(
      (item) => item.sourceEnvironmentId === sourceEnvironmentId && item.targetEnvironmentId === targetEnvironmentId
    );
    return previewComparison && previewComparison.sourceEnvironmentId === sourceEnvironmentId && previewComparison.targetEnvironmentId === targetEnvironmentId
      ? previewComparison
      : cockpitComparison;
  }, [data.comparisons, previewComparison, sourceEnvironmentId, targetEnvironmentId]);
  const promotionReadinessIssues = useMemo(
    () => collectPromotionReadinessIssues(data, sourceEnvironmentId, targetEnvironmentId, canPreviewPromotion, hasValidArtifacts),
    [canPreviewPromotion, data, hasValidArtifacts, sourceEnvironmentId, targetEnvironmentId]
  );

  function getEnvironment(targetId: string) {
    return data.applications.flatMap((item) => item.environments).find((item) => item.id === targetId);
  }

  function getTargetEngine(targetId: string) {
    return data.engines.find((engine) => engine.environmentId === targetId);
  }

  function getLatestTargetRun(targetId: string, engineId: string) {
    return data.history.find((event) => event.environmentId === targetId && event.engineId === engineId);
  }

  const preview = useMutation({
    mutationFn: () => {
      assertPromotionReady(promotionReadinessIssues);
      const source = getEnvironment(sourceEnvironmentId);
      const targetEngine = getTargetEngine(targetEnvironmentId);
      if (!source?.desiredRevision.id || !targetEngine)
        throw new Error("Choose a source revision and target engine before refreshing preview.");

      return previewPromotion(workspaceId, {
        sourceEnvironmentId,
        targetEnvironmentId,
        sourceRevisionId: source.desiredRevision.id,
        targetEngineId: targetEngine.id
      });
    },
    onSuccess: (comparisonResult) => {
      setPreviewComparison(comparisonResult);
      setPromotionNotice("Promotion preview refreshed from live validation.");
    }
  });
  const promoteTargetRevision = useMutation({
    mutationFn: async () => {
      assertPromotionReady(promotionReadinessIssues);
      const source = getEnvironment(sourceEnvironmentId);
      const targetEngine = getTargetEngine(targetEnvironmentId);
      if (!source?.desiredRevision.id || !targetEngine)
        throw new Error("Choose a source revision and target engine before creating the target revision.");

      return promoteRevision(workspaceId, {
        sourceEnvironmentId,
        targetEnvironmentId,
        sourceRevisionId: source.desiredRevision.id,
        targetEngineId: targetEngine.id,
        label: `Promoted from ${source.name} r${source.desiredRevision.revision}`,
        commit: source.desiredRevision.commit || null
      });
    },
    onSuccess: async (promotion) => {
      setPreviewComparison(promotion.comparison);
      setPromotedTargetRevisionId(promotion.targetRevision.id);
      setPromotionNotice(`Target revision r${promotion.targetRevision.revisionNumber} created. Review the gate, then deploy when ready.`);
      await context.refreshDeploymentCockpit();
    }
  });
  const deployRevision = useMutation({
    mutationFn: async () => {
      const targetEngine = getTargetEngine(comparison?.targetEnvironmentId ?? targetEnvironmentId);
      if (!comparison || !targetEngine || !promotedTargetRevisionId)
        throw new Error("Create a target revision before deployment.");

      const confirmation = await createActionConfirmation(workspaceId, {
        actionType: "Deploy",
        targetId: promotedTargetRevisionId,
        lifetimeSeconds: null
      });
      return queueDeploymentRun(workspaceId, {
        sourceRevisionId: promotedTargetRevisionId,
        targetEnvironmentId: comparison.targetEnvironmentId,
        targetEngineId: targetEngine.id,
        confirmationId: confirmation.id,
        mode: "Apply"
      });
    },
    onSuccess: (run) => {
      setPromotionNotice(`Deployment run ${run.status.toLowerCase()} for revision ${run.sourceRevisionId}.`);
      void context.refreshDeploymentCockpit();
    }
  });
  const rollbackRevision = useMutation({
    mutationFn: async () => {
      const targetEngine = getTargetEngine(comparison?.targetEnvironmentId ?? targetEnvironmentId);
      const rollbackSourceRun = getLatestTargetRun(comparison?.targetEnvironmentId ?? targetEnvironmentId, targetEngine?.id ?? "");
      const sourceRevisionId = comparison?.rollbackRevisionId;
      if (!comparison || !targetEngine || !rollbackSourceRun || !sourceRevisionId)
        throw new Error("A previous compatible run is required before rollback can be queued.");

      const confirmation = await createActionConfirmation(workspaceId, {
        actionType: "Rollback",
        targetId: sourceRevisionId,
        lifetimeSeconds: null
      });
      return queueRollbackRun(workspaceId, {
        sourceRevisionId,
        targetEnvironmentId: comparison.targetEnvironmentId,
        targetEngineId: targetEngine.id,
        confirmationId: confirmation.id,
        rollbackSourceRunId: rollbackSourceRun.id,
        mode: "Apply"
      });
    },
    onSuccess: (run) => {
      setPromotionNotice(`Rollback run ${run.status.toLowerCase()} for revision ${run.sourceRevisionId}.`);
      void context.refreshDeploymentCockpit();
    }
  });
  const targetAllowsRollback = hasTierCapability(getEnvironment(targetEnvironmentId), deploymentTierCapabilities.rollbackEnabled);

  return (
    <section className="space-y-5">
      <Breadcrumbs
        items={[
          { label: "Deployments", to: "/admin/deployments" },
          { label: "Applications", to: "/admin/deployments/applications" },
          { label: application.name, to: applicationPath(application.id) },
          { label: environment.name }
        ]}
      />
      <PageHeader
        title={environment.name}
        description={`${application.name} deployment environment`}
        actions={
          <>
            <Link to={`${environmentPath(application.id, environment.id)}/edit`} className={buttonClassName("secondary", !canManageSetup ? "pointer-events-none opacity-50" : undefined)} aria-disabled={!canManageSetup}>
              <Pencil className="h-4 w-4" />
              Edit environment
            </Link>
            <Link to={`${environmentPath(application.id, environment.id)}/revisions/new`} className={buttonClassName("secondary", !canManageDesiredState ? "pointer-events-none opacity-50" : undefined)} aria-disabled={!canManageDesiredState}>
              <GitBranch className="h-4 w-4" />
              New revision
            </Link>
            <Link to={`${environmentPath(application.id, environment.id)}/engines/new`} className={buttonClassName("primary", !canManageSetup ? "pointer-events-none opacity-50" : undefined)} aria-disabled={!canManageSetup}>
              <Plus className="h-4 w-4" />
              Register engine
            </Link>
          </>
        }
      />

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_360px]">
        <div className="space-y-4">
          <div className="grid gap-3 md:grid-cols-4">
            <MetricCard label="Health" value={environment.health} tone={healthTone(environment.health)} />
            <MetricCard label="Deployment" value={environment.deploymentStatus} tone={deploymentTone(environment.deploymentStatus)} />
            <MetricCard label="Drift" value={driftLabel(environment.driftStatus)} tone={driftTone(environment.driftStatus)} />
            <MetricCard label="Engines" value={String(environmentEngines.length)} />
          </div>

          {deploymentBlockers.length > 0 ? <DeploymentBlockersPanel blockers={deploymentBlockers} canManageDesiredState={canManageDesiredState} /> : null}

          <Panel title="Engine registrations" icon={<RadioTower className="h-4 w-4" />}>
            {environmentEngines.length === 0 ? (
              <EmptyState
                title="No engine registered"
                description="Register an engine before verifying runtime health or running controls."
                action={<Link to={`${environmentPath(application.id, environment.id)}/engines/new`} className={buttonClassName()}>Register engine</Link>}
              />
            ) : (
              <div className="space-y-3">
                <div className="grid gap-3 lg:grid-cols-[minmax(16rem,26rem)_auto] lg:items-center">
                  <label className="relative block">
                    <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
                    <Input value={engineQuery} onChange={(event) => setEngineQuery(event.target.value)} className="pl-9" placeholder="Search engines" />
                  </label>
                  <Select value={engineSort} onChange={(event) => setEngineSort(event.target.value as EngineSort)} aria-label="Sort engines">
                    {engineSorts.map((item) => (
                      <option key={item.value} value={item.value}>{item.label}</option>
                    ))}
                  </Select>
                </div>
                {visibleEngines.length === 0 ? (
                  <EmptyState title="No matching engines" description="Clear the search to see all engine registrations." />
                ) : (
                  <EngineTable applicationId={application.id} environmentId={environment.id} engines={visibleEngines} />
                )}
              </div>
            )}
          </Panel>

          <Panel title="Promotion" icon={<GitBranch className="h-4 w-4" />}>
            <PromotionPreviewPanel
              data={data}
              sourceEnvironmentId={sourceEnvironmentId}
              targetEnvironmentId={targetEnvironmentId}
              comparison={comparison}
              readinessIssues={promotionReadinessIssues}
              canManageDesiredState={canManageDesiredState}
              canPreview={canPreviewPromotion}
              canDeploy={canExecuteDeployment}
              hasPromotedTargetRevision={Boolean(promotedTargetRevisionId)}
              canRollback={canExecuteRollback && targetAllowsRollback}
              rollbackBlockedReason={targetAllowsRollback ? undefined : "Rollback is not enabled for the target tier."}
              isPreviewing={preview.isPending}
              isQueueingDeployment={deployRevision.isPending}
              isQueueingRollback={rollbackRevision.isPending}
              isPromoting={promoteTargetRevision.isPending}
              notice={promotionNotice}
              error={
                preview.error instanceof Error
                  ? preview.error.message
                  : promoteTargetRevision.error instanceof Error
                    ? promoteTargetRevision.error.message
                  : deployRevision.error instanceof Error
                    ? deployRevision.error.message
                    : rollbackRevision.error instanceof Error
                      ? rollbackRevision.error.message
                      : undefined
              }
              onSourceEnvironmentChange={(nextId) => {
                setSourceEnvironmentId(nextId);
                setPreviewComparison(null);
                setPromotedTargetRevisionId(null);
                setPromotionNotice("");
              }}
              onTargetEnvironmentChange={(nextId) => {
                setTargetEnvironmentId(nextId);
                setPreviewComparison(null);
                setPromotedTargetRevisionId(null);
                setPromotionNotice("");
              }}
              onRefreshPreview={() => {
                setPromotedTargetRevisionId(null);
                preview.mutate();
              }}
              onPromote={() => promoteTargetRevision.mutate()}
              onDeploy={() => deployRevision.mutate()}
              onRollback={() => rollbackRevision.mutate()}
            />
          </Panel>

          <EnvironmentOperations data={data} environment={environment} />
          <AssistantPlanView
            data={data}
            targetEnvironmentId={environment.id}
            outcome={assistantOutcome}
            onApprove={() => setAssistantOutcome("Approved")}
            onReject={() => setAssistantOutcome("Rejected")}
          />
        </div>

        <aside className="space-y-4">
          <Panel title="Environment" icon={<ShieldCheck className="h-4 w-4" />}>
            <dl className="space-y-3">
              <Detail label="Tier" value={`${environment.tierName || environment.tier}${environment.tierStatus === "Archived" ? " (archived)" : ""}`} />
              <Detail label="Desired revision" value={`r${environment.desiredRevision.revision} · ${environment.desiredRevision.commit}`} />
              <Detail label="Deployed revision" value={environment.deployedRevision ? `r${environment.deployedRevision}` : "Not deployed"} />
              <Detail label="Desired label" value={environment.desiredRevision.label} />
              <Detail label="Authored" value={formatDateTime(environment.desiredRevision.authoredAt)} />
            </dl>
          </Panel>
          <Panel title="Tier capabilities" icon={<ClipboardCheck className="h-4 w-4" />}>
            <div className="flex flex-wrap gap-2">
              {(environment.tierCapabilities ?? []).length > 0 ? (
                environment.tierCapabilities?.map((capability) => <Badge key={capability}>{capability}</Badge>)
              ) : (
                <p className="text-sm text-muted-foreground">No tier capabilities are reported for this environment.</p>
              )}
            </div>
          </Panel>
        </aside>
      </div>
    </section>
  );
}

export function DeploymentEnvironmentEditPage() {
  const context = useDeploymentContext();
  const { applicationId = "", environmentId = "" } = useParams();
  if (context.status !== "ready") return context.state;
  return <DeploymentEnvironmentEditReady context={context.value} applicationId={applicationId} environmentId={environmentId} />;
}

function DeploymentEnvironmentEditReady({
  context,
  applicationId,
  environmentId
}: {
  context: DeploymentContext;
  applicationId: string;
  environmentId: string;
}) {
  const navigate = useNavigate();
  const { workspaceId, data, tiers } = context;
  const resolved = resolveEnvironment(data, applicationId, environmentId);
  if (!resolved.application) return <RequestStateView state="not-found" title="Application not found" />;
  if (!resolved.environment) return <RequestStateView state="not-found" title="Environment not found" />;
  const { application, environment } = resolved;

  const updateEnvironmentMutation = useMutation({
    mutationFn: (values: EnvironmentFormValues) =>
      updateDeploymentEnvironment(workspaceId, application.id, environment.id, {
        name: values.name,
        tier: values.tier,
        tierId: values.tierId
    }),
    onSuccess: async () => {
      await context.refreshDeploymentCockpit();
      navigate(environmentPath(application.id, environment.id));
    }
  });

  return (
    <FormPageShell
      title="Edit environment"
      description="Update environment metadata and tier assignment."
      breadcrumbs={[
        { label: "Deployments", to: "/admin/deployments" },
        { label: "Applications", to: "/admin/deployments/applications" },
        { label: application.name, to: applicationPath(application.id) },
        { label: environment.name, to: environmentPath(application.id, environment.id) },
        { label: "Edit" }
      ]}
    >
      <EnvironmentForm
        environment={environment}
        tiers={tiers}
        isSubmitting={updateEnvironmentMutation.isPending}
        error={updateEnvironmentMutation.error instanceof Error ? updateEnvironmentMutation.error.message : undefined}
        onCancel={() => navigate(environmentPath(application.id, environment.id))}
        onSubmit={(values) => updateEnvironmentMutation.mutate(values)}
      />
    </FormPageShell>
  );
}

export function DeploymentRevisionCreatePage() {
  const context = useDeploymentContext();
  const { applicationId = "", environmentId = "" } = useParams();
  if (context.status !== "ready") return context.state;
  return <DeploymentRevisionCreateReady context={context.value} applicationId={applicationId} environmentId={environmentId} />;
}

function DeploymentRevisionCreateReady({
  context,
  applicationId,
  environmentId
}: {
  context: DeploymentContext;
  applicationId: string;
  environmentId: string;
}) {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const { workspaceId, data, canManageDesiredState } = context;
  const resolved = resolveEnvironment(data, applicationId, environmentId);
  const [label, setLabel] = useState("");
  const [commit, setCommit] = useState("");
  const [artifactRecordId, setArtifactRecordId] = useState("");
  const [includeObservability, setIncludeObservability] = useState(searchParams.get("includeObservability") === "1");
  const [observabilityKind, setObservabilityKind] = useState<ObservabilityBinding["kind"]>("Traces");
  const [observabilityProvider, setObservabilityProvider] = useState("OpenTelemetry Collector");
  const [observabilityScope, setObservabilityScope] = useState(() => `${resolved.environment?.name ?? "Environment"} / workflow runtime`);
  const [observabilitySample, setObservabilitySample] = useState("Runtime telemetry is expected for promotion and deployment review.");
  const artifacts = useQuery({
    queryKey: queryKeys.artifacts(workspaceId),
    queryFn: () => listWorkspaceArtifacts(workspaceId)
  });
  const artifactItems = (artifacts.data?.items ?? []).filter((artifact) => artifact.status !== "Archived");
  const selectedArtifact = artifactItems.find((artifact) => artifact.id === artifactRecordId) ?? artifactItems[0];

  const createRevision = useMutation({
    mutationFn: async () => {
      if (!resolved.application || !resolved.environment)
        throw new Error("Environment not found.");
      if (!selectedArtifact)
        throw new Error("Choose an artifact before creating a revision.");
      if (includeObservability && (!observabilityProvider.trim() || !observabilityScope.trim()))
        throw new Error("Observability provider and scope are required.");

      const records = [artifactRevisionRecord(selectedArtifact)];
      if (includeObservability) {
        records.push(observabilityRevisionRecord({
          kind: observabilityKind,
          provider: observabilityProvider,
          scope: observabilityScope,
          sample: observabilitySample
        }));
      }

      return createDesiredStateRevision(workspaceId, resolved.application.id, resolved.environment.id, {
        label: label.trim() || artifactDisplayName(selectedArtifact),
        commit: commit.trim() || null,
        records
      });
    },
    onSuccess: async () => {
      if (!resolved.application || !resolved.environment) return;
      await context.refreshDeploymentCockpit();
      navigate(environmentPath(resolved.application.id, resolved.environment.id));
    }
  });

  if (!resolved.application) return <RequestStateView state="not-found" title="Application not found" />;
  if (!resolved.environment) return <RequestStateView state="not-found" title="Environment not found" />;
  const { application, environment } = resolved;
  const isLoadingArtifacts = artifacts.isLoading || artifacts.isFetching;
  const submitDisabled = !canManageDesiredState || isLoadingArtifacts || artifactItems.length === 0 || createRevision.isPending;

  return (
    <FormPageShell
      title="New revision"
      description="Create a desired-state revision from a registered deployment artifact."
      breadcrumbs={[
        { label: "Deployments", to: "/admin/deployments" },
        { label: "Applications", to: "/admin/deployments/applications" },
        { label: application.name, to: applicationPath(application.id) },
        { label: environment.name, to: environmentPath(application.id, environment.id) },
        { label: "New revision" }
      ]}
    >
      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_320px]">
        <form
          className="space-y-4 rounded-ui border border-border bg-surface p-4"
          onSubmit={(event) => {
            event.preventDefault();
            createRevision.mutate();
          }}
        >
          {!canManageDesiredState ? (
            <p className="rounded-ui border border-border bg-muted/40 px-3 py-2 text-sm text-muted-foreground">
              You need desired-state management permission to create revisions.
            </p>
          ) : null}
          {artifacts.error instanceof Error ? <p role="alert" className="text-sm text-destructive">{artifacts.error.message}</p> : null}
          {createRevision.error instanceof Error ? <p role="alert" className="text-sm text-destructive">{createRevision.error.message}</p> : null}

          {isLoadingArtifacts ? (
            <RequestStateView state="loading" title="Loading artifacts" description="Fetching registered deployment artifacts." />
          ) : artifactItems.length === 0 ? (
            <EmptyState
              title="No artifacts registered"
              description="Register a deployment artifact before creating a desired-state revision."
              action={<Link to="/admin/artifacts" className={buttonClassName("secondary")}>Open artifacts</Link>}
            />
          ) : (
            <>
              <label className="block text-sm font-medium">
                Artifact
                <Select
                  className="mt-1 w-full"
                  value={selectedArtifact?.id ?? ""}
                  onChange={(event) => setArtifactRecordId(event.target.value)}
                >
                  {artifactItems.map((artifact) => (
                    <option key={artifact.id} value={artifact.id}>{artifactDisplayName(artifact)}</option>
                  ))}
                </Select>
              </label>
              <div className="grid gap-3 md:grid-cols-2">
                <label className="block text-sm font-medium">
                  Revision label
                  <Input value={label} onChange={(event) => setLabel(event.target.value)} placeholder={selectedArtifact ? artifactDisplayName(selectedArtifact) : "Revision label"} className="mt-1" />
                </label>
                <label className="block text-sm font-medium">
                  Commit
                  <Input value={commit} onChange={(event) => setCommit(event.target.value)} placeholder="Optional source commit" className="mt-1" />
                </label>
              </div>
              <div className="rounded-ui border border-border bg-muted/20 p-3">
                <label className="flex items-start gap-2 text-sm font-medium">
                  <input
                    type="checkbox"
                    className="mt-1"
                    checked={includeObservability}
                    onChange={(event) => setIncludeObservability(event.target.checked)}
                  />
                  <span>
                    Include observability binding
                    <span className="mt-1 block text-xs font-normal leading-5 text-muted-foreground">
                      Production targets require the desired state to declare at least one logs, metrics, traces, or console telemetry binding.
                    </span>
                  </span>
                </label>
                {includeObservability ? (
                  <div className="mt-3 grid gap-3 md:grid-cols-2">
                    <label className="block text-sm font-medium">
                      Signal
                      <Select
                        className="mt-1 w-full"
                        value={observabilityKind}
                        onChange={(event) => setObservabilityKind(event.target.value as ObservabilityBinding["kind"])}
                      >
                        <option value="Traces">Traces</option>
                        <option value="Logs">Logs</option>
                        <option value="Metrics">Metrics</option>
                        <option value="Console">Console</option>
                      </Select>
                    </label>
                    <label className="block text-sm font-medium">
                      Provider
                      <Input
                        value={observabilityProvider}
                        onChange={(event) => setObservabilityProvider(event.target.value)}
                        placeholder="OpenTelemetry Collector"
                        className="mt-1"
                      />
                    </label>
                    <label className="block text-sm font-medium">
                      Scope
                      <Input
                        value={observabilityScope}
                        onChange={(event) => setObservabilityScope(event.target.value)}
                        placeholder={`${environment.name} / workflow runtime`}
                        className="mt-1"
                      />
                    </label>
                    <label className="block text-sm font-medium">
                      Sample or note
                      <Input
                        value={observabilitySample}
                        onChange={(event) => setObservabilitySample(event.target.value)}
                        placeholder="Runtime telemetry endpoint configured."
                        className="mt-1"
                      />
                    </label>
                  </div>
                ) : null}
              </div>
              <div className="flex flex-wrap justify-end gap-2">
                <Link to={environmentPath(application.id, environment.id)} className={buttonClassName("secondary")}>Cancel</Link>
                <Button type="submit" disabled={submitDisabled}>
                  <Save className="h-4 w-4" />
                  {createRevision.isPending ? "Creating revision" : "Create revision"}
                </Button>
              </div>
            </>
          )}
        </form>
        <aside className="space-y-3">
          <Panel title="Revision flow" icon={<GitBranch className="h-4 w-4" />}>
            <ActionList
              title="After creation"
              icon={<ClipboardCheck className="h-4 w-4" />}
              actions={[
                "The revision becomes the latest desired state for this environment.",
                "If included, the observability binding satisfies production-tier telemetry validation.",
                "Use Promotion to copy it into a higher environment.",
                "Use Deploy Target Revision after promotion validation passes."
              ]}
            />
          </Panel>
          {selectedArtifact ? (
            <Panel title="Selected artifact" icon={<Rocket className="h-4 w-4" />}>
              <dl className="grid gap-3">
                <Detail label="Artifact" value={artifactDisplayName(selectedArtifact)} />
                <Detail label="Type" value={selectedArtifact.artifactTypeId ?? "Unknown"} />
                <Detail label="Digest" value={`${selectedArtifact.contentDigest.algorithm}:${selectedArtifact.contentDigest.value}`} />
                <Detail label="Reference" value={selectedArtifact.reference} />
              </dl>
            </Panel>
          ) : null}
        </aside>
      </div>
    </FormPageShell>
  );
}

export function DeploymentEngineRegisterPage() {
  const context = useDeploymentContext();
  const { applicationId = "", environmentId = "" } = useParams();
  if (context.status !== "ready") return context.state;
  return <DeploymentEngineRegisterReady context={context.value} applicationId={applicationId} environmentId={environmentId} />;
}

function DeploymentEngineRegisterReady({
  context,
  applicationId,
  environmentId
}: {
  context: DeploymentContext;
  applicationId: string;
  environmentId: string;
}) {
  const navigate = useNavigate();
  const { workspaceId, data, credentialOptions } = context;
  const resolved = resolveEnvironment(data, applicationId, environmentId);
  if (!resolved.application) return <RequestStateView state="not-found" title="Application not found" />;
  if (!resolved.environment) return <RequestStateView state="not-found" title="Environment not found" />;
  const { application, environment } = resolved;

  const registerEngine = useMutation({
    mutationFn: (values: EngineRegistrationValues) => registerDeploymentEngine(workspaceId, environment.id, engineRegistrationRequest(values)),
    onSuccess: async (engine) => {
      await context.refreshDeploymentCockpit();
      navigate(enginePath(application.id, environment.id, engine.id));
    }
  });

  return (
    <FormPageShell
      title="Register engine"
      description="Add another Elsa workflow engine endpoint to this environment."
      breadcrumbs={[
        { label: "Deployments", to: "/admin/deployments" },
        { label: "Applications", to: "/admin/deployments/applications" },
        { label: application.name, to: applicationPath(application.id) },
        { label: environment.name, to: environmentPath(application.id, environment.id) },
        { label: "Register engine" }
      ]}
    >
      <div className="rounded-ui border border-border bg-surface p-4">
        <EngineRegistrationPanel
          environment={environment}
          credentialOptions={credentialOptions}
          isSubmitting={registerEngine.isPending}
          error={registerEngine.error instanceof Error ? registerEngine.error.message : undefined}
          onCancel={() => navigate(environmentPath(application.id, environment.id))}
          onSubmit={(values) => registerEngine.mutate(values)}
        />
      </div>
    </FormPageShell>
  );
}

export function DeploymentEnginePage() {
  const context = useDeploymentContext();
  const { applicationId = "", environmentId = "", engineId = "" } = useParams();
  if (context.status !== "ready") return context.state;
  return <DeploymentEngineReady context={context.value} applicationId={applicationId} environmentId={environmentId} engineId={engineId} />;
}

function DeploymentEngineReady({
  context,
  applicationId,
  environmentId,
  engineId
}: {
  context: DeploymentContext;
  applicationId: string;
  environmentId: string;
  engineId: string;
}) {
  const resolved = resolveEnvironment(context.data, applicationId, environmentId);
  if (!resolved.application) return <RequestStateView state="not-found" title="Application not found" />;
  if (!resolved.environment) return <RequestStateView state="not-found" title="Environment not found" />;
  const engine = context.data.engines.find((item) => item.id === engineId && item.environmentId === resolved.environment?.id);
  if (!engine) return <RequestStateView state="not-found" title="Engine not found" />;

  return <DeploymentEngineDetailReady context={context} application={resolved.application} environment={resolved.environment} engine={engine} />;
}

function DeploymentEngineDetailReady({
  context,
  application,
  environment,
  engine
}: {
  context: DeploymentContext;
  application: DeploymentCockpit["applications"][number];
  environment: EnvironmentSummary;
  engine: WorkflowEngineRegistration;
}) {
  const [operationNotice, setOperationNotice] = useState("");
  const verifyEngine = useMutation({
    mutationFn: () => verifyDeploymentEngine(context.workspaceId, engine.id),
    onSuccess: (result) => {
      setOperationNotice(result.message);
      void context.refreshDeploymentCockpit();
    }
  });
  const runControl = useMutation({
    mutationFn: async (control: RuntimeControl) => {
      const confirmation = await createActionConfirmation(context.workspaceId, {
        actionType: "RuntimeControl",
        targetId: `${engine.id}:${control.id}`,
        lifetimeSeconds: null
      });
      return runRuntimeControl(context.workspaceId, engine.id, control.id, { confirmationId: confirmation.id });
    },
    onSuccess: (execution) => {
      setOperationNotice(execution.message);
      void context.refreshDeploymentCockpit();
    }
  });

  return (
    <section className="space-y-5">
      <Breadcrumbs
        items={[
          { label: "Deployments", to: "/admin/deployments" },
          { label: "Applications", to: "/admin/deployments/applications" },
          { label: application.name, to: applicationPath(application.id) },
          { label: environment.name, to: environmentPath(application.id, environment.id) },
          { label: engine.name }
        ]}
      />
      <PageHeader title={engine.name} description={`${environment.name} workflow engine registration`} />
      <EngineDetailSection
        application={application}
        environment={environment}
        engine={engine}
        operationNotice={operationNotice}
        canManageSetup={context.canManageSetup}
        canExecuteControls={context.canExecuteControls}
        isVerifying={verifyEngine.isPending}
        verifyError={verifyEngine.error instanceof Error ? verifyEngine.error.message : undefined}
        isRunningControl={runControl.isPending}
        controlError={runControl.error instanceof Error ? runControl.error.message : undefined}
        onVerify={() => verifyEngine.mutate()}
        onRunControl={(control) => runControl.mutate(control)}
      />
    </section>
  );
}

export function DeploymentEngineEditPage() {
  const context = useDeploymentContext();
  const { applicationId = "", environmentId = "", engineId = "" } = useParams();
  if (context.status !== "ready") return context.state;
  return <DeploymentEngineEditReady context={context.value} applicationId={applicationId} environmentId={environmentId} engineId={engineId} />;
}

function DeploymentEngineEditReady({
  context,
  applicationId,
  environmentId,
  engineId
}: {
  context: DeploymentContext;
  applicationId: string;
  environmentId: string;
  engineId: string;
}) {
  const navigate = useNavigate();
  const { workspaceId, data, credentialOptions } = context;
  const resolved = resolveEnvironment(data, applicationId, environmentId);
  if (!resolved.application) return <RequestStateView state="not-found" title="Application not found" />;
  if (!resolved.environment) return <RequestStateView state="not-found" title="Environment not found" />;
  const { application, environment } = resolved;
  const engine = data.engines.find((item) => item.id === engineId && item.environmentId === environment.id);
  if (!engine) return <RequestStateView state="not-found" title="Engine not found" />;

  const updateEngine = useMutation({
    mutationFn: (nextEngine: WorkflowEngineRegistration) =>
      updateDeploymentEngine(workspaceId, nextEngine.id, {
        name: nextEngine.name,
        baseUrl: nextEngine.endpoint.baseUrl,
        region: nextEngine.endpoint.region || null,
        credentialProvider: nextEngine.credentialReference.provider,
        credentialReference: nextEngine.credentialReference.reference,
        capabilities: nextEngine.capabilities,
        controls: nextEngine.controls,
        hostingProvider: nextEngine.hostingProvider
    }),
    onSuccess: async () => {
      await context.refreshDeploymentCockpit();
      navigate(enginePath(application.id, environment.id, engine.id));
    }
  });

  return (
    <FormPageShell
      title="Edit engine"
      description="Update endpoint and credential-reference metadata for this engine registration."
      breadcrumbs={[
        { label: "Deployments", to: "/admin/deployments" },
        { label: "Applications", to: "/admin/deployments/applications" },
        { label: application.name, to: applicationPath(application.id) },
        { label: environment.name, to: environmentPath(application.id, environment.id) },
        { label: engine.name, to: enginePath(application.id, environment.id, engine.id) },
        { label: "Edit" }
      ]}
    >
      <EngineEditPanel
        engine={engine}
        credentialOptions={credentialOptions}
        isSubmitting={updateEngine.isPending}
        error={updateEngine.error instanceof Error ? updateEngine.error.message : undefined}
        onCancel={() => navigate(enginePath(application.id, environment.id, engine.id))}
        onSubmit={(values) => updateEngine.mutate(values)}
      />
    </FormPageShell>
  );
}

type DeploymentContextResult = { status: "ready"; value: DeploymentContext } | { status: "state"; state: ReactNode };

function useDeploymentContext(): DeploymentContextResult {
  const queryClient = useQueryClient();
  const workspaceContext = useWorkspaceContext();
  const workspaceId = workspaceContext.selectedWorkspaceId;
  const permissions = useQuery({
    queryKey: queryKeys.deploymentPermissions(workspaceId),
    queryFn: () => getDeploymentPermissions(workspaceId),
    enabled: Boolean(workspaceId)
  });
  const cockpit = useQuery({
    queryKey: queryKeys.deploymentCockpit(workspaceId),
    queryFn: () => getDeploymentCockpit(workspaceId),
    enabled: Boolean(workspaceId),
    refetchInterval: 5_000
  });
  const tiers = useQuery({
    queryKey: queryKeys.deploymentTiers(workspaceId),
    queryFn: () => getDeploymentTiers(workspaceId),
    enabled: Boolean(workspaceId)
  });

  if (workspaceContext.isLoading || cockpit.isLoading) return { status: "state", state: <RequestStateView state="loading" title="Loading deployments" /> };
  if (workspaceContext.isError) return { status: "state", state: <RequestStateView state="unexpected" title="Workspace context could not load" /> };
  if (!workspaceId) {
    return { status: "state", state: <RequestStateView state="empty" title="No workspace selected" description="Select an organization workspace to view deployments." /> };
  }
  if (cockpit.isError || !cockpit.data) return { status: "state", state: <RequestStateView state="unexpected" title="Deployments could not load" /> };

  const deploymentTiers = tiers.data?.tiers ?? [];
  return {
    status: "ready",
    value: {
      workspaceId,
      data: cockpit.data,
      tiers: deploymentTiers,
      activeTiers: deploymentTiers.filter((tier) => tier.status === "Active"),
      credentialOptions: credentialReferenceOptions(cockpit.data.engines),
      canManageSetup: Boolean(permissions.data?.permissions.includes("deployments.setup.manage")),
      canManageDesiredState: Boolean(permissions.data?.permissions.includes("deployments.desired-state.manage")),
      canPreviewPromotion: Boolean(permissions.data?.permissions.includes("deployments.promotion.preview")),
      canExecuteDeployment: Boolean(permissions.data?.permissions.includes("deployments.run.execute")),
      canExecuteRollback: Boolean(permissions.data?.permissions.includes("deployments.rollback.execute")),
      canExecuteControls: Boolean(permissions.data?.permissions.includes("deployments.controls.execute")),
      refreshDeploymentCockpit: () => queryClient.invalidateQueries({ queryKey: queryKeys.deploymentCockpit(workspaceId) })
    }
  };
}

function ApplicationTable({ applications, data }: { applications: DeploymentCockpit["applications"]; data: DeploymentCockpit }) {
  return (
    <Table>
      <table className="min-w-full divide-y divide-border text-sm">
        <thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground">
          <tr>
            <th className="px-3 py-2">Application</th>
            <th className="px-3 py-2">Workspace</th>
            <th className="px-3 py-2">Health</th>
            <th className="px-3 py-2">Environments</th>
            <th className="px-3 py-2">Engines</th>
            <th className="px-3 py-2">Healthy</th>
            <th className="px-3 py-2">Drift</th>
            <th className="px-3 py-2">Actions</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {applications.map((application) => {
            const engines = enginesForApplication(data, application);
            const driftCount = application.environments.filter((environment) => environment.driftStatus === "DriftDetected").length;
            return (
              <tr key={application.id}>
                <td className="px-3 py-3 font-medium"><Link to={applicationPath(application.id)}>{application.name}</Link></td>
                <td className="px-3 py-3 text-muted-foreground">{application.workspaceName}</td>
                <td className="px-3 py-3"><StatusBadge value={summarizeApplicationHealth(application, data.engines)} tone={applicationHealthTone(application, data.engines)} /></td>
                <td className="px-3 py-3">{application.environments.length}</td>
                <td className="px-3 py-3">{engines.length}</td>
                <td className="px-3 py-3">{engines.filter((engine) => engine.health === "Healthy").length}</td>
                <td className="px-3 py-3">{driftCount}</td>
                <td className="px-3 py-3"><Link to={applicationPath(application.id)} className="text-xs font-medium text-primary hover:underline">Open</Link></td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </Table>
  );
}

function EnvironmentTable({
  application,
  environments,
  data
}: {
  application: DeploymentCockpit["applications"][number];
  environments: EnvironmentSummary[];
  data: DeploymentCockpit;
}) {
  return (
    <Table>
      <table className="min-w-full divide-y divide-border text-sm">
        <thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground">
          <tr>
            <th className="px-3 py-2">Environment</th>
            <th className="px-3 py-2">Tier</th>
            <th className="px-3 py-2">Health</th>
            <th className="px-3 py-2">Deployment</th>
            <th className="px-3 py-2">Desired</th>
            <th className="px-3 py-2">Deployed</th>
            <th className="px-3 py-2">Engines</th>
            <th className="px-3 py-2">Drift</th>
            <th className="px-3 py-2">Actions</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {environments.map((environment) => {
            const engines = enginesForEnvironment(data, environment.id);
            return (
              <tr key={environment.id}>
                <td className="px-3 py-3 font-medium"><Link to={environmentPath(application.id, environment.id)}>{environment.name}</Link></td>
                <td className="px-3 py-3 text-muted-foreground">
                  {environment.tierName || environment.tier}
                  {environment.tierStatus === "Archived" ? " (archived)" : ""}
                </td>
                <td className="px-3 py-3"><StatusBadge value={environment.health} tone={healthTone(environment.health)} /></td>
                <td className="px-3 py-3"><StatusBadge value={environment.deploymentStatus} tone={deploymentTone(environment.deploymentStatus)} /></td>
                <td className="px-3 py-3">r{environment.desiredRevision.revision}</td>
                <td className="px-3 py-3">{environment.deployedRevision ? `r${environment.deployedRevision}` : "-"}</td>
                <td className="px-3 py-3">{engines.length}</td>
                <td className="px-3 py-3">{driftLabel(environment.driftStatus)}</td>
                <td className="px-3 py-3"><Link to={environmentPath(application.id, environment.id)} className="text-xs font-medium text-primary hover:underline">Open</Link></td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </Table>
  );
}

function EngineTable({
  applicationId,
  environmentId,
  engines
}: {
  applicationId: string;
  environmentId: string;
  engines: WorkflowEngineRegistration[];
}) {
  return (
    <Table>
      <table className="min-w-full divide-y divide-border text-sm">
        <thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground">
          <tr>
            <th className="px-3 py-2">Engine</th>
            <th className="px-3 py-2">Endpoint</th>
            <th className="px-3 py-2">Health</th>
            <th className="px-3 py-2">Credential</th>
            <th className="px-3 py-2">Capabilities</th>
            <th className="px-3 py-2">Controls</th>
            <th className="px-3 py-2">Last heartbeat</th>
            <th className="px-3 py-2">Actions</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {engines.map((engine) => (
            <tr key={engine.id}>
              <td className="px-3 py-3 font-medium"><Link to={enginePath(applicationId, environmentId, engine.id)}>{engine.name}</Link></td>
              <td className="max-w-xs truncate px-3 py-3 text-muted-foreground">{engine.endpoint.baseUrl}</td>
              <td className="px-3 py-3"><StatusBadge value={engine.health} tone={healthTone(engine.health)} /></td>
              <td className="px-3 py-3">{engine.credentialReference.verificationStatus}</td>
              <td className="px-3 py-3">{engine.capabilities.length}</td>
              <td className="px-3 py-3">{engine.controls.length}</td>
              <td className="px-3 py-3">{formatDateTime(engine.lastHeartbeatAt)}</td>
              <td className="px-3 py-3"><Link to={enginePath(applicationId, environmentId, engine.id)} className="text-xs font-medium text-primary hover:underline">Open</Link></td>
            </tr>
          ))}
        </tbody>
      </table>
    </Table>
  );
}

function EngineDetailSection({
  application,
  environment,
  engine,
  operationNotice,
  canManageSetup,
  canExecuteControls,
  isVerifying,
  verifyError,
  isRunningControl,
  controlError,
  onVerify,
  onRunControl
}: {
  application: DeploymentCockpit["applications"][number];
  environment: EnvironmentSummary;
  engine: WorkflowEngineRegistration;
  operationNotice: string;
  canManageSetup: boolean;
  canExecuteControls: boolean;
  isVerifying: boolean;
  verifyError?: string;
  isRunningControl: boolean;
  controlError?: string;
  onVerify: () => void;
  onRunControl: (control: RuntimeControl) => void;
}) {
  return (
    <section className="space-y-3">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <h2 className="text-sm font-semibold">Engine details</h2>
        <div className="flex flex-wrap gap-2">
          <SecondaryButton disabled={!canManageSetup || isVerifying} onClick={onVerify}>
            <RefreshCw className="h-4 w-4" />
            {isVerifying ? "Verifying" : "Verify"}
          </SecondaryButton>
          <Link to={`${enginePath(application.id, environment.id, engine.id)}/edit`} className={buttonClassName("secondary", !canManageSetup ? "pointer-events-none opacity-50" : undefined)} aria-disabled={!canManageSetup}>
            <Pencil className="h-4 w-4" />
            Edit engine
          </Link>
        </div>
      </div>
      <div className="grid gap-3 lg:grid-cols-[1.2fr_1fr]">
        <Panel title={engine.name} icon={<RadioTower className="h-4 w-4" />}>
          <dl className="grid gap-3 sm:grid-cols-2">
            <Detail label="Endpoint" value={engine.endpoint.baseUrl} />
            <Detail label="Region" value={engine.endpoint.region} />
            <Detail label="Version" value={engine.endpoint.version} />
            <Detail label="Certificate" value={engine.endpoint.certificateStatus} />
            <Detail label="Health" value={<StatusBadge value={engine.health} tone={healthTone(engine.health)} />} />
            <Detail label="Last heartbeat" value={formatDateTime(engine.lastHeartbeatAt)} />
            <Detail label="Last verification" value={formatDateTime(engine.lastVerificationAt)} />
            <Detail label="Diagnostic" value={engine.verificationMessage || "No verification has run yet."} />
          </dl>
        </Panel>
        <Panel title="Credential reference" icon={<KeyRound className="h-4 w-4" />}>
          <dl className="space-y-3">
            <Detail label="Provider" value={engine.credentialReference.provider} />
            <Detail label="Reference" value={engine.credentialReference.reference} />
            <Detail label="Verification" value={<StatusBadge value={engine.credentialReference.verificationStatus} tone={credentialTone(engine.credentialReference.verificationStatus)} />} />
            <Detail label="Last verified" value={formatDateTime(engine.credentialReference.lastVerifiedAt)} />
          </dl>
        </Panel>
      </div>
      {verifyError ? <div role="alert" className="rounded-ui border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">{verifyError}</div> : null}
      <div className="grid gap-3 lg:grid-cols-2">
        <Panel title="Advertised capabilities" icon={<ClipboardCheck className="h-4 w-4" />}>
          <div className="flex flex-wrap gap-2">
            {engine.capabilities.map((capability) => (
              <Badge key={capability.id} className="gap-1">
                <span className="font-medium">{capability.label}</span>
                <span className="text-muted-foreground">{capability.boundary}</span>
              </Badge>
            ))}
          </div>
        </Panel>
        <Panel title="Supported controls" icon={<RefreshCw className="h-4 w-4" />}>
          <RuntimeControlsPanel
            engine={engine}
            canExecuteControls={canExecuteControls}
            isRunning={isRunningControl}
            notice={operationNotice}
            error={controlError}
            onRunControl={onRunControl}
          />
        </Panel>
      </div>
    </section>
  );
}

function EnvironmentOperations({ data, environment }: { data: DeploymentCockpit; environment: EnvironmentSummary }) {
  const driftItems = data.driftReport.filter((item) => item.environmentId === environment.id);
  const environmentHistory = data.history.filter((item) => item.environmentId === environment.id);
  const scopedData = { ...data, history: environmentHistory, driftReport: driftItems };

  return (
    <div className="grid gap-4 xl:grid-cols-2">
      <Panel title="Observability" icon={<Activity className="h-4 w-4" />}>
        {data.observabilityBindings.length === 0 ? (
          <p className="rounded-ui border border-dashed border-border px-3 py-6 text-center text-sm text-muted-foreground">
            No observability metadata has been recorded.
          </p>
        ) : (
          <div className="grid gap-3 md:grid-cols-2">
            {data.observabilityBindings.map((binding) => (
              <div key={binding.id} className="rounded-ui border border-border p-3 text-sm">
                <div className="flex items-center justify-between gap-2">
                  <span className="font-medium">{binding.kind}</span>
                  <StatusBadge value={binding.status} tone={binding.status === "Connected" ? "success" : binding.status === "Degraded" ? "warning" : "destructive"} />
                </div>
                <p className="mt-2 text-muted-foreground">{binding.provider}</p>
                <p className="mt-1 text-xs text-muted-foreground">{binding.scope}</p>
                <p className="mt-1 text-xs text-muted-foreground">Revision r{binding.correlatedRevision} · {binding.sample}</p>
              </div>
            ))}
          </div>
        )}
      </Panel>
      <Panel title="Drift report" icon={<AlertTriangle className="h-4 w-4" />}>
        {driftItems.length === 0 ? (
          <p className="rounded-ui border border-dashed border-border px-3 py-6 text-center text-sm text-muted-foreground">
            No drift metadata has been recorded for this environment.
          </p>
        ) : (
          <div className="space-y-2">
            {driftItems.map((item) => (
              <div key={item.id} className="rounded-ui border border-border p-3 text-sm">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-medium">{item.area}</span>
                  <StatusBadge value={item.action} tone={item.action === "Redeploy" ? "warning" : "neutral"} />
                </div>
                <div className="mt-2 grid gap-2 text-xs text-muted-foreground sm:grid-cols-2">
                  <div>Desired: {item.desired}</div>
                  <div>Observed: {item.observed}</div>
                </div>
                <div className="mt-2 text-xs text-muted-foreground">{engineLabel(item.engineId, data.engines)}</div>
              </div>
            ))}
          </div>
        )}
      </Panel>
      <div className="xl:col-span-2">
        <DeploymentRunsPanel data={scopedData} />
      </div>
    </div>
  );
}

function ApplicationEditPanel({
  application,
  isSubmitting,
  error,
  onCancel,
  onSubmit
}: {
  application: DeploymentCockpit["applications"][number];
  isSubmitting: boolean;
  error?: string;
  onCancel: () => void;
  onSubmit: (name: string) => void;
}) {
  const [name, setName] = useState(application.name);
  const canSubmit = name.trim().length > 0 && name !== application.name;

  return (
    <form
      className="rounded-ui border border-border bg-surface p-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (canSubmit) onSubmit(name.trim());
      }}
    >
      <div className="grid gap-3 md:grid-cols-[1fr_auto] md:items-end">
        <label className="text-sm font-medium">
          Application name
          <Input className="mt-1" value={name} onChange={(event) => setName(event.target.value)} />
        </label>
        <div className="flex gap-2">
          <Button type="submit" disabled={!canSubmit || isSubmitting}>
            <Save className="h-4 w-4" />
            Save
          </Button>
          <SecondaryButton type="button" onClick={onCancel}>Cancel</SecondaryButton>
        </div>
      </div>
      {error ? <p className="mt-3 text-sm text-destructive">{error}</p> : null}
    </form>
  );
}

function EnvironmentForm({
  environment,
  tiers,
  isSubmitting,
  error,
  onCancel,
  onSubmit
}: {
  environment: EnvironmentSummary;
  tiers: WorkspaceDeploymentTier[];
  isSubmitting: boolean;
  error?: string;
  onCancel: () => void;
  onSubmit: (values: EnvironmentFormValues) => void;
}) {
  const initialTierId = environment.tierId ?? tiers.find((tier) => tier.name === (environment.tierName ?? environment.tier))?.id ?? "";
  const [name, setName] = useState(environment.name);
  const [tierId, setTierId] = useState(initialTierId);
  const selectedTier = tiers.find((tier) => tier.id === tierId);
  const canSubmit = name.trim().length > 0 && (name !== environment.name || tierId !== initialTierId);

  return (
    <form
      className="rounded-ui border border-border bg-surface p-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (!canSubmit) return;
        onSubmit({
          name: name.trim(),
          tierId: tierId || null,
          tier: legacyTierFromName(selectedTier?.name ?? environment.tierName)
        });
      }}
    >
      <div className="grid gap-3 md:grid-cols-2">
        <label className="text-sm font-medium">
          Environment
          <Input className="mt-1" value={name} onChange={(event) => setName(event.target.value)} />
        </label>
        <label className="text-sm font-medium">
          Tier
          <Select className="mt-1 w-full" value={tierId} onChange={(event) => setTierId(event.target.value)}>
            <option value="" disabled>Select a tier</option>
            {tiers.map((tier) => (
              <option key={tier.id} value={tier.id}>{tier.name}{tier.status === "Archived" ? " (archived)" : ""}</option>
            ))}
          </Select>
        </label>
      </div>
      <div className="mt-4 flex gap-2">
        <Button type="submit" disabled={!canSubmit || isSubmitting}>
          <Save className="h-4 w-4" />
          Save
        </Button>
        <SecondaryButton type="button" onClick={onCancel}>Cancel</SecondaryButton>
      </div>
      {error ? <p className="mt-3 text-sm text-destructive">{error}</p> : null}
    </form>
  );
}

function EngineRegistrationPanel({
  environment,
  credentialOptions,
  isSubmitting,
  error,
  onCancel,
  onSubmit
}: {
  environment: EnvironmentSummary;
  credentialOptions: CredentialReferenceOption[];
  isSubmitting: boolean;
  error?: string;
  onCancel: () => void;
  onSubmit: (values: EngineRegistrationValues) => void;
}) {
  const [values, setValues] = useState<EngineRegistrationValues>({
    engineName: "",
    baseUrl: "",
    credentialProvider: credentialOptions[0]?.provider ?? "External secret store",
    credentialReference: ""
  });
  const canSubmit =
    values.engineName.trim().length > 0 &&
    values.baseUrl.trim().length > 0 &&
    values.credentialProvider.trim().length > 0 &&
    values.credentialReference.trim().length > 0;

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        if (!canSubmit) return;
        onSubmit({
          engineName: values.engineName.trim(),
          baseUrl: values.baseUrl.trim(),
          credentialProvider: values.credentialProvider.trim(),
          credentialReference: values.credentialReference.trim()
        });
      }}
    >
      <div className="mb-3 flex flex-col gap-1">
        <h2 className="text-sm font-semibold">Register engine for {environment.name}</h2>
        <p className="text-xs text-muted-foreground">Add another Elsa workflow engine endpoint to this environment.</p>
      </div>
      <div className="grid gap-3 md:grid-cols-2">
        <label className="text-sm font-medium">
          Engine
          <Input className="mt-1" value={values.engineName} onChange={(event) => setValues((current) => ({ ...current, engineName: event.target.value }))} />
        </label>
        <label className="text-sm font-medium">
          Base URL
          <Input className="mt-1" value={values.baseUrl} onChange={(event) => setValues((current) => ({ ...current, baseUrl: event.target.value }))} />
        </label>
        <label className="text-sm font-medium">
          Credential provider
          <Input className="mt-1" value={values.credentialProvider} onChange={(event) => setValues((current) => ({ ...current, credentialProvider: event.target.value }))} />
        </label>
        <label className="text-sm font-medium">
          Credential reference
          <CredentialReferenceInput
            className="mt-1"
            value={values.credentialReference}
            options={credentialOptions}
            onChange={(reference) => {
              const option = credentialOptions.find((item) => item.reference === reference);
              setValues((current) => ({
                ...current,
                credentialProvider: option?.provider ?? current.credentialProvider,
                credentialReference: reference
              }));
            }}
          />
        </label>
      </div>
      {error ? <p className="mt-3 text-sm text-destructive">{error}</p> : null}
      <div className="mt-4 flex gap-2">
        <Button type="submit" disabled={!canSubmit || isSubmitting}>
          <Plus className="h-4 w-4" />
          Register engine
        </Button>
        <SecondaryButton type="button" onClick={onCancel}>Cancel</SecondaryButton>
      </div>
    </form>
  );
}

function EngineEditPanel({
  engine,
  credentialOptions,
  isSubmitting,
  error,
  onCancel,
  onSubmit
}: {
  engine: WorkflowEngineRegistration;
  credentialOptions: CredentialReferenceOption[];
  isSubmitting: boolean;
  error?: string;
  onCancel: () => void;
  onSubmit: (engine: WorkflowEngineRegistration) => void;
}) {
  const [name, setName] = useState(engine.name);
  const [baseUrl, setBaseUrl] = useState(engine.endpoint.baseUrl);
  const [region, setRegion] = useState(engine.endpoint.region);
  const [credentialProvider, setCredentialProvider] = useState(engine.credentialReference.provider);
  const [credentialReference, setCredentialReference] = useState(engine.credentialReference.reference);
  const canSubmit =
    name.trim().length > 0 &&
    baseUrl.trim().length > 0 &&
    credentialProvider.trim().length > 0 &&
    credentialReference.trim().length > 0 &&
    (name !== engine.name ||
      baseUrl !== engine.endpoint.baseUrl ||
      region !== engine.endpoint.region ||
      credentialProvider !== engine.credentialReference.provider ||
      credentialReference !== engine.credentialReference.reference);

  return (
    <form
      className="rounded-ui border border-border bg-surface p-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (!canSubmit) return;
        onSubmit({
          ...engine,
          name: name.trim(),
          endpoint: { ...engine.endpoint, baseUrl: baseUrl.trim(), region: region.trim() },
          credentialReference: { ...engine.credentialReference, provider: credentialProvider.trim(), reference: credentialReference.trim() }
        });
      }}
    >
      <div className="grid gap-3 md:grid-cols-2">
        <label className="text-sm font-medium">
          Engine
          <Input className="mt-1" value={name} onChange={(event) => setName(event.target.value)} />
        </label>
        <label className="text-sm font-medium">
          Base URL
          <Input className="mt-1" value={baseUrl} onChange={(event) => setBaseUrl(event.target.value)} />
        </label>
        <label className="text-sm font-medium">
          Region
          <Input className="mt-1" value={region} onChange={(event) => setRegion(event.target.value)} />
        </label>
        <label className="text-sm font-medium">
          Credential provider
          <Input className="mt-1" value={credentialProvider} onChange={(event) => setCredentialProvider(event.target.value)} />
        </label>
        <label className="text-sm font-medium">
          Credential reference
          <CredentialReferenceInput
            className="mt-1"
            value={credentialReference}
            options={credentialOptions}
            onChange={(reference) => {
              const option = credentialOptions.find((item) => item.reference === reference);
              setCredentialReference(reference);
              if (option) setCredentialProvider(option.provider);
            }}
          />
        </label>
      </div>
      {error ? <p className="mt-3 text-sm text-destructive">{error}</p> : null}
      <div className="mt-4 flex gap-2">
        <Button type="submit" disabled={!canSubmit || isSubmitting}>
          <Save className="h-4 w-4" />
          Save
        </Button>
        <SecondaryButton type="button" onClick={onCancel}>Cancel</SecondaryButton>
      </div>
    </form>
  );
}

function AssistantPlanView({
  data,
  targetEnvironmentId,
  outcome,
  onApprove,
  onReject
}: {
  data: DeploymentCockpit;
  targetEnvironmentId: string;
  outcome: "Proposed" | "Approved" | "Rejected";
  onApprove: () => void;
  onReject: () => void;
}) {
  const plan = data.assistantPlans.find((item) => item.targetEnvironmentId === targetEnvironmentId) ?? data.assistantPlans[0];
  if (!plan) {
    return (
      <RequestStateView
        state="empty"
        title="No assistant plan available"
        description="Assistant review will appear after a deployment plan is generated for this environment."
      />
    );
  }

  const displayedStatus = outcome === "Proposed" ? plan.status : outcome;
  const blocked = hasBlockingValidation(plan.validations);

  return (
    <div className="grid gap-4 xl:grid-cols-[1.2fr_1fr]">
      <Panel title={`Assistant plan ${plan.id} v${plan.version}`} icon={<Bot className="h-4 w-4" />}>
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

function DeploymentBlockersPanel({
  blockers,
  canManageDesiredState
}: {
  blockers: DeploymentBlocker[];
  canManageDesiredState: boolean;
}) {
  return (
    <Panel title="Deployment blockers" icon={<AlertTriangle className="h-4 w-4" />}>
      <div className="space-y-2">
        {blockers.map((blocker) => (
          <div key={blocker.id} className="flex gap-2 rounded-ui border border-border bg-background p-3 text-sm">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-destructive" />
            <div>
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-medium">{blocker.scope}</span>
                <Badge>{blocker.source}</Badge>
                <StatusBadge value={blocker.severity} tone={validationTone(blocker.severity)} />
              </div>
              <p className="mt-1 text-muted-foreground">{blocker.message}</p>
              {blocker.validationId === "deployment.tier.observability-required" ? (
                <div className="mt-3 space-y-3 rounded-ui border border-border bg-muted/30 p-3 text-xs text-muted-foreground">
                  <p>
                    Production promotion requires the source revision to declare where runtime telemetry will be sent.
                    Add at least one logs, metrics, traces, or console binding with a provider and scope.
                  </p>
                  {blocker.actionPath ? (
                    <Link
                      to={blocker.actionPath}
                      className={buttonClassName("secondary", !canManageDesiredState ? "pointer-events-none opacity-50" : undefined)}
                      aria-disabled={!canManageDesiredState}
                    >
                      <GitBranch className="h-4 w-4" />
                      {blocker.actionLabel ?? "Add binding to new revision"}
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

function FormPageShell({
  title,
  description,
  breadcrumbs,
  children
}: {
  title: string;
  description: string;
  breadcrumbs: Array<{ label: string; to?: string }>;
  children: ReactNode;
}) {
  const backTarget = [...breadcrumbs].reverse().find((item) => item.to)?.to ?? "/admin/deployments";

  return (
    <section className="mx-auto max-w-5xl space-y-5">
      <Breadcrumbs items={breadcrumbs} />
      <PageHeader
        title={title}
        description={description}
        actions={
          <Link to={backTarget} className={buttonClassName("secondary")}>
            <ArrowLeft className="h-4 w-4" />
            Back
          </Link>
        }
      />
      {children}
    </section>
  );
}

function PageHeader({ title, description, actions }: { title: string; description: string; actions?: ReactNode }) {
  return (
    <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
      <div>
        <h1 className="text-xl font-semibold">{title}</h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">{description}</p>
      </div>
      {actions ? <div className="flex flex-wrap gap-2">{actions}</div> : null}
    </div>
  );
}

function SectionHeader({ title, description }: { title: string; description: string }) {
  return (
    <div>
      <h2 className="text-sm font-semibold">{title}</h2>
      <p className="mt-1 text-xs text-muted-foreground">{description}</p>
    </div>
  );
}

function Breadcrumbs({ items }: { items: Array<{ label: string; to?: string }> }) {
  return (
    <nav aria-label="Breadcrumb" className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
      {items.map((item, index) => (
        <span key={`${item.label}:${index}`} className="flex items-center gap-2">
          {item.to ? <Link to={item.to} className="hover:text-foreground">{item.label}</Link> : <span className="text-foreground">{item.label}</span>}
          {index < items.length - 1 ? <span>/</span> : null}
        </span>
      ))}
    </nav>
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

function MetricCard({ label, value, tone }: { label: string; value: string; tone?: StatusTone }) {
  return (
    <div className="rounded-ui border border-border bg-surface p-3">
      <div className={cn("text-2xl font-semibold", tone ? statusTextClass(tone) : "")}>{value}</div>
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

function applicationPath(applicationId: string) {
  return `/admin/deployments/applications/${encodeURIComponent(applicationId)}`;
}

function environmentPath(applicationId: string, environmentId: string) {
  return `${applicationPath(applicationId)}/environments/${encodeURIComponent(environmentId)}`;
}

function newRevisionPathForEnvironment(data: DeploymentCockpit, environmentId: string, query?: string) {
  const application = data.applications.find((item) => item.environments.some((environment) => environment.id === environmentId));
  if (!application) return undefined;
  const path = `${environmentPath(application.id, environmentId)}/revisions/new`;
  return query ? `${path}?${query}` : path;
}

function enginePath(applicationId: string, environmentId: string, engineId: string) {
  return `${environmentPath(applicationId, environmentId)}/engines/${encodeURIComponent(engineId)}`;
}

function artifactDisplayName(artifact: WorkspaceArtifact) {
  const name = artifact.displayMetadata?.name ?? artifact.manifest.name ?? artifact.artifactId;
  const version = artifact.displayMetadata?.version ?? artifact.manifest.version;
  return version ? `${name} ${version}` : name;
}

function artifactRevisionRecord(artifact: WorkspaceArtifact): WorkspaceDesiredStateRecordRequest {
  return {
    kind: "ArtifactReference",
    name: artifactDisplayName(artifact),
    payload: {
      artifactRecordId: artifact.id,
      artifactId: artifact.artifactId,
      artifactTypeId: artifact.artifactTypeId ?? "elsa.workflow-definition",
      contentDigest: artifact.contentDigest,
      metadata: artifact.displayMetadata?.labels ?? {}
    }
  };
}

function observabilityRevisionRecord({
  kind,
  provider,
  scope,
  sample
}: {
  kind: ObservabilityBinding["kind"];
  provider: string;
  scope: string;
  sample: string;
}): WorkspaceDesiredStateRecordRequest {
  return {
    kind: "ObservabilityBinding",
    name: `${kind} - ${provider.trim()}`,
    payload: {
      kind,
      provider: provider.trim(),
      scope: scope.trim(),
      sample: sample.trim() || null
    }
  };
}

function findApplication(data: DeploymentCockpit, applicationId: string) {
  return data.applications.find((application) => application.id === applicationId);
}

function resolveEnvironment(data: DeploymentCockpit, applicationId: string, environmentId: string) {
  const application = findApplication(data, applicationId);
  const environment = application?.environments.find((item) => item.id === environmentId);
  return { application, environment };
}

function enginesForApplication(data: DeploymentCockpit, application: DeploymentCockpit["applications"][number]) {
  const environmentIds = new Set(application.environments.map((environment) => environment.id));
  return data.engines.filter((engine) => environmentIds.has(engine.environmentId));
}

function enginesForEnvironment(data: DeploymentCockpit, environmentId: string) {
  return data.engines.filter((engine) => engine.environmentId === environmentId);
}

function filterApplications(applications: DeploymentCockpit["applications"], data: DeploymentCockpit, query: string) {
  const term = query.trim().toLowerCase();
  if (!term) return applications;
  return applications.filter((application) => {
    const engines = enginesForApplication(data, application);
    const haystack = [
      application.name,
      application.workspaceName,
      summarizeApplicationHealth(application, data.engines),
      ...application.environments.map((environment) => `${environment.name} ${environment.tierName ?? environment.tier}`),
      ...engines.map((engine) => engine.name)
    ].join(" ").toLowerCase();
    return haystack.includes(term);
  });
}

function sortApplications(applications: DeploymentCockpit["applications"], data: DeploymentCockpit, sort: ApplicationSort) {
  return [...applications].sort((left, right) => {
    const leftEngines = enginesForApplication(data, left);
    const rightEngines = enginesForApplication(data, right);
    switch (sort) {
      case "health":
        return compareText(summarizeApplicationHealth(left, data.engines), summarizeApplicationHealth(right, data.engines));
      case "environments":
        return compareNumber(right.environments.length, left.environments.length);
      case "engines":
        return compareNumber(rightEngines.length, leftEngines.length);
      case "drift":
        return compareNumber(
          right.environments.filter((environment) => environment.driftStatus === "DriftDetected").length,
          left.environments.filter((environment) => environment.driftStatus === "DriftDetected").length
        );
      default:
        return compareText(left.name, right.name);
    }
  });
}

function filterEnvironments(environments: EnvironmentSummary[], query: string) {
  const term = query.trim().toLowerCase();
  if (!term) return environments;
  return environments.filter((environment) =>
    [
      environment.name,
      environment.tierName ?? environment.tier,
      environment.health,
      environment.deploymentStatus,
      driftLabel(environment.driftStatus),
      ...(environment.tierCapabilities ?? [])
    ].join(" ").toLowerCase().includes(term)
  );
}

function sortEnvironments(environments: EnvironmentSummary[], sort: EnvironmentSort) {
  return [...environments].sort((left, right) => {
    switch (sort) {
      case "tier":
        return compareText(left.tierName ?? left.tier, right.tierName ?? right.tier);
      case "health":
        return compareText(left.health, right.health);
      case "deployment":
        return compareText(left.deploymentStatus, right.deploymentStatus);
      case "drift":
        return compareText(driftLabel(left.driftStatus), driftLabel(right.driftStatus));
      default:
        return compareText(left.name, right.name);
    }
  });
}

function filterEngines(engines: WorkflowEngineRegistration[], query: string) {
  const term = query.trim().toLowerCase();
  if (!term) return engines;
  return engines.filter((engine) =>
    [
      engine.name,
      engine.endpoint.baseUrl,
      engine.endpoint.region,
      engine.health,
      engine.credentialReference.provider,
      engine.credentialReference.reference,
      engine.credentialReference.verificationStatus,
      engine.hostingProvider ?? "",
      ...engine.capabilities.map((capability) => `${capability.label} ${capability.boundary}`),
      ...engine.controls.map((control) => `${control.label} ${control.boundary}`)
    ].join(" ").toLowerCase().includes(term)
  );
}

function sortEngines(engines: WorkflowEngineRegistration[], sort: EngineSort) {
  return [...engines].sort((left, right) => {
    switch (sort) {
      case "health":
        return compareText(left.health, right.health);
      case "verification":
        return compareText(left.credentialReference.verificationStatus, right.credentialReference.verificationStatus);
      case "heartbeat":
        return compareText(right.lastHeartbeatAt, left.lastHeartbeatAt);
      default:
        return compareText(left.name, right.name);
    }
  });
}

function collectPromotionReadinessIssues(
  data: DeploymentCockpit,
  sourceEnvironmentId: string,
  targetEnvironmentId: string,
  canPreview: boolean,
  hasValidArtifacts: boolean
) {
  const issues: PromotionReadinessIssue[] = [];
  const source = findEnvironmentById(data, sourceEnvironmentId);
  const target = findEnvironmentById(data, targetEnvironmentId);
  const targetEngines = data.engines.filter((engine) => engine.environmentId === targetEnvironmentId);

  if (!canPreview) {
    issues.push({
      id: "permission.preview",
      scope: "Permission",
      severity: "Blocker",
      message: "Promotion preview permission is required for live validation."
    });
  }
  if (!source) {
    issues.push({
      id: "source.missing",
      scope: "Source environment",
      severity: "Blocker",
      message: "Choose a source environment before previewing promotion."
    });
  }
  if (!target) {
    issues.push({
      id: "target.missing",
      scope: "Target environment",
      severity: "Blocker",
      message: "Choose a target environment before previewing promotion."
    });
  }
  if (source && target && source.id === target.id) {
    issues.push({
      id: "selection.same-environment",
      scope: "Selection",
      severity: "Blocker",
      message: "Choose different source and target environments."
    });
  }
  if (source && !hasUsableDesiredRevision(source)) {
    const sourceApplication = data.applications.find((application) => application.environments.some((environment) => environment.id === source.id));
    issues.push({
      id: "source.revision-missing",
      scope: "Source revision",
      severity: "Blocker",
      message: `${source.name} does not have a desired-state revision yet. Create or choose a source revision before previewing promotion.`,
      action: hasValidArtifacts && sourceApplication
        ? {
            label: `Create ${source.name} revision`,
            to: `${environmentPath(sourceApplication.id, source.id)}/revisions/new`,
            description: "Use a verified artifact to author the source desired state."
          }
        : {
            label: "Upload artifact",
            to: "/admin/artifacts/new",
            description: "A valid artifact is required before this environment can receive a desired-state revision."
          }
    });
  }
  if (target && targetEngines.length === 0) {
    issues.push({
      id: "target.engine-missing",
      scope: "Target engine",
      severity: "Blocker",
      message: `${target.name} has no registered workflow engine. Register a target engine before previewing promotion.`
    });
  }
  if (source && !hasTierCapability(source, deploymentTierCapabilities.promotionSource)) {
    issues.push({
      id: "source.tier",
      scope: "Source tier",
      severity: "Blocker",
      message: `${source.tierName || source.tier} is not configured as a promotion source.`
    });
  }
  if (target && !hasTierCapability(target, deploymentTierCapabilities.promotionTarget)) {
    issues.push({
      id: "target.tier",
      scope: "Target tier",
      severity: "Blocker",
      message: `${target.tierName || target.tier} is not configured as a promotion target.`
    });
  }

  const unhealthyTargetEngine = targetEngines.find((engine) => engine.health !== "Healthy");
  if (unhealthyTargetEngine) {
    issues.push({
      id: `target.engine-health.${unhealthyTargetEngine.id}`,
      scope: "Target engine health",
      severity: "Warning",
      message: `${unhealthyTargetEngine.name} is ${unhealthyTargetEngine.health.toLowerCase()}. Promotion preview can run, but deployment may be blocked until the engine verifies as healthy.`
    });
  }

  return issues;
}

function assertPromotionReady(issues: PromotionReadinessIssue[]) {
  const blockers = issues.filter((issue) => issue.severity === "Blocker");
  if (blockers.length === 0) return;
  throw new Error(blockers.map((issue) => issue.message).join(" "));
}

function findEnvironmentById(data: DeploymentCockpit, environmentId: string) {
  return data.applications.flatMap((application) => application.environments).find((environment) => environment.id === environmentId);
}

function hasUsableDesiredRevision(environment: EnvironmentSummary) {
  return Boolean(environment.desiredRevision.id) && environment.desiredRevision.revision > 0;
}

function isValidArtifactForRevision(artifact: WorkspaceArtifact) {
  return artifact.status !== "Archived" && artifact.inspectionStatus === "Valid" && artifact.checksumStatus === "Verified";
}

function collectDeploymentBlockers(
  data: DeploymentCockpit,
  environment: EnvironmentSummary,
  environmentEngines: WorkflowEngineRegistration[]
) {
  const blockers: DeploymentBlocker[] = [];

  for (const comparison of data.comparisons.filter((item) => item.targetEnvironmentId === environment.id)) {
    for (const validation of comparison.validations.filter((item) => item.severity === "Blocker")) {
      blockers.push({
        id: `comparison:${comparison.sourceEnvironmentId}:${comparison.sourceRevisionId}:${validation.id}`,
        validationId: validation.id,
        scope: validation.scope,
        message: validation.message,
        severity: validation.severity,
        source: `Promotion r${comparison.sourceRevision}`,
        actionPath: validation.id === "deployment.tier.observability-required"
          ? newRevisionPathForEnvironment(data, comparison.sourceEnvironmentId, "includeObservability=1")
          : undefined,
        actionLabel: validation.id === "deployment.tier.observability-required" ? "Add binding to new revision" : undefined
      });
    }

    for (const artifact of comparison.artifacts) {
      for (const validation of artifact.runtimeCompatibility.filter((item) => item.severity === "Blocker")) {
        blockers.push({
          id: `artifact:${comparison.sourceEnvironmentId}:${artifact.name}:${validation.id}`,
          scope: validation.scope,
          message: `${artifact.name}: ${validation.message}`,
          severity: validation.severity,
          source: "Runtime compatibility"
        });
      }
    }
  }

  for (const plan of data.assistantPlans.filter((item) => item.targetEnvironmentId === environment.id && item.status !== "Rejected")) {
    for (const validation of plan.validations.filter((item) => item.severity === "Blocker")) {
      blockers.push({
        id: `assistant:${plan.id}:${validation.id}`,
        scope: validation.scope,
        message: validation.message,
        severity: validation.severity,
        source: `Assistant plan v${plan.version}`
      });
    }
  }

  for (const engine of environmentEngines) {
    if (engine.health !== "Healthy") {
      blockers.push({
        id: `engine:${engine.id}:health`,
        scope: "Engine health",
        message: `${engine.name} is ${engine.health.toLowerCase()}.${engine.verificationMessage ? ` ${engine.verificationMessage}` : ""}`,
        severity: "Blocker",
        source: "Engine"
      });
    }
    if (engine.credentialReference.verificationStatus !== "Verified") {
      blockers.push({
        id: `engine:${engine.id}:credential`,
        scope: "Credential reference",
        message: `${engine.name} credential is ${engine.credentialReference.verificationStatus.toLowerCase()}.`,
        severity: "Blocker",
        source: "Engine"
      });
    }
  }

  if (environmentEngines.length === 0) {
    blockers.push({
      id: "environment:no-engines",
      scope: "Engine",
      message: "No workflow engine is registered for this environment.",
      severity: "Blocker",
      source: "Environment"
    });
  }

  if (environment.tierStatus === "Archived") {
    blockers.push({
      id: "environment:archived-tier",
      scope: "Tier",
      message: `${environment.tierName || environment.tier} is archived.`,
      severity: "Blocker",
      source: "Environment"
    });
  }

  for (const event of data.history.filter((item) => item.environmentId === environment.id && (item.status === "Blocked" || item.validationOutcome === "Blocked"))) {
    const diagnostics = (event.commands ?? []).flatMap((command) =>
      command.diagnostics.filter((diagnostic) => diagnostic.severity === "Error").map((diagnostic) => ({
        id: `history:${event.id}:${command.id}:${diagnostic.code}`,
        scope: command.action,
        message: diagnostic.message,
        severity: "Blocker" as const,
        source: `Run r${event.revision}`
      }))
    );

    if (diagnostics.length > 0) {
      blockers.push(...diagnostics);
    } else {
      blockers.push({
        id: `history:${event.id}:blocked`,
        scope: "Deployment run",
        message: `Run r${event.revision} was blocked with validation outcome ${event.validationOutcome.toLowerCase()}.`,
        severity: "Blocker",
        source: "Run history"
      });
    }
  }

  if (environment.deploymentStatus === "Blocked" && blockers.length === 0) {
    blockers.push({
      id: "environment:blocked-status",
      scope: "Deployment",
      message: "The backend reports this environment as blocked, but no structured validation detail is available yet. Refresh validation or inspect the latest run history.",
      severity: "Blocker",
      source: "Environment"
    });
  }

  return uniqueDeploymentBlockers(blockers);
}

function uniqueDeploymentBlockers(blockers: DeploymentBlocker[]) {
  const seen = new Set<string>();
  return blockers.filter((blocker) => {
    const key = `${blocker.scope}:${blocker.message}:${blocker.source}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function compareText(left?: string | null, right?: string | null) {
  return (left ?? "").localeCompare(right ?? "", undefined, { numeric: true, sensitivity: "base" });
}

function compareNumber(left: number, right: number) {
  return left - right;
}

function deploymentTotals(data: DeploymentCockpit) {
  const environments = data.applications.reduce((total, application) => total + application.environments.length, 0);
  const drift = data.applications.reduce(
    (total, application) => total + application.environments.filter((environment) => environment.driftStatus === "DriftDetected").length,
    0
  );
  return { environments, drift };
}

function credentialReferenceOptions(engines: WorkflowEngineRegistration[]): CredentialReferenceOption[] {
  const options = new Map<string, CredentialReferenceOption>();
  for (const engine of engines) {
    const reference = engine.credentialReference.reference;
    if (!reference || options.has(reference)) continue;
    options.set(reference, {
      provider: engine.credentialReference.provider,
      reference,
      label: `${engine.credentialReference.provider} - ${reference}`
    });
  }
  return Array.from(options.values()).sort((left, right) => left.reference.localeCompare(right.reference));
}

function summarizeApplicationHealth(application: DeploymentCockpit["applications"][number], engines: WorkflowEngineRegistration[]) {
  const environmentIds = new Set(application.environments.map((environment) => environment.id));
  const applicationEngines = engines.filter((engine) => environmentIds.has(engine.environmentId));
  if (application.environments.length === 0 || applicationEngines.length === 0) return "Needs setup";
  if (applicationEngines.some((engine) => engine.health === "Unreachable")) return "Unreachable";
  if (
    applicationEngines.some((engine) => engine.health === "Degraded") ||
    application.environments.some((environment) => environment.driftStatus === "DriftDetected")
  ) {
    return "Needs review";
  }
  return "Healthy";
}

function applicationHealthTone(application: DeploymentCockpit["applications"][number], engines: WorkflowEngineRegistration[]): StatusTone {
  const health = summarizeApplicationHealth(application, engines);
  if (health === "Healthy") return "success";
  if (health === "Needs review" || health === "Needs setup") return "warning";
  return "destructive";
}

function legacyTierFromName(name?: string): EnvironmentSummary["tier"] {
  if (name === "Dev" || name === "Test" || name === "Stage" || name === "Production") return name;
  return "Production";
}

function hasTierCapability(environment: EnvironmentSummary | undefined, capability: string) {
  if (!environment) return false;
  if (environment.tierCapabilities) return environment.tierCapabilities.includes(capability);

  if (capability === deploymentTierCapabilities.rollbackEnabled) return environment.tier === "Production";
  if (capability === deploymentTierCapabilities.promotionSource) return environment.tier !== "Production";
  if (capability === deploymentTierCapabilities.promotionTarget) return environment.tier !== "Dev";
  if (capability === deploymentTierCapabilities.confirmationRequired) return environment.tier === "Production";
  if (capability === deploymentTierCapabilities.productionLike) return environment.tier === "Production";
  return false;
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

function deploymentTone(status: DeploymentStatus | string): StatusTone {
  if (status === "Succeeded") return "success";
  if (status === "Running" || status === "RolledBack" || status === "Queued" || status === "RecoveryRequired") return "warning";
  if (status === "Cancelled") return "neutral";
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

function statusTextClass(tone: StatusTone) {
  if (tone === "success") return "text-success";
  if (tone === "warning") return "text-warning";
  if (tone === "destructive") return "text-destructive";
  return "";
}
