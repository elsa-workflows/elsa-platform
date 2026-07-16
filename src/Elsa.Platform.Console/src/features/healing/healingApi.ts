import { apiRequest } from "@/lib/api/httpClient";
import type {
  HealingApplicationConfiguration,
  HealingAuthorityCatalog,
  HealingAuthorityProfile,
  CreateHealingAuthorityProfileRequest,
  HealingConfirmation,
  HealingComponentManifestsResponse,
  ActivateSourceOwnershipBindingRequest,
  SourceOwnershipBindingsResponse,
  UpdateHealingConfigurationRequest
} from "@/features/healing/healingModels";

function applicationBase(workspaceId: string, applicationId: string) {
  return `/api/workspaces/${encodeURIComponent(workspaceId)}/healing/applications/${encodeURIComponent(applicationId)}`;
}

export function getHealingConfiguration(workspaceId: string, applicationId: string) {
  return apiRequest<HealingApplicationConfiguration>(`${applicationBase(workspaceId, applicationId)}/configuration`);
}

export function updateHealingConfiguration(
  workspaceId: string,
  applicationId: string,
  request: UpdateHealingConfigurationRequest
) {
  return apiRequest<HealingApplicationConfiguration>(`${applicationBase(workspaceId, applicationId)}/configuration`, {
    method: "PUT",
    body: JSON.stringify(request)
  });
}

export function stopHealing(workspaceId: string, applicationId: string) {
  return createHealingConfirmation(workspaceId, applicationId, "HealingEmergencyStop").then((confirmation) =>
    apiRequest<HealingApplicationConfiguration>(`${applicationBase(workspaceId, applicationId)}/stop`, {
      method: "POST",
      body: JSON.stringify({ confirmationId: confirmation.id })
    })
  );
}

export function resumeHealing(workspaceId: string, applicationId: string) {
  return createHealingConfirmation(workspaceId, applicationId, "HealingEmergencyResume").then((confirmation) =>
    apiRequest<HealingApplicationConfiguration>(`${applicationBase(workspaceId, applicationId)}/resume`, {
      method: "POST",
      body: JSON.stringify({ confirmationId: confirmation.id })
    })
  );
}

export function createHealingConfirmation(
  workspaceId: string,
  applicationId: string,
  actionType: "HealingEmergencyStop" | "HealingEmergencyResume" | "HealingAutomaticMerge",
  automaticMergeEnabled?: boolean
) {
  return apiRequest<HealingConfirmation>(`${applicationBase(workspaceId, applicationId)}/confirmations`, {
    method: "POST",
    body: JSON.stringify({ actionType, automaticMergeEnabled })
  });
}

export function getHealingComponentManifests(workspaceId: string, applicationId: string) {
  return apiRequest<HealingComponentManifestsResponse>(`${applicationBase(workspaceId, applicationId)}/component-manifests`);
}

export function getSourceOwnershipBindings(workspaceId: string, applicationId: string) {
  return apiRequest<SourceOwnershipBindingsResponse>(`${applicationBase(workspaceId, applicationId)}/source-ownership-bindings`);
}

export function getHealingAuthorityCatalog(workspaceId: string, applicationId: string) {
  return apiRequest<HealingAuthorityCatalog>(`${applicationBase(workspaceId, applicationId)}/authority-catalog`);
}

export function createHealingAuthorityProfile(
  workspaceId: string,
  applicationId: string,
  request: CreateHealingAuthorityProfileRequest
) {
  return apiRequest<HealingAuthorityProfile>(`${applicationBase(workspaceId, applicationId)}/authority-profiles`, {
    method: "POST",
    body: JSON.stringify(request)
  });
}

export function transitionHealingProviderConnection(
  workspaceId: string,
  applicationId: string,
  providerConnectionId: string,
  transition: "suspend" | "revoke",
  version: string
) {
  return apiRequest(`${applicationBase(workspaceId, applicationId)}/provider-connections/${encodeURIComponent(providerConnectionId)}/${transition}`, {
    method: "POST",
    body: JSON.stringify({ version })
  });
}

export function validateHealingProviderConnection(
  workspaceId: string,
  applicationId: string,
  providerConnectionId: string,
  version: string
) {
  return apiRequest(`${applicationBase(workspaceId, applicationId)}/provider-connections/${encodeURIComponent(providerConnectionId)}/validate`, {
    method: "POST",
    body: JSON.stringify({ version })
  });
}

export async function registerHealingComponentManifest(
  workspaceId: string,
  applicationId: string,
  revisionId: string,
  canonicalManifestJson: string
) {
  const hash = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(canonicalManifestJson));
  const contentDigest = `sha256:${Array.from(new Uint8Array(hash), (value) => value.toString(16).padStart(2, "0")).join("")}`;
  return apiRequest(`${applicationBase(workspaceId, applicationId)}/revisions/${encodeURIComponent(revisionId)}/component-manifests`, {
    method: "POST",
    headers: { "Idempotency-Key": crypto.randomUUID(), "Content-Digest": contentDigest },
    body: canonicalManifestJson
  });
}

export function transitionHealingComponentManifest(
  workspaceId: string,
  applicationId: string,
  manifestId: string,
  transition: "verify" | "revoke"
) {
  return apiRequest(`${applicationBase(workspaceId, applicationId)}/component-manifests/${encodeURIComponent(manifestId)}/${transition}`, { method: "POST" });
}

export function createSourceOwnershipBinding(
  workspaceId: string,
  applicationId: string,
  request: ActivateSourceOwnershipBindingRequest
) {
  return apiRequest(`${applicationBase(workspaceId, applicationId)}/source-ownership-bindings`, {
    method: "POST",
    body: JSON.stringify(request)
  });
}

export function activateDraftSourceOwnershipBinding(workspaceId: string, applicationId: string, bindingId: string) {
  return apiRequest(`${applicationBase(workspaceId, applicationId)}/source-ownership-bindings/${encodeURIComponent(bindingId)}/activate`, { method: "POST" });
}

export function transitionSourceOwnershipBinding(
  workspaceId: string,
  applicationId: string,
  bindingId: string,
  transition: "suspend" | "revoke"
) {
  return apiRequest(`${applicationBase(workspaceId, applicationId)}/source-ownership-bindings/${encodeURIComponent(bindingId)}/${transition}`, { method: "POST" });
}
