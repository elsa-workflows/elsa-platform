import { useQuery } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { Link } from "react-router-dom";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { buttonClassName } from "@/components/ui";
import { useWorkspaceContext } from "@/app/WorkspaceContextProvider";
import { getDeploymentTierCapabilities, getDeploymentTiers } from "@/features/deployments/deploymentApi";
import { DeploymentTiersPanel } from "@/features/deployments/DeploymentTiersPanel";
import { queryKeys } from "@/lib/query/queryClient";

export function DeploymentTiersPage() {
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
    return <RequestStateView state="loading" title="Loading workspace tiers" />;
  if (workspaceContext.isError) return <RequestStateView state="unexpected" title="Workspace context could not load" />;
  if (!workspaceId) {
    return <RequestStateView state="empty" title="No workspace selected" description="Select an organization workspace to manage deployment tiers." />;
  }
  if (tiers.isError || tierCapabilities.isError) return <RequestStateView state="unexpected" title="Workspace tiers could not load" />;

  const canManageTiers = workspaceContext.selectedWorkspace?.role === "Owner";

  return (
    <section className="space-y-5">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <h1 className="text-xl font-semibold">Workspace deployment tiers</h1>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
            Manage the tier definitions, safeguards, promotion eligibility, rollback rules, and validation requirements used by environments in this workspace.
          </p>
        </div>
        <Link to="/admin/deployments" className={buttonClassName("secondary")}>
          <ArrowLeft className="h-4 w-4" />
          Back to deployments
        </Link>
      </div>
      <DeploymentTiersPanel
        workspaceId={workspaceId}
        canManageTiers={canManageTiers}
        tiers={tiers.data?.tiers ?? []}
        capabilities={tierCapabilities.data?.capabilities ?? []}
      />
    </section>
  );
}
