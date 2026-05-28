import { apiRequest } from "@/lib/api/httpClient";
import type {
  WorkspaceArtifact,
  WorkspaceArtifactInspectionResult,
  WorkspaceArtifactListResponse,
  WorkspaceArtifactRegistrationRequest,
  WorkspaceArtifactTypeListResponse
} from "@/features/artifacts/artifactModels";

export function listWorkspaceArtifacts(workspaceId: string) {
  return apiRequest<WorkspaceArtifactListResponse>(`/api/workspaces/${encodeURIComponent(workspaceId)}/artifacts`);
}

export function getWorkspaceArtifact(workspaceId: string, artifactId: string) {
  return apiRequest<WorkspaceArtifact>(`/api/workspaces/${encodeURIComponent(workspaceId)}/artifacts/${encodeURIComponent(artifactId)}`);
}

export function listWorkspaceArtifactTypes(workspaceId: string) {
  return apiRequest<WorkspaceArtifactTypeListResponse>(`/api/workspaces/${encodeURIComponent(workspaceId)}/artifacts/types`);
}

export function registerWorkspaceArtifact(workspaceId: string, request: WorkspaceArtifactRegistrationRequest) {
  return apiRequest<WorkspaceArtifact>(`/api/workspaces/${encodeURIComponent(workspaceId)}/artifacts`, {
    method: "POST",
    body: JSON.stringify(request)
  });
}

export function refreshWorkspaceArtifactInspection(workspaceId: string, artifactId: string) {
  return apiRequest<WorkspaceArtifactInspectionResult>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/artifacts/${encodeURIComponent(artifactId)}/refresh`,
    { method: "POST" }
  );
}
