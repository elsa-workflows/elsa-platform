import { apiRequest } from "@/lib/api/httpClient";
import type { CustomerAuthSession } from "@/lib/auth/authModels";

export function getCustomerAuthSession() {
  return apiRequest<CustomerAuthSession>("/api/auth/session");
}

export function startCustomerSignIn(returnUrl = `${window.location.pathname}${window.location.search}`) {
  window.location.assign(`/api/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`);
}

export function startCustomerSignOut(returnUrl = "/admin/runtime-builder") {
  void fetch(`/api/auth/logout?returnUrl=${encodeURIComponent(returnUrl)}`, {
    method: "POST",
    credentials: "same-origin"
  }).then(() => {
    window.location.assign(returnUrl);
  });
}
