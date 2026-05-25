import { apiRequest } from "@/lib/api/httpClient";
import type { DeploymentCockpit, WorkspaceDeploymentPermissionsResponse } from "@/features/deployments/deploymentModels";

export type DeploymentWorkspaceContext = {
  account: {
    id: string;
    displayName: string | null;
    email: string | null;
  };
  workspaces: Array<{
    id: string;
    name: string;
    kind: string;
    role: string;
  }>;
};

export function getDeploymentWorkspaceContext() {
  return apiRequest<DeploymentWorkspaceContext>("/api/me/workspaces");
}

export function getDeploymentCockpit(workspaceId: string) {
  return apiRequest<DeploymentCockpit>(`/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/cockpit`);
}

export function getDeploymentPermissions(workspaceId: string) {
  return apiRequest<WorkspaceDeploymentPermissionsResponse>(`/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/permissions`);
}
