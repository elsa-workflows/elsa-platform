import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Plus } from "lucide-react";
import { useState } from "react";
import type { ReactNode } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { buttonClassName } from "@/components/ui";
import { useWorkspaceContext } from "@/app/WorkspaceContextProvider";
import {
  createDeploymentTier,
  getDeploymentTierCapabilities,
  getDeploymentTiers,
  previewDeploymentTierImpact,
  updateDeploymentTier
} from "@/features/deployments/deploymentApi";
import { DeploymentTiersPanel, TierForm, type TierFormValues } from "@/features/deployments/DeploymentTiersPanel";
import type { DeploymentTierCapability, WorkspaceDeploymentTier } from "@/features/deployments/deploymentModels";
import { queryKeys } from "@/lib/query/queryClient";

type DeploymentTiersContext =
  | { status: "ready"; value: { workspaceId: string; canManageTiers: boolean; tiers: WorkspaceDeploymentTier[]; capabilities: DeploymentTierCapability[] } }
  | { status: "state"; state: ReactNode };

export function DeploymentTiersPage() {
  const context = useDeploymentTiersContext();
  if (context.status !== "ready") return context.state;

  const { workspaceId, canManageTiers, tiers, capabilities } = context.value;

  return (
    <section className="space-y-5">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <h1 className="text-xl font-semibold">Workspace deployment tiers</h1>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
            Manage the tier definitions, safeguards, promotion eligibility, rollback rules, and validation requirements used by environments in this workspace.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Link to="/admin/deployments/tiers/new" className={buttonClassName("primary", !canManageTiers ? "pointer-events-none opacity-50" : undefined)} aria-disabled={!canManageTiers}>
            <Plus className="h-4 w-4" />
            New tier
          </Link>
          <Link to="/admin/deployments" className={buttonClassName("secondary")}>
            <ArrowLeft className="h-4 w-4" />
            Back to deployments
          </Link>
        </div>
      </div>
      <DeploymentTiersPanel
        workspaceId={workspaceId}
        canManageTiers={canManageTiers}
        tiers={tiers}
        capabilities={capabilities}
      />
    </section>
  );
}

export function DeploymentTierCreatePage() {
  const context = useDeploymentTiersContext();
  if (context.status !== "ready") return context.state;
  return <DeploymentTierCreateReady context={context.value} />;
}

function DeploymentTierCreateReady({ context }: { context: Extract<DeploymentTiersContext, { status: "ready" }>["value"] }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const nextSortOrder = context.tiers.reduce((max, tier) => Math.max(max, tier.sortOrder), 0) + 10;
  const createTier = useMutation({
    mutationFn: (values: TierFormValues) =>
      createDeploymentTier(context.workspaceId, {
        name: values.name,
        description: values.description || null,
        sortOrder: values.sortOrder,
        capabilities: values.capabilities
      }),
    onSuccess: async () => {
      await refreshTierQueries(queryClient, context.workspaceId);
      navigate("/admin/deployments/tiers");
    }
  });

  return (
    <TierFormPageShell title="New tier" description="Create a deployment tier for this workspace.">
      <TierForm
        title="Tier details"
        canManageTiers={context.canManageTiers}
        capabilities={context.capabilities}
        initialValues={{ name: "", description: "", sortOrder: nextSortOrder, capabilities: [] }}
        impact={null}
        isSubmitting={createTier.isPending}
        isPreviewing={false}
        error={createTier.error instanceof Error ? createTier.error.message : undefined}
        onCancel={() => navigate("/admin/deployments/tiers")}
        onSubmit={(values) => createTier.mutate(values)}
      />
    </TierFormPageShell>
  );
}

export function DeploymentTierEditPage() {
  const context = useDeploymentTiersContext();
  const { tierId = "" } = useParams();
  if (context.status !== "ready") return context.state;
  return <DeploymentTierEditReady context={context.value} tierId={tierId} />;
}

function DeploymentTierEditReady({ context, tierId }: { context: Extract<DeploymentTiersContext, { status: "ready" }>["value"]; tierId: string }) {
  const tier = context.tiers.find((item) => item.id === tierId);
  if (!tier) return <RequestStateView state="not-found" title="Deployment tier not found" />;

  return <DeploymentTierEditForm context={context} tier={tier} />;
}

