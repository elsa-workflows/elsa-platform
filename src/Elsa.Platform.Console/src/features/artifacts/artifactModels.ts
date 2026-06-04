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

export type ArtifactPayloadReference = {
  provider: string;
  uri: string;
  mediaType: string | null;
  sizeBytes: number | null;
  referenceDigest: WorkspaceArtifactDigest | null;
  expiresAt: string | null;
};

export type ArtifactProducer = {
  producerType: string;
  producerName: string;
  producerVersion: string | null;
  sourceReference: string | null;
};

export type ArtifactDisplayMetadata = {
  name: string | null;
  version: string | null;
  description: string | null;
  labels: Record<string, string>;
  annotations: Record<string, string>;
  source: string | null;
};

export type ArtifactCompatibilityHint = {
  requiredArtifactType: string;
  runtimeFamily: string | null;
  runtimeVersionRange: string | null;
  requiredCapabilities: string[];
  environmentConstraints: Record<string, string>;
};

export type ArtifactTypeDefinition = {
  typeId: string;
  displayName: string;
  description: string;
  ownedBy: string;
  supportedSchemaVersions: string[];
  enabled: boolean;
  defaultRuntimeFamily: string | null;
  defaultRequiredCapabilities: string[] | null;
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
  envelopeVersion: string | null;
  artifactTypeId: string | null;
  artifactSchemaVersion: string | null;
  manifestDigest: WorkspaceArtifactDigest | null;
  payloadReference: ArtifactPayloadReference | null;
  producer: ArtifactProducer | null;
  displayMetadata: ArtifactDisplayMetadata | null;
  compatibilityHints: ArtifactCompatibilityHint[] | null;
};

export type WorkspaceArtifactListResponse = {
  items: WorkspaceArtifact[];
};

export type WorkspaceArtifactTypeListResponse = {
  items: ArtifactTypeDefinition[];
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
  envelopeVersion?: string | null;
  artifactTypeId?: string | null;
  artifactSchemaVersion?: string | null;
  manifestDigest?: WorkspaceArtifactDigest | null;
  payloadReference?: ArtifactPayloadReference | null;
  producer?: ArtifactProducer | null;
  displayMetadata?: ArtifactDisplayMetadata | null;
  compatibilityHints?: ArtifactCompatibilityHint[] | null;
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

export type WorkspaceArtifactUploadStatus = "Pending" | "Uploading" | "Uploaded" | "Inspecting" | "Completed" | "Failed" | "Aborted" | "Expired";

export type WorkspaceArtifactUploadCapabilitiesResponse = {
  maxUploadBytes: number;
  sampleArtifactGenerationEnabled: boolean;
};

export type CreateArtifactUploadRequest = {
  fileName: string;
  contentType: string | null;
  sizeBytes: number;
  idempotencyKey?: string | null;
};

export type CreateArtifactUploadResponse = {
  uploadId: string;
  status: WorkspaceArtifactUploadStatus;
  expiresAt: string;
  maxUploadBytes: number;
};

export type CompleteArtifactUploadResponse = {
  uploadId: string;
  status: WorkspaceArtifactUploadStatus;
  artifact: WorkspaceArtifact | null;
  created: boolean;
  diagnostics: WorkspaceArtifactDiagnostic[];
};

export type CreateSampleArtifactRequest = {
  artifactName: string;
  version: string;
  environment: string;
  workflowId: string;
};
