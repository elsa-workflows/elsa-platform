import { apiRequest } from "@/lib/api/httpClient";
import type {
  WorkspaceWeaverConfiguration,
  WorkspaceWeaverCreateSessionRequest,
  WorkspaceWeaverPlanApprovalRequest,
  WorkspaceWeaverPlanApprovalResponse,
  WorkspaceWeaverPlanExecuteRequest,
  WorkspaceWeaverPlanExecuteResponse,
  WorkspaceWeaverSendMessageRequest,
  WorkspaceWeaverSendMessageResponse,
  WorkspaceWeaverSession,
  WorkspaceWeaverSessionDetail
} from "@/features/weaver/weaverModels";

export function getWeaverConfiguration(workspaceId: string) {
  return apiRequest<WorkspaceWeaverConfiguration>(`/api/workspaces/${encodeURIComponent(workspaceId)}/weaver/configuration`);
}

export function createWeaverSession(workspaceId: string, request: WorkspaceWeaverCreateSessionRequest) {
  return apiRequest<WorkspaceWeaverSession>(`/api/workspaces/${encodeURIComponent(workspaceId)}/weaver/sessions`, {
    method: "POST",
    body: JSON.stringify(request)
  });
}

export function getWeaverSession(workspaceId: string, sessionId: string) {
  return apiRequest<WorkspaceWeaverSessionDetail>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/weaver/sessions/${encodeURIComponent(sessionId)}`
  );
}

export function sendWeaverMessage(workspaceId: string, sessionId: string, request: WorkspaceWeaverSendMessageRequest) {
  return apiRequest<WorkspaceWeaverSendMessageResponse>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/weaver/sessions/${encodeURIComponent(sessionId)}/messages`,
    {
      method: "POST",
      body: JSON.stringify(request)
    }
  );
}

export function approveWeaverPlan(workspaceId: string, planId: string, request: WorkspaceWeaverPlanApprovalRequest) {
  return apiRequest<WorkspaceWeaverPlanApprovalResponse>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/weaver/plans/${encodeURIComponent(planId)}/approvals`,
    {
      method: "POST",
      body: JSON.stringify(request)
    }
  );
}

export function executeWeaverPlan(workspaceId: string, planId: string, request: WorkspaceWeaverPlanExecuteRequest) {
  return apiRequest<WorkspaceWeaverPlanExecuteResponse>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/weaver/plans/${encodeURIComponent(planId)}/execute`,
    {
      method: "POST",
      body: JSON.stringify(request)
    }
  );
}
