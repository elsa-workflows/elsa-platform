import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { WorkspaceContextProvider } from "@/app/WorkspaceContextProvider";
import { ArtifactsPage } from "@/features/artifacts/ArtifactsPage";
import type { WorkspaceArtifact, WorkspaceArtifactListResponse } from "@/features/artifacts/artifactModels";
import { AuthProvider } from "@/lib/auth/AuthProvider";

describe("ArtifactsPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders registered artifacts without payload content", async () => {
    renderArtifacts();

    expect(await screen.findByRole("heading", { name: "Artifacts" })).toBeInTheDocument();
    expect(screen.getAllByText("sha256:claims-prod").length).toBeGreaterThan(0);
    expect(screen.getAllByText("elsa.workflow-definition").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/Elsa Studio/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/claims v1.0.0/i)).toBeInTheDocument();
    expect(screen.queryByText(/workflow definition payload|secret value|token/i)).not.toBeInTheDocument();
  });

  it("registers artifact metadata through the live API", async () => {
    const fetchMock = renderArtifacts({ items: [] });

    await screen.findByText("No artifacts registered");
    await userEvent.click(screen.getByRole("button", { name: "Register artifact" }));
    await userEvent.clear(screen.getByLabelText("Artifact identity"));
    await userEvent.type(screen.getByLabelText("Artifact identity"), "sha256:new-artifact");
    await userEvent.click(screen.getByRole("button", { name: "Save artifact" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/artifacts`),
        expect.objectContaining({ method: "POST" })
      )
    );
    expect(await screen.findByText("sha256:new-artifact")).toBeInTheDocument();
  });

  it("refreshes artifact inspection state", async () => {
    const fetchMock = renderArtifacts();

    await screen.findByRole("heading", { name: "Artifacts" });
    await userEvent.click(screen.getByRole("button", { name: "Refresh inspection" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/artifacts/artifact-1/refresh`),
        expect.objectContaining({ method: "POST" })
      )
    );
    expect((await screen.findAllByText("Invalid")).length).toBeGreaterThan(0);
    expect(screen.getByText(/Referenced artifact identity does not match/i)).toBeInTheDocument();
  });
});

function renderArtifacts(response: WorkspaceArtifactListResponse = { items: [artifactFixture] }) {
  const fetchMock = createFetchMock(response);
  vi.stubGlobal("fetch", fetchMock);
  render(
    <TestQueryProvider>
      <AuthProvider>
        <WorkspaceContextProvider>
          <ArtifactsPage />
        </WorkspaceContextProvider>
      </AuthProvider>
    </TestQueryProvider>
  );
  return fetchMock;
}

