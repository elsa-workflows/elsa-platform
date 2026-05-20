import { apiRequest } from "@/lib/api/httpClient";
import type { SyncRun } from "@/features/sync-runs/syncRunModels";
import { normalizeCleanupPreview, normalizeCleanupResult, normalizeSyncRun, normalizeSyncRuns } from "@/features/sync-runs/syncRunModels";

export async function listSyncRuns() {
  return normalizeSyncRuns(await apiRequest<unknown>("/api/admin/sync-runs"));
}

export async function getSyncRun(runId: string) {
  return normalizeSyncRun(await apiRequest<unknown>(`/api/admin/sync-runs/${runId}`));
}

export async function syncAll() {
  return normalizeSyncRun(await apiRequest<SyncRun>("/api/admin/sync", { method: "POST" }));
}

export async function cancelSyncRun(runId: string) {
  return normalizeSyncRun(await apiRequest<SyncRun>(`/api/admin/sync-runs/${runId}/cancel`, { method: "POST" }));
}

export async function previewSyncRunCleanup(completedBefore: string) {
  return normalizeCleanupPreview(await apiRequest<unknown>(`/api/admin/sync-runs/deletion-preview?completedBefore=${encodeURIComponent(completedBefore)}`));
}

export async function deleteSyncRun(runId: string) {
  return normalizeCleanupResult(await apiRequest<unknown>(`/api/admin/sync-runs/${runId}`, { method: "DELETE" }));
}

export async function deleteSyncRunsBefore(completedBefore: string) {
  return normalizeCleanupResult(await apiRequest<unknown>(`/api/admin/sync-runs?completedBefore=${encodeURIComponent(completedBefore)}`, { method: "DELETE" }));
}
