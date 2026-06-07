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
  EngineHealthResult,
  PromotionComparison,
  PromotionPreviewRequest,
  PromotionRequest,
  PromotionResult,
  QueueDeploymentRunRequest,
  QueueRollbackRunRequest,
  RegisterDeploymentEngineRequest,
  RuntimeControlExecution,
  RuntimeControlRunRequest,
  UpdateDeploymentApplicationRequest,
  UpdateDeploymentEngineRequest,
  UpdateDeploymentEnvironmentRequest,
  WorkspaceDeploymentTierCapabilitiesResponse,
  WorkspaceDeploymentTierImpactPreviewRequest,
  WorkspaceDeploymentTierRequest,
  WorkspaceDeploymentTiersResponse,
  DeploymentTierImpactSummary,
  WorkspaceDeploymentTier,
  WorkspaceDesiredStateRevision,
  WorkspaceDesiredStateRevisionDetail,
  WorkspaceApplicationRevisionsResponse,
  WorkspaceDeploymentRun,
  WorkspaceDeploymentRunDetailResponse,
  WorkspaceDeploymentPermissionsResponse,
  WorkflowEngineRegistration
} from "@/features/deployments/deploymentModels";

export function getDeploymentCockpit(workspaceId: string) {
  return apiRequest<DeploymentCockpit>(`/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/cockpit`);
}

export function getDeploymentPermissions(workspaceId: string) {
  return apiRequest<WorkspaceDeploymentPermissionsResponse>(`/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/permissions`);
}

export function getDeploymentTierCapabilities(workspaceId: string) {
  return apiRequest<WorkspaceDeploymentTierCapabilitiesResponse>(`/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/tier-capabilities`);
}

export function getDeploymentTiers(workspaceId: string) {
  return apiRequest<WorkspaceDeploymentTiersResponse>(`/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/tiers`);
}

export function createDeploymentTier(workspaceId: string, request: WorkspaceDeploymentTierRequest) {
  return apiRequest<WorkspaceDeploymentTier>(`/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/tiers`, {
    method: "POST",
    body: JSON.stringify(request)
  });
}

export function updateDeploymentTier(workspaceId: string, tierId: string, request: WorkspaceDeploymentTierRequest) {
  return apiRequest<WorkspaceDeploymentTier>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/tiers/${encodeURIComponent(tierId)}`,
    {
      method: "PUT",
      body: JSON.stringify(request)
    }
  );
}

export function previewDeploymentTierImpact(workspaceId: string, tierId: string, request: WorkspaceDeploymentTierImpactPreviewRequest) {
  return apiRequest<DeploymentTierImpactSummary>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/tiers/${encodeURIComponent(tierId)}/impact-preview`,
    {
      method: "POST",
      body: JSON.stringify(request)
    }
  );
}

export function archiveDeploymentTier(workspaceId: string, tierId: string) {
  return apiRequest<WorkspaceDeploymentTier>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/tiers/${encodeURIComponent(tierId)}/archive`,
    { method: "POST" }
  );
}

export function restoreDeploymentTier(workspaceId: string, tierId: string) {
  return apiRequest<WorkspaceDeploymentTier>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/tiers/${encodeURIComponent(tierId)}/restore`,
    { method: "POST" }
  );
}

export function createDeploymentApplication(workspaceId: string, request: CreateDeploymentApplicationRequest) {
  return apiRequest<CreatedDeploymentApplication>(`/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/applications`, {
    method: "POST",
    body: JSON.stringify(request)
  });
}

export function updateDeploymentApplication(workspaceId: string, applicationId: string, request: UpdateDeploymentApplicationRequest) {
  return apiRequest<CreatedDeploymentApplication>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/applications/${encodeURIComponent(applicationId)}`,
    {
      method: "PUT",
      body: JSON.stringify(request)
    }
  );
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

export function updateDeploymentEnvironment(
  workspaceId: string,
  applicationId: string,
  environmentId: string,
  request: UpdateDeploymentEnvironmentRequest
) {
  return apiRequest<CreatedDeploymentEnvironment>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/applications/${encodeURIComponent(applicationId)}/environments/${encodeURIComponent(environmentId)}`,
    {
      method: "PUT",
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

export function updateDeploymentEngine(workspaceId: string, engineId: string, request: UpdateDeploymentEngineRequest) {
  return apiRequest<WorkflowEngineRegistration>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/engines/${encodeURIComponent(engineId)}`,
    {
      method: "PUT",
      body: JSON.stringify(request)
    }
  );
}

export function verifyDeploymentEngine(workspaceId: string, engineId: string) {
  return apiRequest<EngineHealthResult>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/engines/${encodeURIComponent(engineId)}/verify`,
    { method: "POST" }
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

export function getApplicationRevisions(workspaceId: string, applicationId: string) {
  return apiRequest<WorkspaceApplicationRevisionsResponse>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/applications/${encodeURIComponent(applicationId)}/revisions`
  );
}

export function getRevisionDetail(workspaceId: string, revisionId: string) {
  return apiRequest<WorkspaceDesiredStateRevisionDetail>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/revisions/${encodeURIComponent(revisionId)}`
  );
}

export function previewPromotion(workspaceId: string, request: PromotionPreviewRequest) {
  return apiRequest<PromotionComparison>(`/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/promotions/preview`, {
    method: "POST",
    body: JSON.stringify(request)
  });
}

export function promoteRevision(workspaceId: string, request: PromotionRequest) {
  return apiRequest<PromotionResult>(`/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/promotions`, {
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

export function runRuntimeControl(workspaceId: string, engineId: string, controlId: string, request: RuntimeControlRunRequest) {
  return apiRequest<RuntimeControlExecution>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/deployments/engines/${encodeURIComponent(engineId)}/controls/${encodeURIComponent(controlId)}/run`,
    {
      method: "POST",
      body: JSON.stringify(request)
    }
  );
}
