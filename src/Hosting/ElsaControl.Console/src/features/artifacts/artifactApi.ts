import { apiRequest } from "@/lib/api/httpClient";
import type {
  ArtifactTypeDefinition,
  CompleteArtifactUploadResponse,
  CreateArtifactUploadRequest,
  CreateArtifactUploadResponse,
  CreateSampleArtifactRequest,
  WorkspaceArtifact,
  WorkspaceArtifactInspectionResult,
  WorkspaceArtifactListResponse,
  WorkspaceArtifactTypeListResponse,
  WorkspaceArtifactUploadCapabilities
} from "@/features/artifacts/artifactModels";

function workspacePath(workspaceId: string, suffix: string) {
  return `/api/workspaces/${encodeURIComponent(workspaceId)}/artifacts${suffix}`;
}

function uploadPath(workspaceId: string, suffix = "") {
  return `/api/workspaces/${encodeURIComponent(workspaceId)}/artifact-uploads${suffix}`;
}

export function listWorkspaceArtifacts(workspaceId: string, includeArchived = false) {
  const query = includeArchived ? "?includeArchived=true" : "";
  return apiRequest<WorkspaceArtifactListResponse>(workspacePath(workspaceId, query));
}

export function getWorkspaceArtifact(workspaceId: string, artifactRecordId: string) {
  return apiRequest<WorkspaceArtifact>(workspacePath(workspaceId, `/${encodeURIComponent(artifactRecordId)}`));
}

export function listWorkspaceArtifactTypes(workspaceId: string) {
  return apiRequest<WorkspaceArtifactTypeListResponse>(workspacePath(workspaceId, "/types"));
}

export function getArtifactUploadCapabilities(workspaceId: string) {
  return apiRequest<WorkspaceArtifactUploadCapabilities>(uploadPath(workspaceId, "/capabilities"));
}

export function createArtifactUpload(workspaceId: string, request: CreateArtifactUploadRequest) {
  return apiRequest<CreateArtifactUploadResponse>(uploadPath(workspaceId), {
    method: "POST",
    body: JSON.stringify(request)
  });
}

/**
 * Uploads the opaque ZIP payload to the server-owned staging session. The API intentionally does
 * not accept artifact metadata here: identity, checksums, and manifest data are inspected server
 * side after completion. Fetch is used instead of XMLHttpRequest so credentials and API error
 * handling remain consistent with every other console request.
 */
export async function uploadArtifactContent(
  workspaceId: string,
  uploadId: string,
  content: Blob,
  onProgress?: (percent: number) => void
) {
  onProgress?.(0);
  const response = await apiRequest<void>(uploadPath(workspaceId, `/${encodeURIComponent(uploadId)}/content`), {
    method: "PUT",
    headers: { "Content-Type": content.type || "application/zip" },
    body: content
  });
  onProgress?.(100);
  return response;
}

export function completeArtifactUpload(workspaceId: string, uploadId: string) {
  return apiRequest<CompleteArtifactUploadResponse>(uploadPath(workspaceId, `/${encodeURIComponent(uploadId)}/complete`), {
    method: "POST"
  });
}

export function abortArtifactUpload(workspaceId: string, uploadId: string) {
  return apiRequest<void>(uploadPath(workspaceId, `/${encodeURIComponent(uploadId)}`), { method: "DELETE" });
}

export function createSampleArtifact(workspaceId: string, request: CreateSampleArtifactRequest) {
  return apiRequest<CompleteArtifactUploadResponse>(uploadPath(workspaceId, "/dev-sample"), {
    method: "POST",
    body: JSON.stringify(request)
  });
}

export function refreshWorkspaceArtifact(workspaceId: string, artifactRecordId: string) {
  return apiRequest<WorkspaceArtifactInspectionResult>(workspacePath(workspaceId, `/${encodeURIComponent(artifactRecordId)}/refresh`), {
    method: "POST"
  });
}

export function archiveWorkspaceArtifact(workspaceId: string, artifactRecordId: string) {
  return apiRequest<WorkspaceArtifact>(workspacePath(workspaceId, `/${encodeURIComponent(artifactRecordId)}/archive`), {
    method: "POST"
  });
}

export function restoreWorkspaceArtifact(workspaceId: string, artifactRecordId: string) {
  return apiRequest<WorkspaceArtifact>(workspacePath(workspaceId, `/${encodeURIComponent(artifactRecordId)}/restore`), {
    method: "POST"
  });
}

export function workspaceArtifactDownloadUrl(workspaceId: string, artifactRecordId: string) {
  return workspacePath(workspaceId, `/${encodeURIComponent(artifactRecordId)}/download`);
}

export type { ArtifactTypeDefinition };
