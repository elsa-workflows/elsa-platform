export type DeploymentHealth = "Healthy" | "Degraded" | "Unreachable";
export type DriftStatus = "InSync" | "DriftDetected" | "Unknown";
export type DeploymentStatus = "Succeeded" | "Running" | "Blocked" | "RolledBack";
export type DeploymentTierStatus = "Active" | "Archived";
export type DeploymentTierCapabilityCategory = "Classification" | "Promotion" | "Safeguards" | "Rollback" | "Validation" | "Observability";
export type WorkspaceDeploymentRunStatus = "Queued" | "Running" | "Succeeded" | "Failed" | "Blocked" | "Cancelled" | "RolledBack" | "RecoveryRequired";
export type DeploymentCommandAction = "Deploy" | "Rollback" | "Validate" | "RuntimeControl";
export type DeploymentCommandStatus = "Pending" | "Claimed" | "Running" | "Completed" | "Failed" | "Rejected" | "Cancelled" | "RecoveryRequired" | "Expired";
export type CredentialVerificationStatus = "Verified" | "Missing" | "Expired" | "Unverified";
export type CapabilityBoundary = "Workflow" | "EngineApi" | "Shell" | "Hosting";
export type ValidationSeverity = "Pass" | "Warning" | "Blocker";
export type DiffCategory = "Workflows" | "Features" | "ShellConfiguration" | "RuntimeConfiguration" | "SecretReferences" | "Observability" | "EngineBindings";
export type AssistantPlanStatus = "Proposed" | "Approved" | "Rejected" | "Executed";
export type DeploymentPermission =
  | "deployments.read"
  | "deployments.setup.manage"
  | "deployments.desired-state.manage"
  | "deployments.promotion.preview"
  | "deployments.run.execute"
  | "deployments.rollback.execute"
  | "deployments.controls.execute"
  | "deployments.observability.manage";

export const deploymentTierCapabilities = {
  promotionSource: "deployment.promotion.source",
  promotionTarget: "deployment.promotion.target",
  confirmationRequired: "deployment.confirmation.required",
  rollbackEnabled: "deployment.rollback.enabled",
  productionLike: "deployment.tier.production-like",
  observabilityRequired: "deployment.observability.required"
} as const;

export type RuntimeControl = {
  id: string;
  label: string;
  boundary: CapabilityBoundary;
  capabilityId: string;
  description: string;
};

export type EngineCapability = {
  id: string;
  label: string;
  boundary: CapabilityBoundary;
};

export type DeploymentTierCapability = {
  id: string;
  label: string;
  description: string;
  category: DeploymentTierCapabilityCategory;
  isDeprecated: boolean;
};

export type WorkspaceDeploymentTier = {
  id: string;
  workspaceId: string;
  name: string;
  description: string | null;
  sortOrder: number;
  isDefault: boolean;
  status: DeploymentTierStatus;
  capabilities: string[];
  environmentCount: number;
  createdAt: string;
  updatedAt: string;
  createdByAccountId: string | null;
  updatedByAccountId: string | null;
  archivedAt: string | null;
  archivedByAccountId: string | null;
};

export type DeploymentTierEnvironmentSample = {
  applicationId: string;
  applicationName: string;
  environmentId: string;
  environmentName: string;
};

export type DeploymentTierImpactSummary = {
  tierId: string;
  currentCapabilities: string[];
  proposedCapabilities: string[];
  addedCapabilities: string[];
  removedCapabilities: string[];
  affectedEnvironmentCount: number;
  affectedEnvironmentSamples: DeploymentTierEnvironmentSample[];
  changedSafeguards: string[];
};

