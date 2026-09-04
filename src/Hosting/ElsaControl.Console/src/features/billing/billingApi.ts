import { apiRequest } from "@/lib/api/httpClient";
import type { OrganizationBillingSession, OrganizationBillingStatus } from "@/features/billing/billingModels";

export function getOrganizationBillingStatus(organizationId: string) {
  return apiRequest<OrganizationBillingStatus>(`/api/organizations/${encodeURIComponent(organizationId)}/billing/`);
}

export function createOrganizationCheckout(organizationId: string) {
  return apiRequest<OrganizationBillingSession>(`/api/organizations/${encodeURIComponent(organizationId)}/billing/checkout`, {
    method: "POST",
    body: JSON.stringify({})
  });
}

export function createOrganizationBillingPortal(organizationId: string) {
  return apiRequest<OrganizationBillingSession>(`/api/organizations/${encodeURIComponent(organizationId)}/billing/portal`, {
    method: "POST",
    body: JSON.stringify({})
  });
}
