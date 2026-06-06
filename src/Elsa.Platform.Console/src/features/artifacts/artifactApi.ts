import { apiRequest } from "@/lib/api/httpClient";
import type {
  CompleteArtifactUploadResponse,
  CreateArtifactUploadRequest,
  CreateArtifactUploadResponse,
  CreateSampleArtifactRequest,
  WorkspaceArtifact,
  WorkspaceArtifactInspectionResult,
  WorkspaceArtifactUploadCapabilitiesResponse,
  WorkspaceArtifactListResponse,
  WorkspaceArtifactRegistrationRequest,
  WorkspaceArtifactTypeListResponse
} from "@/features/artifacts/artifactModels";

export function listWorkspaceArtifacts(workspaceId: string, includeArchived = false) {
  const query = includeArchived ? "?includeArchived=true" : "";
  return apiRequest<WorkspaceArtifactListResponse>(`/api/workspaces/${encodeURIComponent(workspaceId)}/artifacts${query}`);
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

export function archiveWorkspaceArtifact(workspaceId: string, artifactId: string) {
  return apiRequest<WorkspaceArtifact>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/artifacts/${encodeURIComponent(artifactId)}/archive`,
    { method: "POST" }
  );
}

export function restoreWorkspaceArtifact(workspaceId: string, artifactId: string) {
  return apiRequest<WorkspaceArtifact>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/artifacts/${encodeURIComponent(artifactId)}/restore`,
    { method: "POST" }
  );
}

export function getArtifactUploadCapabilities(workspaceId: string) {
  return apiRequest<WorkspaceArtifactUploadCapabilitiesResponse>(`/api/workspaces/${encodeURIComponent(workspaceId)}/artifact-uploads/capabilities`);
}

export function createArtifactUpload(workspaceId: string, request: CreateArtifactUploadRequest) {
  return apiRequest<CreateArtifactUploadResponse>(`/api/workspaces/${encodeURIComponent(workspaceId)}/artifact-uploads`, {
    method: "POST",
    body: JSON.stringify(request)
  });
}

export function uploadArtifactContent(
  workspaceId: string,
  uploadId: string,
  file: File,
  onProgress: (progress: number) => void
) {
  return new Promise<void>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("PUT", `/api/workspaces/${encodeURIComponent(workspaceId)}/artifact-uploads/${encodeURIComponent(uploadId)}/content`);
    xhr.setRequestHeader("Content-Type", file.type || "application/zip");
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable)
        onProgress(Math.round((event.loaded / event.total) * 100));
    };
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        onProgress(100);
        resolve();
        return;
      }

      reject(new Error(parseProblemTitle(xhr.responseText) ?? `Upload failed with status ${xhr.status}.`));
    };
    xhr.onerror = () => reject(new Error("Upload failed before the server could process the file."));
    xhr.send(file);
  });
}

export function completeArtifactUpload(workspaceId: string, uploadId: string) {
  return apiRequest<CompleteArtifactUploadResponse>(`/api/workspaces/${encodeURIComponent(workspaceId)}/artifact-uploads/${encodeURIComponent(uploadId)}/complete`, {
    method: "POST"
  });
}

export function abortArtifactUpload(workspaceId: string, uploadId: string) {
  return apiRequest<void>(`/api/workspaces/${encodeURIComponent(workspaceId)}/artifact-uploads/${encodeURIComponent(uploadId)}`, {
    method: "DELETE"
  });
}

export function createSampleArtifact(workspaceId: string, request: CreateSampleArtifactRequest) {
  return apiRequest<CompleteArtifactUploadResponse>(`/api/workspaces/${encodeURIComponent(workspaceId)}/artifact-uploads/dev-sample`, {
    method: "POST",
    body: JSON.stringify(request)
  });
}

function parseProblemTitle(responseText: string) {
  try {
    const problem = JSON.parse(responseText) as { title?: string };
    return problem.title;
  } catch {
    return undefined;
  }
}