export type WorkflowEngineRegistration = {
  id: string;
  name: string;
  environmentId: string;
  endpoint: {
    baseUrl: string;
    region: string;
    version: string;
    certificateStatus: "Trusted" | "Untrusted" | "Expiring";
  };
  credentialReference: {
    provider: string;
    reference: string;
    verificationStatus: CredentialVerificationStatus;
    lastVerifiedAt: string | null;
  };
  health: DeploymentHealth;
  lastHeartbeatAt: string | null;
  lastVerificationAt: string | null;
  verificationMessage: string;
  capabilities: EngineCapability[];
  controls: RuntimeControl[];
  hostingProvider: string | null;
};

export type EngineHealthResult = {
  engineId: string;
  environmentId: string;
  health: DeploymentHealth;
  version: string | null;
  certificateStatus: WorkflowEngineRegistration["endpoint"]["certificateStatus"];
  credentialVerificationStatus: CredentialVerificationStatus;
  credentialLastVerifiedAt: string | null;
  lastHeartbeatAt: string | null;
  lastVerificationAt: string | null;
  message: string;
};

export type DesiredStateRevision = {
  id: string;
  revision: number;
  commit: string;
  label: string;
  authoredAt: string;
};

export type EnvironmentSummary = {
  id: string;
  name: string;
  tier: "Dev" | "Test" | "Stage" | "Production";
  tierId?: string | null;
  tierName?: string;
  tierStatus?: DeploymentTierStatus;
  tierCapabilities?: string[];
  health: DeploymentHealth;
  desiredRevision: DesiredStateRevision;
  deployedRevision: number | null;
  deploymentStatus: DeploymentStatus;
  driftStatus: DriftStatus;
  engineIds: string[];
};

export type WorkflowApplication = {
  id: string;
  name: string;
  workspaceName: string;
  environments: EnvironmentSummary[];
};

export type DeploymentDiffItem = {
  id: string;
  category: DiffCategory;
  name: string;
  sourceValue: string;
  targetValue: string;
  impact: "Added" | "Changed" | "Removed";
};

export type DeploymentValidation = {
  id: string;
  severity: ValidationSeverity;
  scope: string;
  message: string;
};

export type PromotionArtifactDigest = {
  algorithm: string;
  value: string;
};

export type PromotionArtifactDescriptor = {
  artifactRecordId: string | null;
  artifactId: string | null;
  artifactTypeId: string | null;
  contentDigest: PromotionArtifactDigest | null;
  metadata: Record<string, string>;
  configuration: Record<string, string>;
  compatibilityHints: string[];
};

export type PromotionArtifactComparison = {
  name: string;
  source: PromotionArtifactDescriptor | null;
  target: PromotionArtifactDescriptor | null;
  impact: "Added" | "Changed" | "Removed" | "Unchanged";
  runtimeCompatibility: DeploymentValidation[];
};

export type PromotionComparison = {
  sourceEnvironmentId: string;
  targetEnvironmentId: string;
  sourceRevisionId: string;
  sourceRevision: number;
  targetRevision: number;
  diff: DeploymentDiffItem[];
  validations: DeploymentValidation[];
  rollbackRevision: number | null;
  rollbackRevisionId: string | null;
  artifacts: PromotionArtifactComparison[];
};

export type DesiredStateRecordKind =
  | "Workflow"
  | "Feature"
  | "ShellConfiguration"
  | "RuntimeConfiguration"
  | "SecretReference"
  | "ObservabilityBinding"
  | "EngineBinding"
  | "ArtifactReference";

export type WorkspaceDesiredStateRecordRequest = {
  kind: DesiredStateRecordKind;
  name: string;
  payload: unknown;
};

export type CreateDesiredStateRevisionRequest = {
  label: string;
  commit: string | null;
  records: WorkspaceDesiredStateRecordRequest[];
};

export type WorkspaceDesiredStateRevision = {
  id: string;
  workspaceId: string;
  applicationId: string;
  environmentId: string;
  revisionNumber: number;
  label: string;
  commit: string | null;
  contentHash: string;
  desiredStateJson: string;
  authoredAt: string;
  createdAt: string;
  createdByAccountId: string | null;
};

