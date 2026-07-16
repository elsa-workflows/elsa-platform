export type HealingPermission =
  | "healing.read"
  | "healing.configure"
  | "healing.automerge.configure"
  | string;

export type HealingEnvironmentConfiguration = {
  environmentId: string;
  name: string;
  discoveryEnabled: boolean;
  repairDispatchEnabled: boolean;
  environmentKillSwitch: boolean;
  occurrenceThreshold?: number | null;
  debounceWindow?: string | null;
  classificationPolicyJson?: string;
};

export type HealingApplicationConfiguration = {
  applicationId: string;
  applicationName: string;
  discoveryEnabled: boolean;
  repairDispatchEnabled: boolean;
  automaticMergeEnabled: boolean;
  applicationKillSwitch: boolean;
  signalProfileVersion: string;
  defaultAttemptLimit: number;
  verificationWindow: string;
  timeBudget: string;
  concurrencyBudget: number;
  inferenceBudget: number;
  repositoryRunBudget: number;
  classificationPolicyJson?: string;
  version: string;
  manifestReadiness: "Ready" | "Missing" | "Stale" | "Untrusted" | string;
  providerReadiness: "Ready" | "Missing" | "Unavailable" | string;
  environments: HealingEnvironmentConfiguration[];
  permissions: HealingPermission[];
};

export type UpdateHealingConfigurationRequest = Pick<
  HealingApplicationConfiguration,
  | "discoveryEnabled"
  | "repairDispatchEnabled"
  | "automaticMergeEnabled"
  | "signalProfileVersion"
  | "defaultAttemptLimit"
  | "verificationWindow"
  | "timeBudget"
  | "concurrencyBudget"
  | "inferenceBudget"
  | "repositoryRunBudget"
  | "classificationPolicyJson"
  | "version"
  | "environments"
> & { confirmationId?: string };

export type HealingConfirmation = {
  id: string;
  actionType: "HealingEmergencyStop" | "HealingEmergencyResume" | "HealingAutomaticMerge";
  targetId: string;
  expiresAt: string;
};

export type ActivateSourceOwnershipBindingRequest = {
  name: string;
  selectorKind: "Application" | "Package" | "Assembly" | "ComponentKey";
  selectorPattern: string;
  providerConnectionId: string;
  repositoryProviderId: string;
  repositoryOwner: string;
  repositoryName: string;
  targetBranch: string;
  workflowIdentity: string;
  workflowRevision: string;
  pathPolicyId: string;
  evidencePolicyId: string;
  mergePolicyId: string;
  priority: number;
};

export type HealingComponent = {
  componentKey: string;
  kind: "Application" | "Package" | "Assembly" | string;
  name: string;
  version: string | null;
  contentHash: string;
  repositorySuggestion: string | null;
  bindingId: string | null;
  ownershipResolution: "Selected" | "Suggested" | "Ambiguous" | "Unmapped" | string;
  repairEligibility: "Repairable" | "ObservationOnly" | "Ambiguous" | "Unauthorized" | string;
  assemblies?: Array<{ name: string; version: string | null; publicKeyToken: string | null; relativePath: string; contentHash: string }>;
  matchingBindings?: Array<{ id: string; name: string; priority: number; repository: string; targetBranch: string; workflowIdentity: string; status: string }>;
  reasonCodes?: string[];
};

export type HealingComponentManifest = {
  id: string;
  revisionId: string;
  sourceRevision: string;
  manifestDigest: string;
  trustState: "Unverified" | "Verified" | "Rejected" | "Revoked" | string;
  verificationMethod?: string | null;
  automationAuthoritative?: boolean;
  createdAt: string;
  dependencies?: Array<{ fromComponentKey: string; toComponentKey: string }>;
  entries: HealingComponent[];
};

export type SourceOwnershipBinding = {
  id: string;
  name: string;
  selectorKind: string;
  selectorPattern: string;
  repository: string;
  targetBranch: string;
  workflowIdentity: string;
  status: "Draft" | "Active" | "Suspended" | "Revoked" | string;
  version: string;
};

export type HealingComponentManifestsResponse = { items: HealingComponentManifest[]; canApproveOwnership?: boolean };
export type SourceOwnershipBindingsResponse = { items: SourceOwnershipBinding[]; permissions?: HealingPermission[]; canApproveOwnership?: boolean };

