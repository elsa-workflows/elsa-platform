export type DeploymentHealth = "Healthy" | "Degraded" | "Unreachable";
export type DriftStatus = "InSync" | "DriftDetected" | "Unknown";
export type DeploymentStatus = "Succeeded" | "Running" | "Blocked" | "RolledBack";
export type CredentialVerificationStatus = "Verified" | "Missing" | "Expired" | "Unverified";
export type CapabilityBoundary = "Workflow" | "EngineApi" | "Shell" | "Hosting";
export type ValidationSeverity = "Pass" | "Warning" | "Blocker";
export type DiffCategory = "Workflows" | "Features" | "ShellConfiguration" | "RuntimeConfiguration" | "SecretReferences" | "Observability" | "EngineBindings";
export type AssistantPlanStatus = "Proposed" | "Approved" | "Rejected" | "Executed";

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
