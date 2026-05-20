import { apiRequest } from "@/lib/api/httpClient";
import type { PackageSource, SourceFormValues } from "@/features/sources/sourceModels";
import { toSourceRequest } from "@/features/sources/sourceModels";

export function listSources() {
  return apiRequest<PackageSource[]>("/api/admin/sources");
}

export function getSource(sourceId: string) {
  return apiRequest<PackageSource>(`/api/admin/sources/${sourceId}`);
}

export function createSource(values: SourceFormValues) {
  return apiRequest<PackageSource>("/api/admin/sources", {
    method: "POST",
    body: JSON.stringify(toSourceRequest(values))
  });
}

export function updateSource(sourceId: string, values: SourceFormValues) {
  return apiRequest<PackageSource>(`/api/admin/sources/${sourceId}`, {
    method: "PUT",
    body: JSON.stringify(toSourceRequest(values))
  });
}

export function setSourceEnabled(source: PackageSource, enabled: boolean) {
  return updateSource(source.id, {
    name: source.name,
    url: source.url,
    enabled,
    approvalPolicy: source.approvalPolicy,
    versionDiscoveryPolicy: source.versionDiscoveryPolicy,
    includePatterns: source.includePatterns.join("\n"),
    excludePatterns: source.excludePatterns.join("\n"),
    pollingInterval: source.pollingInterval ?? ""
  });
}

export function deleteSource(sourceId: string) {
  return apiRequest<void>(`/api/admin/sources/${sourceId}`, { method: "DELETE" });
}

export function syncSource(sourceId: string) {
  return apiRequest<{ id: string; status: string }>(`/api/admin/sync/sources/${sourceId}`, { method: "POST" });
}
