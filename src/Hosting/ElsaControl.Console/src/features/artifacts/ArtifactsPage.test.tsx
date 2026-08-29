import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { AuthProvider } from "@/lib/auth/AuthProvider";
import { WorkspaceContextProvider } from "@/app/WorkspaceContextProvider";
import { ArtifactDetailsPage, ArtifactsPage } from "@/features/artifacts/ArtifactsPage";
import type { WorkspaceArtifact } from "@/features/artifacts/artifactModels";

const workspaceId = "00000000-0000-0000-0000-000000000010";
const artifact = artifactFixture();

afterEach(() => vi.unstubAllGlobals());

describe("ArtifactsPage", () => {
  it("lists registered artifact metadata and links to details", async () => {
    installFetchMock();
    renderPage(<ArtifactsPage />, "/admin/artifacts");

    expect(await screen.findByRole("link", { name: "Payment Retry 4.2.0" })).toHaveAttribute("href", `/admin/artifacts/${artifact.id}`);
    expect(screen.getByText("sha256:payment-retry")).toBeInTheDocument();
    expect(screen.getByText("Valid")).toBeInTheDocument();
    expect(screen.getByText("Verified")).toBeInTheDocument();
  });

  it("shows safe artifact details and exposes a workspace-scoped download", async () => {
    installFetchMock();
    renderPage(<ArtifactDetailsPage />, `/admin/artifacts/${artifact.id}`);

    expect(await screen.findByRole("heading", { name: "Payment Retry 4.2.0" })).toBeInTheDocument();
    expect(screen.getByText("sha256:abc123")).toBeInTheDocument();
    expect(screen.getByText("Payment Retry warning")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Download ZIP" })).toHaveAttribute(
      "href",
      `/api/workspaces/${workspaceId}/artifacts/${artifact.id}/download`
    );
    expect(screen.queryByText("/srv/artifacts/payment-retry.zip")).not.toBeInTheDocument();
  });
});

function renderPage(element: ReactNode, initialEntry: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <AuthProvider>
          <WorkspaceContextProvider>
            <Routes>
              <Route path="/admin/artifacts" element={element} />
              <Route path="/admin/artifacts/:artifactId" element={element} />
            </Routes>
          </WorkspaceContextProvider>
        </AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

function installFetchMock() {
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
    const url = input instanceof Request ? input.url : input.toString();
    if (url.endsWith("/api/auth/session")) {
      return Response.json({ loginEnabled: true, authenticated: true, displayName: "Test User", email: "test@example.com", loginPath: "/api/auth/login", logoutPath: "/api/auth/logout" });
    }
    if (url.endsWith("/api/me/organizations")) {
      return Response.json({
        account: { id: "account-1", displayName: "Test User", email: "test@example.com" },
        organizations: [{ id: "organization-1", name: "Acme Corp", role: "Owner" }],
        workspaces: [{ id: workspaceId, name: "Acme Insurance", kind: "Shared", role: "Owner", organizationId: "organization-1", organizationName: "Acme Corp", organizationRole: "Owner" }]
      });
    }
    if (url.endsWith(`/api/workspaces/${workspaceId}/artifacts`)) return Response.json({ items: [artifact] });
    if (url.endsWith(`/api/workspaces/${workspaceId}/artifacts/${artifact.id}`)) return Response.json(artifact);
    if (url.endsWith(`/api/workspaces/${workspaceId}/deployments/permissions`)) return Response.json({ permissions: ["deployments.read", "deployments.setup.manage"] });
    return Response.json({ title: "Not found" }, { status: 404 });
  }));
}

function artifactFixture(): WorkspaceArtifact {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    workspaceId,
    artifactId: "sha256:payment-retry",
    layoutVersion: "elsa-control/artifact-layout/v1alpha1",
    contentDigest: { algorithm: "sha256", value: "abc123" },
    format: "Zip",
    referenceProvider: "local",
    reference: "/srv/artifacts/payment-retry.zip",
    manifest: { name: "Payment Retry", version: "4.2.0", environment: "Development" },
    resources: [{ type: "Workflow", logicalId: "payment-retry", scope: null, version: "1", desiredStateHash: null }],
    checksumStatus: "Verified",
    inspectionStatus: "Valid",
    diagnostics: [{ code: "artifact.warning", severity: "Warning", message: "Payment Retry warning" }],
    registeredAt: "2026-08-29T08:00:00Z",
    registeredByAccountId: "account-1",
    lastInspectedAt: "2026-08-29T08:01:00Z",
    createdAt: "2026-08-29T08:00:00Z",
    updatedAt: "2026-08-29T08:01:00Z",
    status: "Active",
    artifactTypeId: "elsa.workflow-definition",
    artifactSchemaVersion: "1.0",
    displayMetadata: { name: "Payment Retry", version: "4.2.0", description: null, labels: {}, annotations: {}, source: null },
    payloadReference: { provider: "local", uri: "/srv/artifacts/payment-retry.zip", mediaType: "application/zip", sizeBytes: 1024 }
  };
}
