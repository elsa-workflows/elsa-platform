import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Activity,
  AlertTriangle,
  Bot,
  CheckCircle2,
  ClipboardCheck,
  KeyRound,
  Pencil,
  Plus,
  RadioTower,
  RefreshCw,
  Save,
  Settings2,
  ShieldCheck,
  XCircle
} from "lucide-react";
import { Fragment, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { Badge, Button, buttonClassName, Input, SecondaryButton, Select, Table } from "@/components/ui";
import { RequestStateView } from "@/components/states/RequestStateViews";
import {
  createDeploymentApplication,
  createDeploymentEnvironment,
  createActionConfirmation,
  getDeploymentCockpit,
  getDeploymentPermissions,
  getDeploymentTiers,
  previewPromotion,
  queueDeploymentRun,
  queueRollbackRun,
  registerDeploymentEngine,
  runRuntimeControl,
  updateDeploymentApplication,
  updateDeploymentEngine,
  updateDeploymentEnvironment,
  verifyDeploymentEngine
} from "@/features/deployments/deploymentApi";
import {
  CredentialReferenceInput,
  DeploymentSetupPanel,
  setupEngineRequest,
  type CredentialReferenceOption,
  type DeploymentSetupValues
} from "@/features/deployments/DeploymentSetupPanel";
import { DeploymentRunsPanel } from "@/features/deployments/DeploymentRunsPanel";
import { PromotionPreviewPanel } from "@/features/deployments/PromotionPreviewPanel";
import { RuntimeControlsPanel } from "@/features/deployments/RuntimeControlsPanel";
import {
  engineLabel,
  environmentLabel,
  hasBlockingValidation,
  deploymentTierCapabilities,
  type DeploymentCockpit,
  type DeploymentHealth,
  type DeploymentStatus,
  type DriftStatus,
  type EnvironmentSummary,
  type RuntimeControl,
  type ValidationSeverity,
  type WorkspaceDeploymentTier,
  type WorkspaceDeploymentRunStatus,
  type WorkflowEngineRegistration
} from "@/features/deployments/deploymentModels";
import { formatDateTime } from "@/lib/formatters";
import { queryKeys } from "@/lib/query/queryClient";
import { statusToneClass, type StatusTone } from "@/lib/status/statusBadges";
import { cn } from "@/lib/utils";
import { useWorkspaceContext } from "@/app/WorkspaceContextProvider";

type ViewId = "fleet" | "engine" | "promotion" | "governance" | "assistant";
type SetupMode = "application" | "environment" | null;
type EnvironmentEditValues = {
  name: string;
  tierId: string | null;
  tier: EnvironmentSummary["tier"];
  engine?: WorkflowEngineRegistration;
};

const views: Array<{ id: ViewId; label: string }> = [
  { id: "fleet", label: "Environments" },
  { id: "engine", label: "Engine Registration" },
  { id: "promotion", label: "Promotion Diff" },
  { id: "governance", label: "Observability" },
  { id: "assistant", label: "Assistant Review" }
];

export function DeploymentsPage() {
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
    enabled: Boolean(workspaceId)
  });
  const tiers = useQuery({
    queryKey: queryKeys.deploymentTiers(workspaceId),
    queryFn: () => getDeploymentTiers(workspaceId),
    enabled: Boolean(workspaceId)
  });
  const refreshDeploymentCockpit = () => queryClient.invalidateQueries({ queryKey: queryKeys.deploymentCockpit(workspaceId) });
  const setup = useMutation({
    mutationFn: async ({ applicationId, values }: { applicationId?: string; values: DeploymentSetupValues }) => {
      const targetApplicationId = applicationId ?? (await createDeploymentApplication(workspaceId, {
        name: values.applicationName,
        description: null
      })).id;
      const environment = await createDeploymentEnvironment(workspaceId, targetApplicationId, {
        name: values.environmentName,
        tier: values.environmentTier,
        tierId: values.environmentTierId
      });
      await registerDeploymentEngine(workspaceId, environment.id, setupEngineRequest(values));
    },
    onSuccess: () => {
      void refreshDeploymentCockpit();
      setSetupMode(null);
    }
  });
  const updateApplication = useMutation({
    mutationFn: ({ applicationId, name }: { applicationId: string; name: string }) =>
      updateDeploymentApplication(workspaceId, applicationId, { name, description: null }),
    onSuccess: () => {
      setEditingApplication(false);
      void refreshDeploymentCockpit();
    }
  });
  const updateEnvironment = useMutation({
    mutationFn: async ({
      applicationId,
      environmentId,
      values
    }: {
      applicationId: string;
      environmentId: string;
      values: EnvironmentEditValues;
    }) => {
      await updateDeploymentEnvironment(workspaceId, applicationId, environmentId, {
        name: values.name,
        tier: values.tier,
        tierId: values.tierId
      });
      if (!values.engine) return;
      await updateDeploymentEngine(workspaceId, values.engine.id, {
        name: values.engine.name,
        baseUrl: values.engine.endpoint.baseUrl,
        region: values.engine.endpoint.region || null,
        credentialProvider: values.engine.credentialReference.provider,
        credentialReference: values.engine.credentialReference.reference,
        capabilities: values.engine.capabilities,
        controls: values.engine.controls,
        hostingProvider: values.engine.hostingProvider
      });
    },
    onSuccess: () => {
      setEditingEnvironmentId("");
      void refreshDeploymentCockpit();
    }
  });
  const updateEngine = useMutation({
    mutationFn: (engine: WorkflowEngineRegistration) =>
      updateDeploymentEngine(workspaceId, engine.id, {
        name: engine.name,
        baseUrl: engine.endpoint.baseUrl,
        region: engine.endpoint.region || null,
        credentialProvider: engine.credentialReference.provider,
        credentialReference: engine.credentialReference.reference,
        capabilities: engine.capabilities,
        controls: engine.controls,
        hostingProvider: engine.hostingProvider
    }),
    onSuccess: () => {
      setEditingEngine(false);
      void refreshDeploymentCockpit();
    }
  });
  const verifyEngine = useMutation({
    mutationFn: (engine: WorkflowEngineRegistration) => verifyDeploymentEngine(workspaceId, engine.id),
    onSuccess: (result) => {
      setOperationNotice(result.message);
      void refreshDeploymentCockpit();
    }
  });
  const runControl = useMutation({
    mutationFn: async ({ engine, control }: { engine: WorkflowEngineRegistration; control: RuntimeControl }) => {
      const confirmation = await createActionConfirmation(workspaceId, {
        actionType: "RuntimeControl",
        targetId: `${engine.id}:${control.id}`,
        lifetimeSeconds: null
      });
      return runRuntimeControl(workspaceId, engine.id, control.id, { confirmationId: confirmation.id });
    },
    onSuccess: (execution) => {
      setOperationNotice(execution.message);
      void refreshDeploymentCockpit();
    }
  });
  const preview = useMutation({
    mutationFn: () => {
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
    onSuccess: (comparison) => {
      setPreviewComparison(comparison);
      setPromotionNotice("Promotion preview refreshed from live validation.");
    }
  });
  const deployRevision = useMutation({
    mutationFn: async () => {
      const currentComparison = getActiveComparison();
      const targetEngine = getTargetEngine(currentComparison?.targetEnvironmentId ?? targetEnvironmentId);
      const sourceRevisionId = currentComparison?.sourceRevisionId || getEnvironment(sourceEnvironmentId)?.desiredRevision.id;
      if (!currentComparison || !targetEngine || !sourceRevisionId)
        throw new Error("Refresh a valid promotion preview before deployment.");

      const confirmation = await createActionConfirmation(workspaceId, {
        actionType: "Deploy",
        targetId: sourceRevisionId,
        lifetimeSeconds: null
      });
      return queueDeploymentRun(workspaceId, {
        sourceRevisionId,
        targetEnvironmentId: currentComparison.targetEnvironmentId,
        targetEngineId: targetEngine.id,
        confirmationId: confirmation.id,
        mode: "Apply"
      });
    },
    onSuccess: (run) => {
      setPromotionNotice(`Deployment run ${run.status.toLowerCase()} for revision ${run.sourceRevisionId}.`);
      void refreshDeploymentCockpit();
    }
  });
  const rollbackRevision = useMutation({
    mutationFn: async () => {
      const currentComparison = getActiveComparison();
      const targetEngine = getTargetEngine(currentComparison?.targetEnvironmentId ?? targetEnvironmentId);
      const rollbackSourceRun = getLatestTargetRun(currentComparison?.targetEnvironmentId ?? targetEnvironmentId, targetEngine?.id ?? "");
      const sourceRevisionId = currentComparison?.rollbackRevisionId;
      if (!currentComparison || !targetEngine || !rollbackSourceRun || !sourceRevisionId)
        throw new Error("A previous compatible run is required before rollback can be queued.");

      const confirmation = await createActionConfirmation(workspaceId, {
        actionType: "Rollback",
        targetId: sourceRevisionId,
        lifetimeSeconds: null
      });
      return queueRollbackRun(workspaceId, {
        sourceRevisionId,
        targetEnvironmentId: currentComparison.targetEnvironmentId,
        targetEngineId: targetEngine.id,
        confirmationId: confirmation.id,
        rollbackSourceRunId: rollbackSourceRun.id,
        mode: "Apply"
      });
    },
    onSuccess: (run) => {
      setPromotionNotice(`Rollback run ${run.status.toLowerCase()} for revision ${run.sourceRevisionId}.`);
      void refreshDeploymentCockpit();
    }
  });
  const [activeView, setActiveView] = useState<ViewId>("fleet");
  const [setupMode, setSetupMode] = useState<SetupMode>(null);
  const [editingApplication, setEditingApplication] = useState(false);
  const [editingEnvironmentId, setEditingEnvironmentId] = useState("");
  const [editingEngine, setEditingEngine] = useState(false);
  const [selectedApplicationId, setSelectedApplicationId] = useState("");
  const [selectedEnvironmentId, setSelectedEnvironmentId] = useState("");
  const [selectedEngineId, setSelectedEngineId] = useState("");
  const [sourceEnvironmentId, setSourceEnvironmentId] = useState("");
  const [targetEnvironmentId, setTargetEnvironmentId] = useState("");
  const [operationNotice, setOperationNotice] = useState("");
  const [promotionNotice, setPromotionNotice] = useState("");
  const [previewComparison, setPreviewComparison] = useState<DeploymentCockpit["comparisons"][number] | null>(null);
  const [assistantOutcome, setAssistantOutcome] = useState<"Proposed" | "Approved" | "Rejected">("Proposed");

  const data = cockpit.data;
  const selectedApplication = data?.applications.find((application) => application.id === selectedApplicationId) ?? data?.applications[0];
  const selectedApplicationEnvironmentIds = useMemo(
    () => new Set(selectedApplication?.environments.map((environment) => environment.id) ?? []),
    [selectedApplication?.environments]
  );
  const selectedEnvironment =
    selectedApplication?.environments.find((environment) => environment.id === selectedEnvironmentId) ?? selectedApplication?.environments[0];
  const selectedEngine =
    data?.engines.find((engine) => engine.id === selectedEngineId && selectedApplicationEnvironmentIds.has(engine.environmentId)) ??
    data?.engines.find((engine) => engine.environmentId === selectedEnvironment?.id);

  useEffect(() => {
    if (!data) return;

    const application = data.applications.find((item) => item.id === selectedApplicationId) ?? data.applications[0];
    const environment = application?.environments.find((item) => item.id === selectedEnvironmentId) ?? application?.environments[0];
    const applicationEnvironmentIds = new Set(application?.environments.map((item) => item.id) ?? []);
    const engine =
      data.engines.find((item) => item.id === selectedEngineId && applicationEnvironmentIds.has(item.environmentId)) ??
      data.engines.find((item) => item.environmentId === environment?.id);
    const nextComparison = data.comparisons.find(
      (item) => item.sourceEnvironmentId === sourceEnvironmentId && item.targetEnvironmentId === targetEnvironmentId
    ) ?? data.comparisons[0];

    if (application && application.id !== selectedApplicationId) setSelectedApplicationId(application.id);
    if (environment && environment.id !== selectedEnvironmentId) setSelectedEnvironmentId(environment.id);
    if (engine && engine.id !== selectedEngineId) setSelectedEngineId(engine.id);
    if (nextComparison && !sourceEnvironmentId) setSourceEnvironmentId(nextComparison.sourceEnvironmentId);
    if (nextComparison && !targetEnvironmentId) setTargetEnvironmentId(nextComparison.targetEnvironmentId);
  }, [data, selectedApplicationId, selectedEngineId, selectedEnvironmentId, sourceEnvironmentId, targetEnvironmentId]);

  const comparison = useMemo(() => {
    const cockpitComparison = data?.comparisons.find(
      (item) => item.sourceEnvironmentId === sourceEnvironmentId && item.targetEnvironmentId === targetEnvironmentId
    ) ?? data?.comparisons[0];
    return previewComparison?.sourceEnvironmentId === sourceEnvironmentId && previewComparison.targetEnvironmentId === targetEnvironmentId
      ? previewComparison
      : cockpitComparison;
  }, [data?.comparisons, previewComparison, sourceEnvironmentId, targetEnvironmentId]);
  const canManageSetup = Boolean(permissions.data?.permissions.includes("deployments.setup.manage"));
  const canPreviewPromotion = Boolean(permissions.data?.permissions.includes("deployments.promotion.preview"));
  const canExecuteDeployment = Boolean(permissions.data?.permissions.includes("deployments.run.execute"));
  const canExecuteRollback = Boolean(permissions.data?.permissions.includes("deployments.rollback.execute"));
  const canExecuteControls = Boolean(permissions.data?.permissions.includes("deployments.controls.execute"));
  const targetAllowsRollback = hasTierCapability(getEnvironment(targetEnvironmentId), deploymentTierCapabilities.rollbackEnabled);
  const deploymentTiers = tiers.data?.tiers ?? [];
  const activeDeploymentTiers = deploymentTiers.filter((tier) => tier.status === "Active");
  const credentialOptions = useMemo(() => credentialReferenceOptions(data?.engines ?? []), [data?.engines]);

  function getEnvironment(environmentId: string) {
    return data?.applications.flatMap((application) => application.environments).find((environment) => environment.id === environmentId);
  }

  function getTargetEngine(environmentId: string) {
    return data?.engines.find((engine) => engine.environmentId === environmentId);
  }

  function getLatestTargetRun(environmentId: string, engineId: string) {
    return data?.history.find((event) => event.environmentId === environmentId && event.engineId === engineId);
  }

  function getActiveComparison() {
    return comparison;
  }

  if (workspaceContext.isLoading || cockpit.isLoading) return <RequestStateView state="loading" title="Loading deployments" />;
  if (workspaceContext.isError) return <RequestStateView state="unexpected" title="Workspace context could not load" />;
  if (!workspaceId) {
    return <RequestStateView state="empty" title="No workspace selected" description="Select an organization workspace to view deployments." />;
  }
  if (cockpit.isError || !data) {
    return <RequestStateView state="unexpected" title="Deployments could not load" />;
  }
  if (data.applications.length === 0) {
    return (
      <section className="space-y-4">
        <RequestStateView
          state="empty"
          title="No deployment setup"
          description="Create a workflow application, environment, and engine registration to start managing deployments."
        />
        <DeploymentSetupPanel
          canManageSetup={canManageSetup}
          tiers={activeDeploymentTiers}
          tiersLoading={tiers.isLoading}
          credentialOptions={credentialOptions}
          isSubmitting={setup.isPending}
          error={setup.error instanceof Error ? setup.error.message : undefined}
          onSubmit={(values) => setup.mutate({ values })}
        />
      </section>
    );
  }
  if (!selectedApplication) {
    return <RequestStateView state="unexpected" title="Deployments could not load" />;
  }

  function selectApplication(applicationId: string) {
    const application = data?.applications.find((item) => item.id === applicationId);
    const environment = application?.environments[0];
    const engine = data?.engines.find((item) => item.environmentId === environment?.id);
    setSelectedApplicationId(applicationId);
    if (environment) setSelectedEnvironmentId(environment.id);
    if (engine) setSelectedEngineId(engine.id);
    setOperationNotice("");
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
            Manage workflow applications, environments, engine registrations, promotion previews, observability, and assistant plan approvals for the selected workspace.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <SecondaryButton disabled={!canManageSetup} onClick={() => setSetupMode((current) => current === "application" ? null : "application")}>
            <Plus className="h-4 w-4" />
            New application
          </SecondaryButton>
          <Link to="/admin/deployments/tiers" className={buttonClassName("secondary")}>
            <Settings2 className="h-4 w-4" />
            Workspace tiers
          </Link>
        </div>
      </div>

      <div className="grid gap-5 lg:grid-cols-[280px_minmax(0,1fr)]">
        <ApplicationRail
          applications={data.applications}
          engines={data.engines}
          selectedApplicationId={selectedApplication.id}
          workspaceName={selectedApplication.workspaceName}
          organizationName={workspaceContext.selectedOrganization?.name ?? "organization"}
          onSelectApplication={selectApplication}
        />
        <div className="space-y-4">
          <SelectedApplicationHeader
            application={selectedApplication}
            engines={data.engines}
            canManageSetup={canManageSetup}
            onEditApplication={() => setEditingApplication((current) => !current)}
            onAddEnvironment={() => setSetupMode((current) => current === "environment" ? null : "environment")}
          />

          {setupMode ? (
            <DeploymentSetupPanel
              fixedApplicationName={setupMode === "environment" ? selectedApplication.name : undefined}
              canManageSetup={canManageSetup}
              tiers={activeDeploymentTiers}
              tiersLoading={tiers.isLoading}
              credentialOptions={credentialOptions}
              isSubmitting={setup.isPending}
              error={setup.error instanceof Error ? setup.error.message : undefined}
              submitLabel={setupMode === "environment" ? "Add environment" : "Create setup"}
              onSubmit={(values) => setup.mutate({ applicationId: setupMode === "environment" ? selectedApplication.id : undefined, values })}
            />
          ) : null}
          {editingApplication ? (
            <ApplicationEditPanel
              key={selectedApplication.id}
              application={selectedApplication}
              isSubmitting={updateApplication.isPending}
              error={updateApplication.error instanceof Error ? updateApplication.error.message : undefined}
              onCancel={() => setEditingApplication(false)}
              onSubmit={(name) => updateApplication.mutate({ applicationId: selectedApplication.id, name })}
            />
          ) : null}

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
              canManageSetup={canManageSetup}
              editingEnvironmentId={editingEnvironmentId}
              tiers={activeDeploymentTiers}
              credentialOptions={credentialOptions}
              isSavingEnvironment={updateEnvironment.isPending}
              environmentError={updateEnvironment.error instanceof Error ? updateEnvironment.error.message : undefined}
              onEditEnvironment={setEditingEnvironmentId}
              onCancelEnvironmentEdit={() => setEditingEnvironmentId("")}
              onSaveEnvironment={(environmentId, values) =>
                updateEnvironment.mutate({ applicationId: selectedApplication.id, environmentId, values })
              }
              onInspectEnvironment={inspectEnvironment}
            />
          ) : null}
          {activeView === "engine" && selectedEngine ? (
            <EngineView
              application={selectedApplication}
              data={data}
              selectedEnvironmentId={selectedEnvironmentId}
              selectedEngine={selectedEngine}
              credentialOptions={credentialOptions}
              operationNotice={operationNotice}
              canManageSetup={canManageSetup}
              isEditingEngine={editingEngine}
              isSavingEngine={updateEngine.isPending}
              engineError={updateEngine.error instanceof Error ? updateEngine.error.message : undefined}
              isVerifyingEngine={verifyEngine.isPending}
              verifyError={verifyEngine.error instanceof Error ? verifyEngine.error.message : undefined}
              canExecuteControls={canExecuteControls}
              isRunningControl={runControl.isPending}
              controlError={runControl.error instanceof Error ? runControl.error.message : undefined}
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
              onEditEngine={() => setEditingEngine((current) => !current)}
              onCancelEngineEdit={() => setEditingEngine(false)}
              onSaveEngine={(engine) => updateEngine.mutate(engine)}
              onVerifyEngine={() => verifyEngine.mutate(selectedEngine)}
              onRunControl={(control) => runControl.mutate({ engine: selectedEngine, control })}
            />
          ) : null}
          {activeView === "engine" && !selectedEngine ? (
            <RequestStateView
              state="empty"
              title="No engine registered"
              description="Create a deployment setup or register an engine before inspecting engine registration."
            />
          ) : null}
          {activeView === "promotion" ? (
            <PromotionView
              data={data}
              sourceEnvironmentId={sourceEnvironmentId}
              targetEnvironmentId={targetEnvironmentId}
              onSourceEnvironmentChange={(environmentId) => {
                setSourceEnvironmentId(environmentId);
                setPreviewComparison(null);
                setPromotionNotice("");
              }}
              onTargetEnvironmentChange={(environmentId) => {
                setTargetEnvironmentId(environmentId);
                setPreviewComparison(null);
                setPromotionNotice("");
              }}
              comparison={comparison}
              canPreview={canPreviewPromotion}
              canDeploy={canExecuteDeployment}
              canRollback={canExecuteRollback && targetAllowsRollback}
              rollbackBlockedReason={targetAllowsRollback ? undefined : "Rollback is not enabled for the target tier."}
              isPreviewing={preview.isPending}
              isQueueingDeployment={deployRevision.isPending}
              isQueueingRollback={rollbackRevision.isPending}
              notice={promotionNotice}
              error={
                preview.error instanceof Error
                  ? preview.error.message
                  : deployRevision.error instanceof Error
                    ? deployRevision.error.message
                    : rollbackRevision.error instanceof Error
                      ? rollbackRevision.error.message
                      : undefined
              }
              onRefreshPreview={() => preview.mutate()}
              onDeploy={() => deployRevision.mutate()}
              onRollback={() => rollbackRevision.mutate()}
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
        </div>
      </div>
    </section>
  );
}

