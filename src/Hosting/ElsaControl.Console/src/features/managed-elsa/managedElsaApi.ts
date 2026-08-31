import { apiRequest } from "@/lib/api/httpClient";
import type { ManagedElsaHandoffIssueRequest, ManagedElsaHandoffIssueResponse, ManagedElsaInstance } from "@/features/managed-elsa/managedElsaModels";

export function listManagedElsaInstances(workspaceId: string) {
  return apiRequest<ManagedElsaInstance[]>(
    `/api/workspaces/${encodeURIComponent(workspaceId)}/managed-elsa/instances`
  );
}
export function issueManagedElsaHandoff(request: ManagedElsaHandoffIssueRequest) {
  return apiRequest<ManagedElsaHandoffIssueResponse>("/api/managed-elsa/handoff/issue", {
    method: "POST",
    body: JSON.stringify(request)
  });
}
