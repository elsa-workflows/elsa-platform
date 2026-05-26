export type DeploymentHealth = "Healthy" | "Degraded" | "Unreachable";
export type DriftStatus = "InSync" | "DriftDetected" | "Unknown";
export type DeploymentStatus = "Succeeded" | "Running" | "Blocked" | "RolledBack";
export type WorkspaceDeploymentRunStatus = "Queued" | "Running" | "Succeeded" | "Failed" | "Blocked" | "Cancelled" | "RolledBack" | "RecoveryRequired";
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
  capabilities: EngineCapability[];
  controls: RuntimeControl[];
  hostingProvider: string | null;
};

export type DesiredStateRevision = {
  revision: number;
  commit: string;
  label: string;
  authoredAt: string;
};

export type EnvironmentSummary = {
  id: string;
  name: string;
  tier: "Dev" | "Test" | "Stage" | "Production";
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

export type PromotionComparison = {
  sourceEnvironmentId: string;
  targetEnvironmentId: string;
  sourceRevision: number;
  targetRevision: number;
  diff: DeploymentDiffItem[];
  validations: DeploymentValidation[];
  rollbackRevision: number | null;
};

export type DesiredStateRecordKind =
  | "Workflow"
  | "Feature"
  | "ShellConfiguration"
  | "RuntimeConfiguration"
  | "SecretReference"
  | "ObservabilityBinding"
  | "EngineBinding";

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
};

export type PromotionPreviewRequest = {
  sourceEnvironmentId: string;
  targetEnvironmentId: string;
  sourceRevisionId: string;
  targetEngineId: string;
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

export type WorkspaceDeploymentRunDetailResponse = {
  run: WorkspaceDeploymentRun;
  history: DeploymentRunHistoryRecord[];
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
  status: DeploymentStatus;
  revision: number;
  actor: string;
  environmentId: string;
  engineId: string;
  validationOutcome: "Passed" | "Warnings" | "Blocked";
  occurredAt: string;
  rollbackSourceRevision: number | null;
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

export type CreateDeploymentApplicationRequest = {
  name: string;
  description: string | null;
};

export type UpdateDeploymentApplicationRequest = CreateDeploymentApplicationRequest;

export type CreateDeploymentEnvironmentRequest = {
  name: string;
  tier: EnvironmentSummary["tier"];
};

export type UpdateDeploymentEnvironmentRequest = CreateDeploymentEnvironmentRequest;

export type RegisterDeploymentEngineRequest = {
  name: string;
  baseUrl: string;
  region: string | null;
  credentialProvider: string;
  credentialReference: string;
  capabilities: EngineCapability[];
  controls: RuntimeControl[];
  hostingProvider: string | null;
};

export type UpdateDeploymentEngineRequest = RegisterDeploymentEngineRequest;

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
