import { describe, expect, it } from "vitest";
import { artifactDiagnosticCount, artifactDigest, artifactDisplayName, artifactFormatLabel, type WorkspaceArtifact } from "@/features/artifacts/artifactModels";

const artifact: WorkspaceArtifact = {
  id: "artifact-record-1",
  workspaceId: "workspace-1",
  artifactId: "sha256:payment-retry",
  layoutVersion: "elsa-control/artifact-layout/v1alpha1",
  contentDigest: { algorithm: "sha256", value: "abc123" },
  format: "Zip",
  referenceProvider: "local",
  reference: "/srv/artifacts/payment-retry.zip",
  manifest: { name: "Payment Retry", version: "4.2.0", environment: "Development" },
  resources: [],
  checksumStatus: "Verified",
  inspectionStatus: "Valid",
  diagnostics: [
    { code: "artifact.warning", severity: "Warning", message: "A warning." },
    { code: "artifact.info", severity: "Info", message: "An info." }
  ],
  registeredAt: "2026-08-29T08:00:00Z",
  registeredByAccountId: "account-1",
  lastInspectedAt: "2026-08-29T08:01:00Z",
  createdAt: "2026-08-29T08:00:00Z",
  updatedAt: "2026-08-29T08:01:00Z",
  status: "Active"
};

describe("artifact models", () => {
  it("uses display metadata first and falls back to the manifest", () => {
    expect(artifactDisplayName(artifact)).toBe("Payment Retry 4.2.0");
    expect(artifactDigest(artifact)).toBe("sha256:abc123");
    expect(artifactFormatLabel(artifact.format)).toBe("ZIP");
    expect(artifactDiagnosticCount(artifact, "Warning")).toBe(1);

    expect(artifactDisplayName({
      ...artifact,
      displayMetadata: { name: "Display Name", version: "9", description: null, labels: {}, annotations: {}, source: null }
    })).toBe("Display Name 9");
  });
});
