export type WorkspaceArtifactFormat = "Folder" | "Zip" | "Unknown";
export type WorkspaceArtifactChecksumStatus = "Unverified" | "Verified" | "Missing" | "Mismatched" | "Unexpected" | "Unavailable";
export type WorkspaceArtifactInspectionStatus = "NeverInspected" | "Valid" | "Invalid" | "Unavailable" | "Unsupported";
export type WorkspaceArtifactDiagnosticSeverity = "Info" | "Warning" | "Error";

export type WorkspaceArtifactDigest = {
  algorithm: string;
  value: string;
};

export type WorkspaceArtifactManifestSummary = {
  name: string | null;
  version: string | null;
  environment: string | null;
};

export type WorkspaceArtifactResourceSummary = {
  type: string;
  logicalId: string;
  scope: string | null;
  version: string | null;
  desiredStateHash: WorkspaceArtifactDigest | null;
};

export type WorkspaceArtifactDiagnostic = {
  code: string;
  severity: WorkspaceArtifactDiagnosticSeverity;
  message: string;
};

export type WorkspaceArtifact = {
  id: string;
  workspaceId: string;
  artifactId: string;
  layoutVersion: string;
  contentDigest: WorkspaceArtifactDigest;
  format: WorkspaceArtifactFormat;
  referenceProvider: string;
  reference: string;
  manifest: WorkspaceArtifactManifestSummary;
  resources: WorkspaceArtifactResourceSummary[];
  checksumStatus: WorkspaceArtifactChecksumStatus;
  inspectionStatus: WorkspaceArtifactInspectionStatus;
  diagnostics: WorkspaceArtifactDiagnostic[];
  registeredAt: string;
  registeredByAccountId: string;
  lastInspectedAt: string | null;
  createdAt: string;
  updatedAt: string;
};

export type WorkspaceArtifactListResponse = {
  items: WorkspaceArtifact[];
};

export type WorkspaceArtifactRegistrationRequest = {
  artifactId: string;
  layoutVersion: string;
  contentDigest: WorkspaceArtifactDigest;
  format: WorkspaceArtifactFormat;
  referenceProvider: string;
  reference: string;
  manifest: WorkspaceArtifactManifestSummary;
  resources: WorkspaceArtifactResourceSummary[];
  diagnostics: WorkspaceArtifactDiagnostic[];
};

export type WorkspaceArtifactInspectionResult = {
  artifactRecordId: string;
  artifactId: string;
  checksumStatus: WorkspaceArtifactChecksumStatus;
  inspectionStatus: WorkspaceArtifactInspectionStatus;
  lastInspectedAt: string | null;
  resourceCount: number;
  resources: WorkspaceArtifactResourceSummary[];
  diagnostics: WorkspaceArtifactDiagnostic[];
};