export type WorkspaceDesiredStateRevisionSummary = {
  revision: WorkspaceDesiredStateRevision;
  environmentName: string;
  environmentTier: EnvironmentSummary["tier"];
  environmentTierId: string | null;
  environmentTierName: string | null;
  isCurrentDesired: boolean;
  isCurrentDeployed: boolean;
  latestRunStatus: WorkspaceDeploymentRunStatus | null;
  latestRunQueuedAt: string | null;
};

export type WorkspaceDesiredStateRevisionRecord = {
  id: string;
  kind: DesiredStateRecordKind;
  name: string;
  payloadJson: string;
  contentHash: string;
  artifactRecordId: string | null;
  artifactId: string | null;
  artifactTypeId: string | null;
  artifactDigest: WorkspaceArtifactDigest | null;
};

export type WorkspaceDesiredStateRevisionRunSummary = {
  id: string;
  environmentId: string;
  engineId: string;
  status: WorkspaceDeploymentRunStatus;
  validationOutcome: "Passed" | "Warnings" | "Blocked";
  queuedAt: string;
  completedAt: string | null;
  failureMessage: string | null;
};

export type WorkspaceDesiredStateRevisionDetail = {
  summary: WorkspaceDesiredStateRevisionSummary;
  records: WorkspaceDesiredStateRevisionRecord[];
  runs: WorkspaceDesiredStateRevisionRunSummary[];
};

export type WorkspaceApplicationRevisionsResponse = {
  items: WorkspaceDesiredStateRevisionSummary[];
};

export type PromotionPreviewRequest = {
  sourceEnvironmentId: string;
  targetEnvironmentId: string;
  sourceRevisionId: string;
  targetEngineId: string;
};

export type PromotionRequest = PromotionPreviewRequest & {
  label: string;
  commit: string | null;
};

export type PromotionResult = {
  sourceRevision: WorkspaceDesiredStateRevision;
  targetRevision: WorkspaceDesiredStateRevision;
  comparison: PromotionComparison;
};

export type ConfirmationActionType = "Deploy" | "Rollback" | "RuntimeControl";
export type RuntimeControlExecutionStatus = "Succeeded" | "Failed";

export type ActionConfirmation = {
  id: string;
  workspaceId: string;
  actionType: ConfirmationActionType;
  targetId: string;
  confirmedByAccountId: string;
  confirmedAt: string;
  expiresAt: string;
  usedAt: string | null;
};

export type CreateActionConfirmationRequest = {
  actionType: ConfirmationActionType;
  targetId: string;
  lifetimeSeconds: number | null;
};

export type QueueDeploymentRunRequest = {
  sourceRevisionId: string;
  targetEnvironmentId: string;
  targetEngineId: string;
  confirmationId: string;
  mode: "DryRun" | "Apply";
};

export type QueueRollbackRunRequest = QueueDeploymentRunRequest & {
  rollbackSourceRunId: string;
};

export type WorkspaceDeploymentRun = {
  id: string;
  workspaceId: string;
  applicationId: string;
  environmentId: string;
  engineId: string;
  sourceRevisionId: string;
  previousDeployedRevisionId: string | null;
  rollbackSourceRunId: string | null;
  status: WorkspaceDeploymentRunStatus;
  validationOutcome: "Passed" | "Warnings" | "Blocked";
  confirmationId: string;
  actorAccountId: string;
  queuedAt: string;
  startedAt: string | null;
  completedAt: string | null;
  createdAt: string;
  workerId: string | null;
  workerHeartbeatAt: string | null;
  attemptNumber: number;
  recoveryReason: string | null;
  failureMessage: string | null;
};

export type DeploymentRunHistoryRecord = {
  id: string;
  workspaceId: string;
  runId: string;
  status: WorkspaceDeploymentRunStatus;
  message: string;
  createdAt: string;
};

export type WorkspaceArtifactDigest = {
  algorithm: string;
  value: string;
};

