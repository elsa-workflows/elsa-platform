import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { WorkspaceContextProvider } from "@/app/WorkspaceContextProvider";
import { ArtifactCreatePage, ArtifactDetailsPage, ArtifactsPage } from "@/features/artifacts/ArtifactsPage";
import type { WorkspaceArtifact, WorkspaceArtifactListResponse } from "@/features/artifacts/artifactModels";
import { AuthProvider } from "@/lib/auth/AuthProvider";

describe("ArtifactsPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders registered artifacts without payload content", async () => {
    renderArtifacts();

    expect(await screen.findByRole("heading", { name: "Artifacts" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Upload artifact" })).toHaveAttribute("href", "/admin/artifacts/new");
    expect(screen.getByRole("link", { name: "sha256:claims-prod" })).toHaveAttribute("href", "/admin/artifacts/artifact-1");
    expect(screen.getByRole("link", { name: "Open details" })).toHaveAttribute("href", "/admin/artifacts/artifact-1");
    expect(screen.getByText("elsa.workflow-definition")).toBeInTheDocument();
    expect(screen.getByText(/Elsa Studio/i)).toBeInTheDocument();
    expect(screen.getByText(/claims v1.0.0/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Refresh inspection" })).not.toBeInTheDocument();
    expect(screen.queryByText("Compatibility")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Artifact identity")).not.toBeInTheDocument();
    expect(screen.queryByText(/workflow definition payload|secret value|token/i)).not.toBeInTheDocument();
  });

  it("shows upload guidance on the dedicated creation route", async () => {
    renderArtifacts({ items: [] }, "/admin/artifacts/new");

    expect(await screen.findByRole("heading", { name: "New artifact" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back to list" })).toHaveAttribute("href", "/admin/artifacts");
    expect(screen.getByRole("heading", { name: "Upload artifact package" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Upload ZIP" })).toBeDisabled();

    const file = new File(["artifact"], "claims-prod.zip", { type: "application/zip" });
    await userEvent.upload(screen.getByLabelText("Artifact package"), file);

    expect(screen.getAllByText("claims-prod.zip")).toHaveLength(2);
    expect(screen.getByText("8 B")).toBeInTheDocument();
    expect(screen.getByText("Waiting for a ZIP")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Upload ZIP" })).toBeEnabled();
  });

  it("uploads a zip artifact through the session flow", async () => {
    const fetchMock = renderArtifacts({ items: [] }, "/admin/artifacts/new");
    installSuccessfulXhrUpload();

    const file = new File(["artifact"], "claims-prod.zip", { type: "application/zip" });
    await screen.findByRole("heading", { name: "Upload artifact package" });
    await userEvent.upload(screen.getByLabelText("Artifact package"), file);
    await userEvent.click(screen.getByRole("button", { name: "Upload ZIP" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/artifact-uploads/upload-1/complete`),
        expect.objectContaining({ method: "POST" })
      )
    );
    expect(await screen.findByRole("heading", { name: "sha256:claims-prod" })).toBeInTheDocument();
  });

  it("registers artifact metadata through the dedicated creation route", async () => {
    const fetchMock = renderArtifacts({ items: [] }, "/admin/artifacts/new");

    await screen.findByRole("heading", { name: "Advanced metadata registration" });
    await userEvent.clear(screen.getByLabelText("Artifact identity"));
    await userEvent.type(screen.getByLabelText("Artifact identity"), "sha256:new-artifact");
    await userEvent.click(screen.getByRole("button", { name: "Save artifact" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/artifacts`),
        expect.objectContaining({ method: "POST" })
      )
    );
    expect(await screen.findByRole("heading", { name: "sha256:new-artifact" })).toBeInTheDocument();
  });

  it("renders artifact details on a dedicated route", async () => {
    renderArtifacts(undefined, "/admin/artifacts/artifact-1");

    expect(await screen.findByRole("heading", { name: "sha256:claims-prod" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Artifacts" })).toHaveAttribute("href", "/admin/artifacts");
    expect(screen.getByRole("button", { name: "Refresh inspection" })).toBeInTheDocument();
    expect(screen.getByText("Compatibility")).toBeInTheDocument();
    expect(screen.getByText(/studio:\/\/workflows\/claims\/versions\/1\.0\.0/)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Open details" })).not.toBeInTheDocument();
    expect(screen.queryByText(/workflow definition payload|secret value|token/i)).not.toBeInTheDocument();
  });

  it("links local artifact references to the download endpoint", async () => {
    renderArtifacts({ items: [localArtifactFixture] }, "/admin/artifacts/artifact-local");

    const link = await screen.findByRole("link", { name: "local · local:///tmp/claims-prod.zip" });

    expect(link).toHaveAttribute("href", `/api/workspaces/${workspaceId}/artifacts/artifact-local/download`);
    expect(link).toHaveAttribute("download");
  });

  it("refreshes artifact inspection state from the details route", async () => {
    const fetchMock = renderArtifacts(undefined, "/admin/artifacts/artifact-1");

    await screen.findByRole("heading", { name: "sha256:claims-prod" });
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

function renderArtifacts(response: WorkspaceArtifactListResponse = { items: [artifactFixture] }, route = "/admin/artifacts") {
  const fetchMock = createFetchMock(response);
  vi.stubGlobal("fetch", fetchMock);
  render(
    <TestQueryProvider>
      <AuthProvider>
        <WorkspaceContextProvider>
          <MemoryRouter initialEntries={[route]}>
            <Routes>
              <Route path="/admin/artifacts" element={<ArtifactsPage />} />
              <Route path="/admin/artifacts/new" element={<ArtifactCreatePage />} />
              <Route path="/admin/artifacts/:artifactId" element={<ArtifactDetailsPage />} />
            </Routes>
          </MemoryRouter>
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
    if (method === "GET" && url.endsWith(`/api/workspaces/${workspaceId}/artifact-uploads/capabilities`)) {
      return jsonResponse({ maxUploadBytes: 52428800, sampleArtifactGenerationEnabled: true });
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/artifact-uploads`)) {
      return jsonResponse({ uploadId: "upload-1", status: "Pending", expiresAt: "2026-06-04T13:00:00Z", maxUploadBytes: 52428800 }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/artifact-uploads/upload-1/complete`)) {
      list = { items: [artifactFixture] };
      return jsonResponse({ uploadId: "upload-1", status: "Completed", artifact: artifactFixture, created: true, diagnostics: [] }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/artifact-uploads/dev-sample`)) {
      list = { items: [artifactFixture] };
      return jsonResponse({ uploadId: "upload-sample", status: "Completed", artifact: artifactFixture, created: true, diagnostics: [] }, 201);
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
    if (method === "GET" && url.includes(`/api/workspaces/${workspaceId}/artifacts/`)) {
      const artifactRecordId = decodeURIComponent(url.split("/artifacts/")[1].split("/")[0]);
      const artifact = list.items.find((item) => item.id === artifactRecordId);
      return artifact ? jsonResponse(artifact) : jsonResponse({ title: "Not found" }, 404);
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

function installSuccessfulXhrUpload() {
  class MockXMLHttpRequest {
    status = 204;
    responseText = "";
    upload = { onprogress: undefined as ((event: ProgressEvent) => void) | undefined };
    onload: (() => void) | null = null;
    onerror: (() => void) | null = null;
    open = vi.fn();
    setRequestHeader = vi.fn();
    send = vi.fn((file: File) => {
      this.upload.onprogress?.({ lengthComputable: true, loaded: file.size, total: file.size } as ProgressEvent);
      this.onload?.();
    });
  }

  vi.stubGlobal("XMLHttpRequest", MockXMLHttpRequest);
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

const localArtifactFixture: WorkspaceArtifact = {
  ...artifactFixture,
  id: "artifact-local",
  payloadReference: {
    provider: "local",
    uri: "local:///tmp/claims-prod.zip",
    mediaType: "application/zip",
    sizeBytes: 1234,
    referenceDigest: null,
    expiresAt: null
  }
};