function ApplicationRail({
  applications,
  engines,
  selectedApplicationId,
  workspaceName,
  organizationName,
  onSelectApplication
}: {
  applications: DeploymentCockpit["applications"];
  engines: WorkflowEngineRegistration[];
  selectedApplicationId: string;
  workspaceName: string;
  organizationName: string;
  onSelectApplication: (applicationId: string) => void;
}) {
  return (
    <aside className="space-y-3 lg:sticky lg:top-20">
      <div className="rounded-ui border border-border bg-surface p-3">
        <div className="text-xs text-muted-foreground">Workspace</div>
        <div className="mt-1 font-medium">{workspaceName}</div>
        <div className="text-xs text-muted-foreground">Under {organizationName}</div>
      </div>
      <div className="rounded-ui border border-border bg-surface">
        <div className="border-b border-border px-3 py-2">
          <h2 className="text-sm font-semibold">Workflow applications</h2>
          <p className="text-xs text-muted-foreground">{applications.length} configured</p>
        </div>
        <div className="divide-y divide-border">
          {applications.map((application) => {
            const environmentIds = new Set(application.environments.map((environment) => environment.id));
            const applicationEngines = engines.filter((engine) => environmentIds.has(engine.environmentId));
            const driftCount = application.environments.filter((environment) => environment.driftStatus === "DriftDetected").length;
            const isSelected = application.id === selectedApplicationId;

            return (
              <button
                key={application.id}
                type="button"
                aria-pressed={isSelected}
                className={cn(
                  "block w-full px-3 py-3 text-left transition-colors",
                  isSelected ? "bg-primary/10" : "hover:bg-muted/60"
                )}
                onClick={() => onSelectApplication(application.id)}
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="min-w-0 truncate text-sm font-medium">{application.name}</span>
                  <StatusBadge value={summarizeApplicationHealth(application, engines)} tone={applicationHealthTone(application, engines)} />
                </div>
                <div className="mt-2 flex flex-wrap gap-2 text-xs text-muted-foreground">
                  <span>{application.environments.length} environments</span>
                  <span>{applicationEngines.length} engines</span>
                  {driftCount > 0 ? <span>{driftCount} drift</span> : null}
                </div>
              </button>
            );
          })}
        </div>
      </div>
    </aside>
  );
}