export type HealingProviderConnection = {
  id: string;
  provider: string;
  installationId: string;
  repositoryProviderId: string;
  repositoryOwner: string;
  repositoryName: string;
  status: "PendingValidation" | "Active" | "Suspended" | "Revoked" | string;
  updatedAt: string;
  version: string;
};

export type HealingPolicyReference = {
  id: string;
  name: string;
  policyVersion: string;
  policyHash: string;
};

export type HealingAuthorityCatalog = {
  providerConnections: HealingProviderConnection[];
  pathPolicies: HealingPolicyReference[];
  evidencePolicies: HealingPolicyReference[];
  mergePolicies: HealingPolicyReference[];
};

export type CreateHealingAuthorityProfileRequest = {
  name: string;
  installationId: string;
  repositoryOwner: string;
  repositoryName: string;
  credentialReferenceId: string;
  allowedRoots?: string[];
  forbiddenRoots?: string[];
  maxFiles?: number;
  maxChangedLines?: number;
  maxPatchBytes?: number;
  requireReproduction?: boolean;
  allowHighConfidenceInference?: boolean;
  minimumInferenceConfidence?: number;
  automaticMergeEnabled?: boolean;
  requiredChecks?: string[];
  independentVerifier?: string;
  forbiddenChangeCategories?: string[];
  requireRollbackOrStopCapability?: boolean;
  confirmationId?: string;
};

export type HealingAuthorityProfile = {
  providerConnection: HealingProviderConnection;
  pathPolicy: HealingPolicyReference;
  evidencePolicy: HealingPolicyReference;
  mergePolicy: HealingPolicyReference;
};

export type HealingIncidentStatus =
  | "Observed"
  | "ThresholdPending"
  | "ReadyForRepair"
  | "Repairing"
  | "PullRequestOpen"
  | "ObservationOnly"
  | "Suppressed"
  | "NeedsHuman"
  | "Failed"
  | "Merged"
  | "Verifying"
  | "Healed"
  | "FailedVerification"
  | "Superseded"
  | "Waived"
  | string;

export type HealingEnvironmentImpact = {
  episodeId: string;
  environmentId: string;
  firstSeenAt: string;
  lastSeenAt: string;
  occurrenceCount: number;
  producingRevisions: string[];
  currentDeployedRevision?: string | null;
  verificationStatus: string;
  occurrenceThreshold: number;
  debounceWindow: string;
  thresholdReachedAt?: string | null;
  readyAfter?: string | null;
};

export type HealingIncidentSummary = {
  id: string;
  applicationId: string;
  status: HealingIncidentStatus;
  severity: string;
  classification: string;
  firstSeenAt: string;
  lastSeenAt: string;
  occurrenceCount: number;
  activeEpisodeId?: string | null;
  repairable: boolean;
  needsHumanReason?: string | null;
  readyAfter?: string | null;
  environmentImpacts: HealingEnvironmentImpact[];
};

export type HealingIncidentListResponse = {
  items: HealingIncidentSummary[];
  nextCursor?: string | null;
};

export type HealingIncidentEpisode = {
  id: string;
  previousEpisodeId?: string | null;
  openedAt: string;
  closedAt?: string | null;
  producingRevisions: string[];
  targetRevision?: string | null;
  outcome: string;
  regressionReason?: string | null;
};

export type HealingIncidentOccurrence = {
  id: string;
  environmentId: string;
  revisionId?: string | null;
  occurredAt: string;
  acceptedAt: string;
  classification: string;
  severity: string;
  exceptionType: string;
  operationName: string;
  retryState: string;
  evidenceTier: string;
};

export type HealingComponentAttribution = {
  id: string;
  occurrenceId: string;
  componentEntryId: string;
  bindingId?: string | null;
  confidence: number;
  basis: string | number;
  resolution: string;
  reasonCodes: string[];
};

export type HealingWorkItemSummary = {
  id: string;
  episodeId: string;
  number?: number | null;
  url?: string | null;
  providerState?: string | null;
  projectionStatus: string;
  lastProjectedAt?: string | null;
  lastObservedAt?: string | null;
};

export type HealingIncidentDetail = HealingIncidentSummary & {
  episodes: HealingIncidentEpisode[];
  occurrences: HealingIncidentOccurrence[];
  attributions: HealingComponentAttribution[];
  workItem?: HealingWorkItemSummary | null;
};

export type HealingIncidentFilters = {
  applicationId?: string;
  environmentId?: string;
  status?: string;
  severity?: string;
  repairable?: boolean;
  cursor?: string;
  take?: number;
};