export type DeploymentCommandDiagnostic = {
  code: string;
  severity: "Info" | "Warning" | "Error";
  message: string;
};

export type DeploymentCommandArtifactReference = {
  artifactRecordId: string | null;
  artifactId: string;
  artifactTypeId: string;
  contentDigest: WorkspaceArtifactDigest;
};

export type DeploymentRunCommandSummary = {
  id: string;
  workspaceId: string;
  runId: string;
  environmentId: string;
  engineId: string;
  action: DeploymentCommandAction;
  status: DeploymentCommandStatus;
  artifact: DeploymentCommandArtifactReference | null;
  workerId: string | null;
  claimedAt: string | null;
  leaseExpiresAt: string | null;
  heartbeatAt: string | null;
  attemptNumber: number;
  percentComplete: number | null;
  progressMessage: string | null;
  observedArtifactDigest: WorkspaceArtifactDigest | null;
  runtimeReference: string | null;
  diagnostics: DeploymentCommandDiagnostic[];
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
};

export type WorkspaceDeploymentRunDetailResponse = {
  run: WorkspaceDeploymentRun;
  history: DeploymentRunHistoryRecord[];
  commands: DeploymentRunCommandSummary[];
};

export type RuntimeControlExecution = {
  id: string;
  workspaceId: string;
  engineId: string;
  environmentId: string;
  controlId: string;
  controlLabel: string;
  boundary: CapabilityBoundary;
  requiredCapabilityId: string;
  confirmationId: string;
  actorAccountId: string;
  status: RuntimeControlExecutionStatus;
  createdAt: string;
  message: string;
};

export type RuntimeControlRunRequest = {
  confirmationId: string;
};

export type ObservabilityBinding = {
  id: string;
  kind: "Logs" | "Traces" | "Metrics" | "Console";
  provider: string;
  status: "Connected" | "Degraded" | "Unavailable";
  scope: string;
  correlatedRevision: number;
  sample: string;
};

export type DeploymentHistoryEvent = {
  id: string;
  status: DeploymentStatus | WorkspaceDeploymentRunStatus;
  revision: number;
  actor: string;
  environmentId: string;
  engineId: string;
  validationOutcome: "Passed" | "Warnings" | "Blocked";
  occurredAt: string;
  rollbackSourceRevision: number | null;
  commands?: DeploymentRunCommandSummary[];
};

export type DriftReportItem = {
  id: string;
  environmentId: string;
  engineId: string;
  area: string;
  desired: string;
  observed: string;
  action: "Review" | "Redeploy" | "Import";
};

export type AssistantPlan = {
  id: string;
  version: number;
  status: AssistantPlanStatus;
  workspaceName: string;
  targetEnvironmentId: string;
  targetEngineId: string;
  summary: string;
  proposedActions: string[];
  executedActions: string[];
  validations: DeploymentValidation[];
  rollbackPath: string;
  allOrNothing: boolean;
  createdAt: string;
};

export type DeploymentCockpit = {
  applications: WorkflowApplication[];
  engines: WorkflowEngineRegistration[];
  comparisons: PromotionComparison[];
  observabilityBindings: ObservabilityBinding[];
  history: DeploymentHistoryEvent[];
  driftReport: DriftReportItem[];
  assistantPlans: AssistantPlan[];
};

export type WorkspaceDeploymentPermissionsResponse = {
  permissions: DeploymentPermission[];
};

export type WorkspaceDeploymentTierCapabilitiesResponse = {
  capabilities: DeploymentTierCapability[];
};

export type WorkspaceDeploymentTiersResponse = {
  tiers: WorkspaceDeploymentTier[];
};

export type DesiredStateRequirementApplicability = "CurrentTier" | "ContextualFix" | "Optional";

export type DesiredStateRequirement = {
  id: string;
  capabilityId: string | null;
  recordKind: DesiredStateRecordKind;
  label: string;
  description: string;
  validationId: string;
  required: boolean;
  applicability: DesiredStateRequirementApplicability;
};