function createFetchMock(initial: WorkspaceArtifactListResponse) {
  let list = initial;
  return vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = input instanceof Request ? input.url : input.toString();
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    if (url.endsWith("/api/auth/session"))
      return jsonResponse({ loginEnabled: true, authenticated: true, displayName: "Test User", email: "test@example.com", loginPath: "/api/auth/login", logoutPath: "/api/auth/logout" });
    if (url.endsWith("/api/me/organizations"))
      return jsonResponse(workspaceContextFixture());
    if (url.endsWith(`/api/workspaces/${workspaceId}/deployments/permissions`)) {
      return jsonResponse({ permissions: ["deployments.read", "deployments.setup.manage"] });
    }
    if (method === "GET" && url.endsWith(`/api/workspaces/${workspaceId}/artifacts`)) {
      return jsonResponse(list);
    }
    if (method === "GET" && url.endsWith(`/api/workspaces/${workspaceId}/artifacts/types`)) {
      return jsonResponse({
        items: [
          {
            typeId: "elsa.workflow-definition",
            displayName: "Elsa Workflow Definition",
            description: "Workflow definition artifact",
            ownedBy: "platform",
            supportedSchemaVersions: ["1.0"],
            enabled: true,
            defaultRuntimeFamily: "elsa-workflows",
            defaultRequiredCapabilities: ["workflow-definition.apply"]
          }
        ]
      });
    }
    if (method === "GET" && url.endsWith(`/api/workspaces/${workspaceId}/artifacts/artifact-1`)) {
      return jsonResponse(list.items[0]);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/artifacts`)) {
      const body = JSON.parse(init?.body?.toString() ?? "{}") as WorkspaceArtifact;
      const artifact = { ...artifactFixture, id: "artifact-new", artifactId: body.artifactId };
      list = { items: [artifact] };
      return jsonResponse(artifact, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/artifacts/artifact-1/refresh`)) {
      list = {
        items: [
          {
            ...artifactFixture,
            inspectionStatus: "Invalid",
            checksumStatus: "Mismatched",
            diagnostics: [
              {
                code: "artifact.identity.mismatch",
                severity: "Error",
                message: "Referenced artifact identity does not match the registered identity."
              }
            ]
          }
        ]
      };
      return jsonResponse({
        artifactRecordId: "artifact-1",
        artifactId: "sha256:claims-prod",
        checksumStatus: "Mismatched",
        inspectionStatus: "Invalid",
        lastInspectedAt: "2026-05-26T10:10:00Z",
        resourceCount: 1,
        resources: artifactFixture.resources,
        diagnostics: list.items[0].diagnostics
      });
    }
    return jsonResponse({ title: "Not found" }, 404);
  });
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function workspaceContextFixture() {
  return {
    account: { id: "account-1", displayName: "Test User", email: "test@example.com" },
    organizations: [{ id: organizationId, name: "Acme Corp", role: "Owner" }],
    workspaces: [
      { id: workspaceId, name: "Acme Insurance", kind: "Shared", role: "Owner", organizationId, organizationName: "Acme Corp", organizationRole: "Owner" }
    ]
  };
}

function TestQueryProvider({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false }
    }
  });

  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

const organizationId = "00000000-0000-0000-0000-000000000001";
const workspaceId = "00000000-0000-0000-0000-000000000010";

const artifactFixture: WorkspaceArtifact = {
  id: "artifact-1",
  workspaceId,
  artifactId: "sha256:claims-prod",
  layoutVersion: "platform.elsa.io/deployment-artifact/v1alpha1",
  contentDigest: { algorithm: "sha256", value: "claims-prod" },
  format: "Zip",
  referenceProvider: "local",
  reference: "local:///tmp/claims-prod.zip",
  manifest: { name: "claims", version: "1.0.0", environment: "prod" },
  resources: [
    {
      type: "workflowDefinition",
      logicalId: "payment-retry",
      scope: null,
      version: "8",
      desiredStateHash: { algorithm: "sha256", value: "workflow-hash" }
    }
  ],
  checksumStatus: "Verified",
  inspectionStatus: "Valid",
  diagnostics: [],
  registeredAt: "2026-05-26T10:00:00Z",
  registeredByAccountId: "account-1",
  lastInspectedAt: "2026-05-26T10:05:00Z",
  createdAt: "2026-05-26T10:00:00Z",
  updatedAt: "2026-05-26T10:05:00Z",
  envelopeVersion: "platform.elsa.io/artifact-envelope/v1alpha1",
  artifactTypeId: "elsa.workflow-definition",
  artifactSchemaVersion: "1.0",
  manifestDigest: { algorithm: "sha256", value: "claims-manifest" },
  payloadReference: {
    provider: "producer-managed",
    uri: "studio://workflows/claims/versions/1.0.0",
    mediaType: "application/vnd.elsa.workflow-definition+json",
    sizeBytes: 1234,
    referenceDigest: null,
    expiresAt: null
  },
  producer: {
    producerType: "studio",
    producerName: "Elsa Studio",
    producerVersion: "4.0.0",
    sourceReference: "workflow:claims"
  },
  displayMetadata: {
    name: "claims",
    version: "1.0.0",
    description: "Claims workflow",
    labels: { domain: "claims" },
    annotations: {},
    source: "prod"
  },
  compatibilityHints: [
    {
      requiredArtifactType: "elsa.workflow-definition",
      runtimeFamily: "elsa-workflows",
      runtimeVersionRange: ">=4.0.0",
      requiredCapabilities: ["workflow-definition.apply"],
      environmentConstraints: {}
    }
  ]
};