function DeploymentTierEditForm({ context, tier }: { context: Extract<DeploymentTiersContext, { status: "ready" }>["value"]; tier: WorkspaceDeploymentTier }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [impact, setImpact] = useState<Awaited<ReturnType<typeof previewDeploymentTierImpact>> | null>(null);

  const previewImpact = useMutation({
    mutationFn: (values: TierFormValues) => previewDeploymentTierImpact(context.workspaceId, tier.id, { capabilities: values.capabilities }),
    onSuccess: setImpact
  });
  const updateTier = useMutation({
    mutationFn: (values: TierFormValues) =>
      updateDeploymentTier(context.workspaceId, tier.id, {
        name: values.name,
        description: values.description || null,
        sortOrder: values.sortOrder,
        capabilities: values.capabilities,
        impactAccepted: Boolean(impact)
      }),
    onSuccess: async () => {
      await refreshTierQueries(queryClient, context.workspaceId);
      navigate("/admin/deployments/tiers");
    }
  });

  const error =
    previewImpact.error instanceof Error
      ? previewImpact.error.message
      : updateTier.error instanceof Error
        ? updateTier.error.message
        : undefined;

  return (
    <TierFormPageShell title={`Edit ${tier.name}`} description="Update tier metadata and capability assignments.">
      <TierForm
        title="Tier details"
        canManageTiers={context.canManageTiers}
        capabilities={context.capabilities}
        initialValues={{
          name: tier.name,
          description: tier.description ?? "",
          sortOrder: tier.sortOrder,
          capabilities: tier.capabilities
        }}
        impact={impact}
        isSubmitting={updateTier.isPending}
        isPreviewing={previewImpact.isPending}
        error={error}
        onCancel={() => navigate("/admin/deployments/tiers")}
        onPreview={(values) => previewImpact.mutate(values)}
        onSubmit={(values) => updateTier.mutate(values)}
      />
    </TierFormPageShell>
  );
}

function useDeploymentTiersContext(): DeploymentTiersContext {
  const workspaceContext = useWorkspaceContext();
  const workspaceId = workspaceContext.selectedWorkspaceId;
  const tiers = useQuery({
    queryKey: queryKeys.deploymentTiers(workspaceId),
    queryFn: () => getDeploymentTiers(workspaceId),
    enabled: Boolean(workspaceId)
  });
  const tierCapabilities = useQuery({
    queryKey: queryKeys.deploymentTierCapabilities(workspaceId),
    queryFn: () => getDeploymentTierCapabilities(workspaceId),
    enabled: Boolean(workspaceId)
  });

  if (workspaceContext.isLoading || tiers.isLoading || tierCapabilities.isLoading)
    return { status: "state", state: <RequestStateView state="loading" title="Loading workspace tiers" /> };
  if (workspaceContext.isError) return { status: "state", state: <RequestStateView state="unexpected" title="Workspace context could not load" /> };
  if (!workspaceId) {
    return { status: "state", state: <RequestStateView state="empty" title="No workspace selected" description="Select an organization workspace to manage deployment tiers." /> };
  }
  if (tiers.isError || tierCapabilities.isError) return { status: "state", state: <RequestStateView state="unexpected" title="Workspace tiers could not load" /> };

  const canManageTiers = workspaceContext.selectedWorkspace?.role === "Owner";

  return {
    status: "ready",
    value: {
      workspaceId,
      canManageTiers,
      tiers: tiers.data?.tiers ?? [],
      capabilities: tierCapabilities.data?.capabilities ?? []
    }
  };
}

function TierFormPageShell({ title, description, children }: { title: string; description: string; children: ReactNode }) {
  return (
    <section className="space-y-5">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <nav className="mb-2 flex flex-wrap items-center gap-2 text-sm text-muted-foreground" aria-label="Breadcrumb">
            <Link to="/admin/deployments">Deployments</Link>
            <span>/</span>
            <Link to="/admin/deployments/tiers">Workspace tiers</Link>
            <span>/</span>
            <span className="text-foreground">{title}</span>
          </nav>
          <h1 className="text-xl font-semibold">{title}</h1>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">{description}</p>
        </div>
        <Link to="/admin/deployments/tiers" className={buttonClassName("secondary")}>
          <ArrowLeft className="h-4 w-4" />
          Back to tiers
        </Link>
      </div>
      <div className="max-w-3xl">{children}</div>
    </section>
  );
}

async function refreshTierQueries(queryClient: ReturnType<typeof useQueryClient>, workspaceId: string) {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: queryKeys.deploymentTiers(workspaceId) }),
    queryClient.invalidateQueries({ queryKey: queryKeys.deploymentCockpit(workspaceId) })
  ]);
}