export type WorkspaceDesiredStateRequirementsResponse = {
  environmentId: string;
  environmentName: string;
  tierName: string;
  tierCapabilities: string[];
  requirements: DesiredStateRequirement[];
};

export type WorkspaceDeploymentTierRequest = {
  name: string;
  description: string | null;
  sortOrder: number;
  capabilities: string[];
  impactAccepted?: boolean;
};

export type WorkspaceDeploymentTierImpactPreviewRequest = {
  capabilities: string[];
};

export type CreateDeploymentApplicationRequest = {
  name: string;
  description: string | null;
};

export type UpdateDeploymentApplicationRequest = CreateDeploymentApplicationRequest;

export type CreateDeploymentEnvironmentRequest = {
  name: string;
  tier?: EnvironmentSummary["tier"];
  tierId?: string | null;
};

export type UpdateDeploymentEnvironmentRequest = CreateDeploymentEnvironmentRequest;

export type RegisterDeploymentEngineRequest = {
  name: string;
  baseUrl: string;
  region: string | null;
  credentialProvider?: string | null;
  credentialReference?: string | null;
  credentialReferenceId?: string | null;
  capabilities: EngineCapability[];
  controls: RuntimeControl[];
  hostingProvider: string | null;
};

export type UpdateDeploymentEngineRequest = RegisterDeploymentEngineRequest;

export type DeploymentSecretStoreStatus = "Active" | "Archived";

export type WorkspaceDeploymentSecretStore = {
  id: string;
  workspaceId: string;
  name: string;
  provider: string;
  description: string | null;
  status: DeploymentSecretStoreStatus;
  createdAt: string;
  updatedAt: string;
  createdByAccountId: string | null;
  updatedByAccountId: string | null;
  archivedAt: string | null;
  archivedByAccountId: string | null;
};

export type WorkspaceDeploymentCredentialReference = {
  id: string;
  workspaceId: string;
  secretStoreId: string;
  secretStoreName: string;
  secretStoreProvider: string;
  name: string;
  reference: string;
  description: string | null;
  status: DeploymentSecretStoreStatus;
  verificationStatus: CredentialVerificationStatus;
  lastVerifiedAt: string | null;
  createdAt: string;
  updatedAt: string;
  createdByAccountId: string | null;
  updatedByAccountId: string | null;
  archivedAt: string | null;
  archivedByAccountId: string | null;
};

export type WorkspaceDeploymentSecretStoresResponse = {
  items: WorkspaceDeploymentSecretStore[];
};

export type WorkspaceDeploymentSecretStoreRequest = {
  name: string;
  provider: string;
  description: string | null;
};

export type WorkspaceDeploymentCredentialReferencesResponse = {
  items: WorkspaceDeploymentCredentialReference[];
};

export type WorkspaceDeploymentCredentialReferenceRequest = {
  name: string;
  reference: string;
  description: string | null;
};

export type CreatedDeploymentApplication = {
  id: string;
  workspaceId: string;
  name: string;
};

export type CreatedDeploymentEnvironment = {
  id: string;
  workspaceId: string;
  applicationId: string;
  name: string;
  tierId: string | null;
};

export function environmentLabel(environmentId: string, applications: WorkflowApplication[]) {
  for (const application of applications) {
    const environment = application.environments.find((item) => item.id === environmentId);
    if (environment) return environment.name;
  }
  return environmentId;
}

export function engineLabel(engineId: string, engines: WorkflowEngineRegistration[]) {
  return engines.find((engine) => engine.id === engineId)?.name ?? engineId;
}

export function hasBlockingValidation(validations: DeploymentValidation[]) {
  return validations.some((validation) => validation.severity === "Blocker");
}

export function supportedControlIds(engine: WorkflowEngineRegistration) {
  const capabilities = new Set(engine.capabilities.map((capability) => capability.id));
  return engine.controls.filter((control) => capabilities.has(control.capabilityId));
}
