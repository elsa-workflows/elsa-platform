export type ManagedElsaDesiredLifecycle = "Running" | "Stopped" | "Deleting";
export type ManagedElsaObservedLifecycle =
  | "Pending"
  | "Provisioning"
  | "Ready"
  | "Updating"
  | "Degraded"
  | "Stopping"
  | "Stopped"
  | "Deleting"
  | "Failed"
  | "Unknown"
  | "Deleted";
export type ManagedElsaHealth = "Healthy" | "Degraded" | "Unreachable" | "Unknown";

export type ManagedElsaInstance = {
  organizationId: string;
  instanceId: string;
  name: string;
  slug: string;
  desiredLifecycle: ManagedElsaDesiredLifecycle;
  observedLifecycle: ManagedElsaObservedLifecycle;
  health: ManagedElsaHealth;
  canOpen: boolean;
  audience: string | null;
  redirectUri: string | null;
  unavailableReason: string | null;
};

export type ManagedElsaHandoffIssueRequest = {
  organizationId: string;
  instanceId: string;
  audience: string;
  redirectUri: string;
  codeChallenge: string;
};

export type ManagedElsaHandoffIssueResponse = {
  token: string;
  tokenType: string;
  audience: string;
  redirectUri: string;
  issuedAt: string;
  expiresAt: string;
  sessionExpiresAt?: string;
};
