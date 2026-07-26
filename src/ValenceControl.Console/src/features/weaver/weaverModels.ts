export type WeaverMode = "Inspect" | "Plan" | "Operate";
export type WeaverProviderMode = "Disabled" | "GitHubCopilot" | "BringYourOwnKey" | "Fake";
export type WeaverSessionStatus = "Active" | "WaitingForUser" | "WaitingForApproval" | "Executing" | "Completed" | "Failed" | "Canceled" | "Archived";
export type WeaverMessageRole = "User" | "Assistant" | "System" | "Tool";
export type WeaverRedactionState = "None" | "Redacted" | "Omitted";
export type WeaverToolCallStatus = "Started" | "Succeeded" | "Failed" | "Denied" | "Canceled";
export type WeaverToolAuthorizationResult = "Allowed" | "Denied" | "RequiresApproval";
export type WeaverPlanStatus = "Draft" | "Blocked" | "ReadyForApproval" | "Approved" | "Rejected" | "Executing" | "Succeeded" | "Failed" | "Canceled";
export type WeaverPlanType = "Deployment" | "Promotion" | "Rollback" | "RuntimeControl" | "EngineRegistration" | "SecretReference" | "SetupGuidance";
export type WeaverPlanRisk = "Low" | "Medium" | "High";

export type WorkspaceWeaverConfiguration = {
  enabled: boolean;
  providerMode: WeaverProviderMode;
  model: string;
  reasoningEffort?: string | null;
  streamingEnabled: boolean;
  modes: WeaverMode[];
  disabledReason?: string | null;
};

export type WorkspaceWeaverCreateSessionRequest = {
  routePath?: string | null;
  mode: WeaverMode;
  context: Record<string, string>;
};

export type WorkspaceWeaverSession = {
  id: string;
  status: WeaverSessionStatus;
  mode: WeaverMode;
  createdAt: string;
};

export type WorkspaceWeaverSendMessageRequest = {
  prompt: string;
  mode: WeaverMode;
  delivery: "Immediate";
};

export type WorkspaceWeaverSendMessageResponse = {
  messageId: string;
  assistantMessageId?: string | null;
  sessionStatus: WeaverSessionStatus;
};

export type WorkspaceWeaverPlanApprovalRequest = {
  version: number;
  decision: "Approved" | "Rejected";
  confirmationId?: string | null;
  reason?: string | null;
};

export type WorkspaceWeaverPlanApprovalResponse = {
  planId: string;
  version: number;
  status: WeaverPlanStatus;
};

export type WorkspaceWeaverPlanExecuteRequest = {
  version: number;
};

export type WorkspaceWeaverPlanExecuteResponse = {
  executionId: string;
  status: "Queued" | "Running" | "Succeeded" | "Failed" | "Canceled" | "Compensated";
  linkedResourceJson: string;
};

export type WorkspaceWeaverSessionDetail = {
  session: WorkspaceWeaverSession;
  messages: WorkspaceWeaverMessage[];
  toolCalls: WorkspaceWeaverToolCall[];
  plans: WorkspaceWeaverPlan[];
};

export type WorkspaceWeaverMessage = {
  id: string;
  role: WeaverMessageRole;
  content: string;
  redactionState: WeaverRedactionState;
  sequence: number;
  createdAt: string;
};

export type WorkspaceWeaverToolCall = {
  id: string;
  toolName: string;
  resultSummaryJson?: string | null;
  authorizationResult: WeaverToolAuthorizationResult;
  status: WeaverToolCallStatus;
  durationMilliseconds?: number | null;
  createdAt: string;
  completedAt?: string | null;
};

export type WorkspaceWeaverPlan = {
  id: string;
  version: number;
  planType: WeaverPlanType;
  title: string;
  summary: string;
  targetJson: string;
  impactJson: string;
  validationJson: string;
  rollbackJson?: string | null;
  risk: WeaverPlanRisk;
  status: WeaverPlanStatus;
  createdAt: string;
  updatedAt: string;
};