function SelectedApplicationHeader({
  application,
  engines,
  canManageSetup,
  onEditApplication,
  onAddEnvironment
}: {
  application: DeploymentCockpit["applications"][number];
  engines: WorkflowEngineRegistration[];
  canManageSetup: boolean;
  onEditApplication: () => void;
  onAddEnvironment: () => void;
}) {
  const applicationEngineIds = new Set(application.environments.flatMap((environment) => environment.engineIds));
  const applicationEngines = engines.filter((engine) => applicationEngineIds.has(engine.id));
  const driftCount = application.environments.filter((environment) => environment.driftStatus === "DriftDetected").length;

  return (
    <div className="rounded-ui border border-border bg-surface p-4">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div>
          <h2 className="text-lg font-semibold">{application.name}</h2>
          <div className="mt-2 flex flex-wrap gap-2 text-xs text-muted-foreground">
            <span>{application.environments.length} environments</span>
            <span>{applicationEngines.length} registered engines</span>
            <span>{applicationEngines.filter((engine) => engine.health === "Healthy").length} healthy</span>
            <span>{driftCount} drift detected</span>
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          <SecondaryButton disabled={!canManageSetup} onClick={onEditApplication}>
            <Pencil className="h-4 w-4" />
            Edit application
          </SecondaryButton>
          <Button disabled={!canManageSetup} onClick={onAddEnvironment}>
            <Plus className="h-4 w-4" />
            Add environment
          </Button>
        </div>
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

function EnvironmentEditPanel({
  environment,
  engine,
  tiers,
  credentialOptions,
  isSubmitting,
  error,
  onCancel,
  onSubmit
}: {
  environment: EnvironmentSummary;
  engine?: WorkflowEngineRegistration;
  tiers: WorkspaceDeploymentTier[];
  credentialOptions: CredentialReferenceOption[];
  isSubmitting: boolean;
  error?: string;
  onCancel: () => void;
  onSubmit: (values: EnvironmentEditValues) => void;
}) {
  const [name, setName] = useState(environment.name);
  const [tierId, setTierId] = useState(environment.tierId ?? "");
  const [engineName, setEngineName] = useState(engine?.name ?? "");
  const [baseUrl, setBaseUrl] = useState(engine?.endpoint.baseUrl ?? "");
  const [region, setRegion] = useState(engine?.endpoint.region ?? "");
  const [credentialProvider, setCredentialProvider] = useState(engine?.credentialReference.provider ?? credentialOptions[0]?.provider ?? "");
  const [credentialReference, setCredentialReference] = useState(engine?.credentialReference.reference ?? "");
  const selectedTier = tiers.find((tier) => tier.id === tierId);
  const environmentChanged = name !== environment.name || tierId !== (environment.tierId ?? "");
  const engineChanged = Boolean(
    engine &&
      (engineName !== engine.name ||
        baseUrl !== engine.endpoint.baseUrl ||
        region !== engine.endpoint.region ||
        credentialProvider !== engine.credentialReference.provider ||
        credentialReference !== engine.credentialReference.reference)
  );
  const engineFieldsValid =
    !engine ||
    (engineName.trim().length > 0 &&
      baseUrl.trim().length > 0 &&
      credentialProvider.trim().length > 0 &&
      credentialReference.trim().length > 0);
  const canSubmit = name.trim().length > 0 && engineFieldsValid && (environmentChanged || engineChanged);

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        if (!canSubmit) return;
        onSubmit({
          name: name.trim(),
          tierId: tierId || null,
          tier: legacyTierFromName(selectedTier?.name ?? environment.tierName),
          engine: engine
            ? {
                ...engine,
                name: engineName.trim(),
                endpoint: { ...engine.endpoint, baseUrl: baseUrl.trim(), region: region.trim() },
                credentialReference: {
                  ...engine.credentialReference,
                  provider: credentialProvider.trim(),
                  reference: credentialReference.trim()
                }
              }
            : undefined
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
              <option key={tier.id} value={tier.id}>{tier.name}</option>
            ))}
          </Select>
        </label>
        {engine ? (
          <>
            <label className="text-sm font-medium">
              Engine
              <Input className="mt-1" value={engineName} onChange={(event) => setEngineName(event.target.value)} />
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
            <label className="text-sm font-medium md:col-span-2">
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
          </>
        ) : (
          <p className="rounded-ui border border-dashed border-border px-3 py-2 text-sm text-muted-foreground md:col-span-2">
            No engine is registered for this environment yet. Use Add environment to create one with an engine registration.
          </p>
        )}
      </div>
      <div className="mt-3 flex gap-2">
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

function FleetView({
  application,
  engines,
  canManageSetup,
  editingEnvironmentId,
  tiers,
  credentialOptions,
  isSavingEnvironment,
  environmentError,
  onEditEnvironment,
  onCancelEnvironmentEdit,
  onSaveEnvironment,
  onInspectEnvironment
}: {
  application: DeploymentCockpit["applications"][number];
  engines: WorkflowEngineRegistration[];
  canManageSetup: boolean;
  editingEnvironmentId: string;
  tiers: WorkspaceDeploymentTier[];
  credentialOptions: CredentialReferenceOption[];
  isSavingEnvironment: boolean;
  environmentError?: string;
  onEditEnvironment: (environmentId: string) => void;
  onCancelEnvironmentEdit: () => void;
  onSaveEnvironment: (environmentId: string, values: EnvironmentEditValues) => void;
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
            {application.environments.length === 0 ? (
              <tr>
                <td colSpan={8} className="px-3 py-6 text-center text-sm text-muted-foreground">
                  No environments registered.
                </td>
              </tr>
            ) : null}
            {application.environments.map((environment) => (
              <Fragment key={environment.id}>
                {(() => {
                  const primaryEngine = engines.find((engine) => environment.engineIds.includes(engine.id));
                  return (
                <>
                <tr>
                  <td className="px-3 py-3">
                    <div className="font-medium">{environment.name}</div>
                    <div className="text-xs text-muted-foreground">
                      {environment.tierName || environment.tier}
                      {environment.tierStatus === "Archived" ? " (archived)" : ""}
                    </div>
                    <div className="mt-1 flex flex-wrap gap-1">
                      {(environment.tierCapabilities ?? []).slice(0, 3).map((capability) => (
                        <Badge key={capability}>{capability}</Badge>
                      ))}
                    </div>
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
                  <td className="px-3 py-3">
                    <div className="flex justify-end gap-2">
                      <SecondaryButton className="h-8" disabled={!canManageSetup} onClick={() => onEditEnvironment(environment.id)}>
                        Edit
                      </SecondaryButton>
                      <SecondaryButton className="h-8" onClick={() => onInspectEnvironment(environment.id)}>
                        Inspect
                      </SecondaryButton>
                    </div>
                  </td>
                </tr>
                {editingEnvironmentId === environment.id ? (
                  <tr>
                    <td colSpan={8} className="bg-muted/20 px-3 py-3">
                      <EnvironmentEditPanel
                        environment={environment}
                        engine={primaryEngine}
                        tiers={tiers}
                        credentialOptions={credentialOptions}
                        isSubmitting={isSavingEnvironment}
                        error={environmentError}
                        onCancel={onCancelEnvironmentEdit}
                        onSubmit={(values) => onSaveEnvironment(environment.id, values)}
                      />
                    </td>
                  </tr>
                ) : null}
                </>
                  );
                })()}
              </Fragment>
            ))}
          </tbody>
        </table>
      </Table>
    </div>
  );
}

function EngineView({
  application,
  data,
  selectedEnvironmentId,
  selectedEngine,
  credentialOptions,
  operationNotice,
  canManageSetup,
  isEditingEngine,
  isSavingEngine,
  engineError,
  isVerifyingEngine,
  verifyError,
  canExecuteControls,
  isRunningControl,
  controlError,
  onEnvironmentChange,
  onEngineChange,
  onEditEngine,
  onCancelEngineEdit,
  onSaveEngine,
  onVerifyEngine,
  onRunControl
}: {
  application: DeploymentCockpit["applications"][number];
  data: DeploymentCockpit;
  selectedEnvironmentId: string;
  selectedEngine: WorkflowEngineRegistration;
  credentialOptions: CredentialReferenceOption[];
  operationNotice: string;
  canManageSetup: boolean;
  isEditingEngine: boolean;
  isSavingEngine: boolean;
  engineError?: string;
  isVerifyingEngine: boolean;
  verifyError?: string;
  canExecuteControls: boolean;
  isRunningControl: boolean;
  controlError?: string;
  onEnvironmentChange: (environmentId: string) => void;
  onEngineChange: (engineId: string) => void;
  onEditEngine: () => void;
  onCancelEngineEdit: () => void;
  onSaveEngine: (engine: WorkflowEngineRegistration) => void;
  onVerifyEngine: () => void;
  onRunControl: (control: RuntimeControl) => void;
}) {
  const environmentOptions = application.environments;
  const environmentEngines = data.engines.filter((engine) => engine.environmentId === selectedEnvironmentId);

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

      <div className="flex justify-end gap-2">
        <SecondaryButton disabled={!canManageSetup || isVerifyingEngine} onClick={onVerifyEngine}>
          <RefreshCw className="h-4 w-4" />
          {isVerifyingEngine ? "Verifying" : "Verify"}
        </SecondaryButton>
        <SecondaryButton disabled={!canManageSetup} onClick={onEditEngine}>
          <Pencil className="h-4 w-4" />
          Edit engine
        </SecondaryButton>
      </div>

      {isEditingEngine ? (
        <EngineEditPanel
          key={selectedEngine.id}
          engine={selectedEngine}
          credentialOptions={credentialOptions}
          isSubmitting={isSavingEngine}
          error={engineError}
          onCancel={onCancelEngineEdit}
          onSubmit={onSaveEngine}
        />
      ) : null}

      <div className="grid gap-3 lg:grid-cols-[1.2fr_1fr]">
        <Panel title={selectedEngine.name} icon={<RadioTower className="h-4 w-4" />}>
          <dl className="grid gap-3 sm:grid-cols-2">
            <Detail label="Endpoint" value={selectedEngine.endpoint.baseUrl} />
            <Detail label="Region" value={selectedEngine.endpoint.region} />
            <Detail label="Version" value={selectedEngine.endpoint.version} />
            <Detail label="Certificate" value={selectedEngine.endpoint.certificateStatus} />
            <Detail label="Health" value={<StatusBadge value={selectedEngine.health} tone={healthTone(selectedEngine.health)} />} />
            <Detail label="Last heartbeat" value={formatDateTime(selectedEngine.lastHeartbeatAt)} />
            <Detail label="Last verification" value={formatDateTime(selectedEngine.lastVerificationAt)} />
            <Detail label="Diagnostic" value={selectedEngine.verificationMessage || "No verification has run yet."} />
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
      {verifyError ? <div role="alert" className="rounded-ui border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">{verifyError}</div> : null}

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
          <RuntimeControlsPanel
            engine={selectedEngine}
            canExecuteControls={canExecuteControls}
            isRunning={isRunningControl}
            notice={operationNotice}
            error={controlError}
            onRunControl={onRunControl}
          />
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
  canPreview,
  canDeploy,
  canRollback,
  rollbackBlockedReason,
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
}: {
  data: DeploymentCockpit;
  sourceEnvironmentId: string;
  targetEnvironmentId: string;
  comparison: DeploymentCockpit["comparisons"][number] | undefined;
  canPreview: boolean;
  canDeploy: boolean;
  canRollback: boolean;
  rollbackBlockedReason?: string;
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
}) {
  return (
    <PromotionPreviewPanel
      data={data}
      sourceEnvironmentId={sourceEnvironmentId}
      targetEnvironmentId={targetEnvironmentId}
      comparison={comparison}
      canPreview={canPreview}
      canDeploy={canDeploy}
      canRollback={canRollback}
      rollbackBlockedReason={rollbackBlockedReason}
      isPreviewing={isPreviewing}
      isQueueingDeployment={isQueueingDeployment}
      isQueueingRollback={isQueueingRollback}
      notice={notice}
      error={error}
      onSourceEnvironmentChange={onSourceEnvironmentChange}
      onTargetEnvironmentChange={onTargetEnvironmentChange}
      onRefreshPreview={onRefreshPreview}
      onDeploy={onDeploy}
      onRollback={onRollback}
    />
  );
}

function GovernanceView({ data }: { data: DeploymentCockpit }) {
  return (
    <div className="space-y-4">
      {data.observabilityBindings.length === 0 ? (
        <RequestStateView
          state="empty"
          title="No observability metadata"
          description="Persisted log, trace, metric, and console bindings will appear here without opening provider credentials."
        />
      ) : (
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
      )}

      <div className="grid gap-4 xl:grid-cols-2">
        <DeploymentRunsPanel data={data} />
        <Panel title="Drift report" icon={<AlertTriangle className="h-4 w-4" />}>
          {data.driftReport.length === 0 ? (
            <p className="rounded-ui border border-dashed border-border px-3 py-6 text-center text-sm text-muted-foreground">
              No drift metadata has been recorded for this workspace.
            </p>
          ) : (
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
          )}
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
  if (!plan) {
    return (
      <RequestStateView
        state="empty"
        title="No assistant plan available"
        description="Assistant review will appear after a deployment plan is generated for this workspace."
      />
    );
  }

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
  const applicationEngineIds = new Set(application.environments.flatMap((environment) => environment.engineIds));
  const applicationEngines = engines.filter((engine) => applicationEngineIds.has(engine.id));
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
