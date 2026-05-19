import { apiRequest } from "@/lib/api/httpClient";

export type ApplicationInfo = {
  name: string;
  buildNumber: string;
};

export function getApplicationInfo() {
  return apiRequest<ApplicationInfo>("/api/admin/application");
}
