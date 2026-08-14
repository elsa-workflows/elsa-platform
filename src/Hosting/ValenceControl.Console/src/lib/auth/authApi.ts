import { apiRequest } from "@/lib/api/httpClient";
import type { CustomerAuthSession } from "@/lib/auth/authModels";

export function getCustomerAuthSession() {
  return apiRequest<CustomerAuthSession>("/api/auth/session");
}
