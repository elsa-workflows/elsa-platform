import { apiRequest } from "@/lib/api/httpClient";
import type {
  CreatedDeploymentApplication,
  ActionConfirmation,
  CreatedDeploymentEnvironment,
  CreateActionConfirmationRequest,
  CreateDesiredStateRevisionRequest,
  CreateDeploymentApplicationRequest,
  CreateDeploymentEnvironmentRequest,
  DeploymentCockpit,
  PromotionComparison,
  PromotionPreviewRequest,
  QueueDeploymentRunRequest,
  QueueRollbackRunRequest,
  RegisterDeploymentEngineRequest,
  WorkspaceDesiredStateRevision,
  WorkspaceDeploymentRun,
  WorkspaceDeploymentRunDetailResponse,
  WorkspaceDeploymentPermissionsResponse,
  WorkflowEngineRegistration
} from "@/features/deployments/deploymentModels";

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

export function createDeploymentApplication(workspaceId: string, request: CreateDeploymentApplicationRequest) {
  return apiRequest<CreatedDeploymentApplication>(`/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/applications`, {
    method: "POST",
    body: JSON.stringify(request)
  });
}

export function createDeploymentEnvironment(workspaceId: string, applicationId: string, request: CreateDeploymentEnvironmentRequest) {
  return apiRequest<CreatedDeploymentEnvironment>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/applications/${encodeURIComponent(applicationId)}/environments`,
    {
      method: "POST",
      body: JSON.stringify(request)
    }
  );
}

export function registerDeploymentEngine(workspaceId: string, environmentId: string, request: RegisterDeploymentEngineRequest) {
  return apiRequest<WorkflowEngineRegistration>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/environments/${encodeURIComponent(environmentId)}/engines`,
    {
      method: "POST",
      body: JSON.stringify(request)
    }
  );
}

export function createDesiredStateRevision(
  workspaceId: string,
  applicationId: string,
  environmentId: string,
  request: CreateDesiredStateRevisionRequest
) {
  return apiRequest<WorkspaceDesiredStateRevision>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/applications/${encodeURIComponent(applicationId)}/environments/${encodeURIComponent(environmentId)}/revisions`,
    {
      method: "POST",
      body: JSON.stringify(request)
    }
  );
}

export function previewPromotion(workspaceId: string, request: PromotionPreviewRequest) {
  return apiRequest<PromotionComparison>(`/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/promotions/preview`, {
    method: "POST",
    body: JSON.stringify(request)
  });
}

export function createActionConfirmation(workspaceId: string, request: CreateActionConfirmationRequest) {
  return apiRequest<ActionConfirmation>(`/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/confirmations`, {
    method: "POST",
    body: JSON.stringify(request)
  });
}

export function queueDeploymentRun(workspaceId: string, request: QueueDeploymentRunRequest) {
  return apiRequest<WorkspaceDeploymentRun>(`/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/runs`, {
    method: "POST",
    body: JSON.stringify(request)
  });
}

export function queueRollbackRun(workspaceId: string, request: QueueRollbackRunRequest) {
  return apiRequest<WorkspaceDeploymentRun>(`/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/rollbacks`, {
    method: "POST",
    body: JSON.stringify(request)
  });
}

export function getDeploymentRun(workspaceId: string, runId: string) {
  return apiRequest<WorkspaceDeploymentRunDetailResponse>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/runs/${encodeURIComponent(runId)}`
  );
}
