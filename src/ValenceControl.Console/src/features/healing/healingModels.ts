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
  workflowReference: string;
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
  matchingBindings?: Array<{ id: string; name: string; priority: number; repository: string; targetBranch: string; workflowIdentity: string; workflowReference: string; status: string }>;
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
  workflowReference: string;
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
  webhookSecretCredentialReferenceId?: string;
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
  deploymentObservations: HealingDeploymentObservation[];
  verificationResults: HealingVerificationResult[];
  occurrences: HealingIncidentOccurrence[];
  attributions: HealingComponentAttribution[];
  workItem?: HealingWorkItemSummary | null;
  attempts: HealingRepairAttemptView[];
  humanCommands: HealingHumanCommandView[];
  permissions: HealingPermission[];
};

export type HealingDeploymentObservation = {
  id: string;
  environmentId: string;
  revision: string;
  deployedAt: string;
  source: string;
  sourceObservationId: string;
  acceptedAt: string;
};

export type HealingVerificationResult = {
  id: string;
  episodeId: string;
  environmentId: string;
  repairedRevision: string;
  windowStartedAt?: string | null;
  windowEndsAt?: string | null;
  relevantOperationSuccessCount: number;
  lastRelevantOperationSuccessAt?: string | null;
  recurrenceCount: number;
  lastRecurrenceAt?: string | null;
  outcome: string;
  decidedAt?: string | null;
  decisionReason?: string | null;
  waiverExpiresAt?: string | null;
};

export type HealingHumanCommandView = {
  id: string;
  command: string;
  status: string;
  resultCode?: string | null;
  requestedAt: string;
  completedAt?: string | null;
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

export type HealingNamedCount = { name: string; count: number };
export type HealingEnabledState = { total: number; enabled: number; disabled: number; stopped: number };

export type HealingUsageReport = {
  from?: string | null;
  to?: string | null;
  attempts: number;
  completedAttempts: number;
  failedAttempts: number;
  inputUnits: number;
  outputUnits: number;
  agentDurationSeconds: number;
  repositoryRunDurationSeconds: number;
  repositoryRuns: number;
  providerOperations: number;
  failedProviderOperations: number;
  inferenceBudget: number;
  repositoryRunBudget: number;
  timeBudgetSeconds: number;
  concurrencyBudget: number;
};

export type HealingOverviewIncident = {
  id: string;
  applicationId: string;
  status: string;
  severity: string;
  classification: string;
  occurrenceCount: number;
  repairable: boolean;
  lastSeenAt: string;
};

export type HealingOverview = {
  updatedAt: string;
  applications: HealingEnabledState;
  environments: HealingEnabledState;
  openIncidents: number;
  incidentStates: HealingNamedCount[];
  severities: HealingNamedCount[];
  repairability: { repairable: number; observationOnly: number };
  repairActivity: { activeAttempts: number; blockedAttempts: number; openPullRequests: number; blockedPullRequests: number };
  verificationOutcomes: HealingNamedCount[];
  usage: HealingUsageReport;
  recentIncidents: HealingOverviewIncident[];
  permissions: HealingPermission[];
};

export type HealingOverviewFilters = {
  applicationId?: string;
  environmentId?: string;
  status?: string;
  severity?: string;
  repairable?: boolean;
  from?: string;
  to?: string;
};

export type HealingAuditItem = {
  id: string;
  sequence: number;
  aggregateType: string;
  aggregateId: string;
  eventType: string;
  reasonCode: string;
  actorType: string;
  actorId: string;
  correlationId: string;
  causationId?: string | null;
  policyVersion?: string | null;
  inputHash?: string | null;
  outputHash?: string | null;
  details: Record<string, string | null>;
  occurredAt: string;
};

export type HealingAuditPage = { items: HealingAuditItem[]; nextCursor?: string | null };
export type HealingAuditFilters = { applicationId?: string; incidentId?: string; cursor?: string; take?: number };

export type HealingRepairEvidenceView = {
  tier: string;
  omittedFields: string[];
  expiresAt?: string | null;
};

export type HealingRepairReproductionView = {
  wasAttempted: boolean;
  wasReproduced: boolean;
  classification: string;
  summary: string;
};

export type HealingRepairValidationView = {
  kind: string;
  outcome: string;
  safeSummary: string;
};

export type HealingRepairPullRequestView = {
  number: number;
  url: string;
  isDraft: boolean;
  mergeState: string;
  checksState: string;
  autoMergeDecision: string;
  mergeGates: HealingMergeGateView[];
};

export type HealingMergeGateView = { gate: string; state: string; reasonCode: string };

/** Safe UI projection. Raw diffs and provider credentials are deliberately absent. */
export type HealingRepairAttemptView = {
  id: string;
  attemptNumber: number;
  status: string;
  targetRevision: string;
  producingRevision?: string | null;
  evidence: HealingRepairEvidenceView;
  classification: string;
  confidence?: number | null;
  causalSummary?: string | null;
  reproduction: HealingRepairReproductionView;
  validations: HealingRepairValidationView[];
  pullRequest?: HealingRepairPullRequestView | null;
};
