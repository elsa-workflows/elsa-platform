import { apiRequest } from "@/lib/api/httpClient";
import type {
  CatalogPackage,
  PackageManifestContent,
  PackageDetails,
  SelectablePackageVersion,
  ValidationFindingsResponse
} from "@/features/packages/packageModels";

export function listPackages() {
  return apiRequest<CatalogPackage[]>("/api/admin/packages");
}

export function getPackageDetails(packageId: string) {
  return apiRequest<PackageDetails>(`/api/admin/packages/${encodeURIComponent(packageId)}`);
}

export function getPackageValidation(packageId: string, version: string) {
  return apiRequest<ValidationFindingsResponse>(
    `/api/admin/packages/${encodeURIComponent(packageId)}/versions/${encodeURIComponent(version)}/validation`
  );
}

export function getPackageManifest(packageId: string, version: string) {
  return apiRequest<PackageManifestContent>(
    `/api/admin/packages/${encodeURIComponent(packageId)}/versions/${encodeURIComponent(version)}/manifest`
  );
}

export function approvePackageVersion(item: SelectablePackageVersion, reason?: string) {
  const trimmedReason = reason?.trim();
  const body = {
    ...(trimmedReason ? { reason: trimmedReason } : {}),
    ...(item.expectedStateToken ? { expectedStateToken: item.expectedStateToken } : {})
  };
  return apiRequest<void>(`/api/admin/packages/${encodeURIComponent(item.packageId)}/versions/${encodeURIComponent(item.version)}/approve`, {
    method: "POST",
    ...(Object.keys(body).length > 0 ? { body: JSON.stringify(body) } : {})
  });
}

export function rejectPackageVersion(item: SelectablePackageVersion, reason: string) {
  return apiRequest<void>(`/api/admin/packages/${encodeURIComponent(item.packageId)}/versions/${encodeURIComponent(item.version)}/reject`, {
    method: "POST",
    body: JSON.stringify({
      reason: reason.trim(),
      ...(item.expectedStateToken ? { expectedStateToken: item.expectedStateToken } : {})
    })
  });
}
