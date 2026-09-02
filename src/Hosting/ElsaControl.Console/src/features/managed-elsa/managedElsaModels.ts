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

export const managedElsaHandoffTokenType = "elsa-handoff+jwt" as const;

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
  version?: number;
  eTag?: string;
  intent?: ManagedElsaInstanceIntent | null;
};

export type ManagedElsaInstanceList = {
  items: ManagedElsaInstance[];
  page: number;
  pageSize: number;
  totalCount: number;
  hasMore: boolean;
};

export type ManagedElsaLaunchProfile = {
  name: string;
  description: string;
  targetMode: string;
  regionCode: string;
  isolationProfile: string;
  capacityProfile: string;
  networkOutcome: string;
  domainOutcome: string;
};

export type ManagedElsaOnboardingOptions = {
  releases: Array<{
    distributionId: string;
    releaseLine: string;
    version: string;
    channel: string;
    topologyId: string;
  }>;
  launchProfile: ManagedElsaLaunchProfile;
};

export type ManagedElsaInstanceIntent = {
  release: {
    distributionId: string;
    releaseLine: string;
    requestedVersion: string;
    channel: string;
    patchUpdates: string;
    minorUpdates: string;
    majorMigrations: string;
  };
  application: {
    topologyId: string;
    featurePresetId: string | null;
    featureOverrides: Record<string, never>;
    packagePolicy: string | null;
    configurationShapeRevisionId: string | null;
  };
  placement: Omit<ManagedElsaLaunchProfile, "name" | "description">;
  desiredLifecycle: "Running";
};

export type ManagedElsaOperation = {
  id: string;
  instanceId: string;
  action: string;
  state: "Accepted" | "WaitingForPriorOperation" | "Queued" | "Running" | "Succeeded" | "Failed" | "RecoveryRequired" | "Cancelled";
  attemptNumber: number;
  failureCode: string | null;
  links: Record<string, string>;
};

export type ManagedElsaAccepted = {
  instance: ManagedElsaInstance;
  operation: ManagedElsaOperation;
  links: Record<string, string>;
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
};
