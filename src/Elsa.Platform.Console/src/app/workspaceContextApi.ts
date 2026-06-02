import { apiRequest } from "@/lib/api/httpClient";
import type { OrganizationWorkspaceContextResponse } from "@/app/workspaceContextModels";

export function getOrganizationWorkspaceContext() {
  return apiRequest<OrganizationWorkspaceContextResponse>("/api/me/organizations");
}
