import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState, type ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ManagedElsaOperationsPage } from "@/features/managed-elsa/ManagedElsaOperationsPage";
import { operationalHealthGuidance } from "@/features/managed-elsa/managedElsaModels";
import { ApiError } from "@/lib/api/httpClient";

const workspaceId = "00000000-0000-0000-0000-000000000010";
const secondWorkspaceId = "00000000-0000-0000-0000-000000000011";
const instanceId = "00000000-0000-0000-0000-000000000101";
const secondInstanceId = "00000000-0000-0000-0000-000000000102";

const workspace = { id: workspaceId, name: "Claims", kind: "Shared", role: "Owner", organizationId: "org-1", organizationName: "Acme", organizationRole: "Owner" };
const secondWorkspace = { ...workspace, id: secondWorkspaceId, name: "Billing" };

const mocks = vi.hoisted(() => ({
  listManagedElsaInstances: vi.fn(),
  getManagedElsaInstanceHealth: vi.fn(),
  getManagedElsaInstanceAudit: vi.fn(),
  activeWorkspaceId: "00000000-0000-0000-0000-000000000010"
}));

vi.mock("@/features/managed-elsa/managedElsaApi", async () => {
  const actual = await vi.importActual<typeof import("@/features/managed-elsa/managedElsaApi")>("@/features/managed-elsa/managedElsaApi");
  return {
    ...actual,
    listManagedElsaInstances: mocks.listManagedElsaInstances,
    getManagedElsaInstanceHealth: mocks.getManagedElsaInstanceHealth,
    getManagedElsaInstanceAudit: mocks.getManagedElsaInstanceAudit
  };
});

vi.mock("@/app/WorkspaceContextProvider", () => ({
  useWorkspaceContext: () => ({
    selectedWorkspaceId: mocks.activeWorkspaceId,
    selectedWorkspace: mocks.activeWorkspaceId === workspaceId ? workspace : secondWorkspace,
    isLoading: false,
    isError: false,
    error: null,
    workspaces: [workspace, secondWorkspace]
  })
}));

describe("ManagedElsaOperationsPage", () => {
  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
    vi.unstubAllGlobals();
    mocks.activeWorkspaceId = workspaceId;
  });

  it.each([
    ["Healthy", "managed.lifecycle.healthy"],
    ["Degraded", "managed.lifecycle.degraded"],
    ["Failed", "managed.lifecycle.failed"],
    ["Unknown", "managed.lifecycle.unknown"],
    ["Stale", "managed.lifecycle.stale"],
    ["RecoveryRequired", "managed.lifecycle.recovery-required"]
  ] as const)("renders fixed guidance for %s without conflating list health", async (status, code) => {
    mocks.listManagedElsaInstances.mockResolvedValue({ items: [instanceFixture()], page: 1, pageSize: 100, totalCount: 1, hasMore: false });
    mocks.getManagedElsaInstanceHealth.mockResolvedValue(healthFixture(status, code));
    mocks.getManagedElsaInstanceAudit.mockResolvedValue({ items: [] });

    renderPage();

    expect(await screen.findByRole("heading", { name: "Runtime Operations" })).toBeInTheDocument();
    expect(await screen.findByRole("status", { name: `Operational status: ${status}` })).toBeInTheDocument();
    expect(screen.getByText(operationalHealthGuidance[status])).toBeInTheDocument();
    expect(screen.getByText("List health")).toBeInTheDocument();
    expect(screen.getAllByText("Healthy", { selector: "span" })).toHaveLength(status === "Healthy" ? 2 : 1);
    expect(screen.getByText(code)).toBeInTheDocument();
  });

  it("renders operation, run, alerts and only the safe audit projection", async () => {
    mocks.listManagedElsaInstances.mockResolvedValue({ items: [instanceFixture()], page: 1, pageSize: 100, totalCount: 1, hasMore: false });
    mocks.getManagedElsaInstanceHealth.mockResolvedValue({
      ...healthFixture("Degraded", "provider.apply.failed"),
      operation: {
        id: "00000000-0000-0000-0000-000000000201",
        state: "Running",
        attemptNumber: 2,
        acceptedAt: "2026-09-02T10:00:00Z",
        startedAt: "2026-09-02T10:00:01Z",
        heartbeatAt: "2026-09-02T10:00:02Z",
        lastProgressAt: "2026-09-02T10:00:03Z",
        diagnosticCode: "provider.apply.failed"
      },
      run: {
        id: "00000000-0000-0000-0000-000000000301",
        status: "Running",
        attemptNumber: 3,
        queuedAt: "2026-09-02T09:59:00Z",
        startedAt: "2026-09-02T09:59:01Z",
        heartbeatAt: "2026-09-02T09:59:02Z",
        lastProgressAt: "2026-09-02T09:59:03Z",
        diagnosticCode: "deployment.run.failed"
      },
      alerts: [{ code: "managed.lifecycle.unhealthy-endpoint", severity: "Critical", dedupeIdentity: "must-not-render" }]
    });
    mocks.getManagedElsaInstanceAudit.mockResolvedValue({ items: [{
      id: "00000000-0000-0000-0000-000000000401",
      sequence: 3,
      eventType: "instance.reconciled",
      actorAccountId: "must-not-render",
      operatorSubject: "must-not-render",
      operationId: "00000000-0000-0000-0000-000000000201",
      deploymentRunId: "00000000-0000-0000-0000-000000000301",
      priorState: "Degraded",
      newState: "Failed",
      desiredStateRevisionId: "must-not-render",
      planReference: "must-not-render",
      diagnosticCode: "managed.lifecycle.unhealthy-endpoint",
      summary: "must-not-render",
      requestKeyHash: "must-not-render",
      occurredAt: "2026-09-02T10:01:00Z"
    }] });

    renderPage();

    expect((await screen.findAllByText("Running", { selector: "dd" }))).toHaveLength(2);
    expect(screen.getByText("Attempt 2")).toBeInTheDocument();
    expect(screen.getByText("Attempt 3")).toBeInTheDocument();
    expect(screen.getAllByText("managed.lifecycle.unhealthy-endpoint")).toHaveLength(2);
    expect(screen.getByText("Critical")).toBeInTheDocument();
    expect(screen.getByText("instance.reconciled")).toBeInTheDocument();
    expect(screen.getByText("Degraded → Failed")).toBeInTheDocument();
    expect(screen.queryByText("must-not-render")).not.toBeInTheDocument();
    expect(screen.getAllByText("provider.apply.failed")).toHaveLength(2);
  });

  it("keeps unknown future safe codes visible without inventing guidance", async () => {
    mocks.listManagedElsaInstances.mockResolvedValue({ items: [instanceFixture()], page: 1, pageSize: 100, totalCount: 1, hasMore: false });
    mocks.getManagedElsaInstanceHealth.mockResolvedValue({
      ...healthFixture("Degraded", "managed.lifecycle.future-state"),
      alerts: [{ code: "managed.lifecycle.future-alert", severity: "Warning", dedupeIdentity: "must-not-render" }]
    });
    mocks.getManagedElsaInstanceAudit.mockResolvedValue({ items: [] });

    renderPage();

    expect(await screen.findByText("managed.lifecycle.future-state")).toBeInTheDocument();
    expect(screen.getByText("managed.lifecycle.future-alert")).toBeInTheDocument();
    expect(screen.getByText("No fixed operator guidance is available for this code.")).toBeInTheDocument();
    expect(screen.queryByText("must-not-render")).not.toBeInTheDocument();
  });

  it("changes instance selection without retaining the prior detail", async () => {
    mocks.listManagedElsaInstances.mockResolvedValue({ items: [instanceFixture(), instanceFixture({ instanceId: secondInstanceId, name: "Billing runtime" })], page: 1, pageSize: 100, totalCount: 2, hasMore: false });
    mocks.getManagedElsaInstanceHealth
      .mockResolvedValueOnce(healthFixture("Healthy", "managed.lifecycle.healthy"))
      .mockResolvedValueOnce(healthFixture("Failed", "managed.lifecycle.failed"));
    mocks.getManagedElsaInstanceAudit.mockResolvedValue({ items: [] });

    renderPage();
    expect(await screen.findByText("managed.lifecycle.healthy")).toBeInTheDocument();

    await userEvent.selectOptions(screen.getByRole("combobox", { name: "Managed instance" }), secondInstanceId);

    expect(screen.queryByText("managed.lifecycle.healthy")).not.toBeInTheDocument();
    expect(await screen.findByText("managed.lifecycle.failed")).toBeInTheDocument();
    expect(mocks.getManagedElsaInstanceHealth).toHaveBeenLastCalledWith(workspaceId, secondInstanceId);
  });

  it("resets selected data when the workspace changes", async () => {
    mocks.listManagedElsaInstances.mockResolvedValue({ items: [instanceFixture()], page: 1, pageSize: 100, totalCount: 1, hasMore: false });
    mocks.getManagedElsaInstanceHealth.mockResolvedValue(healthFixture("Healthy", "managed.lifecycle.healthy"));
    mocks.getManagedElsaInstanceAudit.mockResolvedValue({ items: [] });

    const { rerender } = renderPage();
    expect(await screen.findByText("managed.lifecycle.healthy")).toBeInTheDocument();

    mocks.activeWorkspaceId = secondWorkspaceId;
    mocks.listManagedElsaInstances.mockResolvedValue({ items: [], page: 1, pageSize: 100, totalCount: 0, hasMore: false });
    rerender(<TestQueryProvider><ManagedElsaOperationsPage /></TestQueryProvider>);

    expect(screen.queryByText("managed.lifecycle.healthy")).not.toBeInTheDocument();
    expect(await screen.findByText("No managed Elsa instances")).toBeInTheDocument();
    expect(mocks.getManagedElsaInstanceHealth).toHaveBeenCalledTimes(1);
  });

  it("keeps list and detail failures recoverable, including not-found details", async () => {
    mocks.listManagedElsaInstances.mockRejectedValueOnce(new Error("list unavailable"));
    renderPage();

    expect(await screen.findByRole("heading", { name: "Managed instances could not load" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument();

    cleanup();
    mocks.listManagedElsaInstances.mockResolvedValue({ items: [instanceFixture()], page: 1, pageSize: 100, totalCount: 1, hasMore: false });
    const notFound = new ApiError("NotFound", "not found", 404);
    mocks.getManagedElsaInstanceHealth.mockRejectedValue(notFound);
    mocks.getManagedElsaInstanceAudit.mockRejectedValue(notFound);
    renderPage();

    expect(await screen.findByRole("heading", { name: "Runtime health not found" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Refresh" })).toBeInTheDocument();
  });
});

describe("managed operational clients", () => {
  it("use path-encoded workspace and instance IDs", async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL) => Response.json({ items: [] }));
    vi.stubGlobal("fetch", fetchMock);

    const actual = await vi.importActual<typeof import("@/features/managed-elsa/managedElsaApi")>("@/features/managed-elsa/managedElsaApi");
    await actual.getManagedElsaInstanceHealth("workspace/with spaces", "instance/with spaces");
    await actual.getManagedElsaInstanceAudit("workspace/with spaces", "instance/with spaces");

    expect(fetchMock.mock.calls.map(([request]) => String(request))).toEqual([
      "/api/workspaces/workspace%2Fwith%20spaces/instances/instance%2Fwith%20spaces/health",
      "/api/workspaces/workspace%2Fwith%20spaces/instances/instance%2Fwith%20spaces/audit"
    ]);
  });
});

function renderPage() {
  return render(<TestQueryProvider><ManagedElsaOperationsPage /></TestQueryProvider>);
}

function TestQueryProvider({ children }: { children: ReactNode }) {
  const [queryClient] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: false } } }));
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

function instanceFixture(overrides: Partial<{ instanceId: string; name: string }> = {}) {
  return {
    organizationId: "org-1",
    instanceId: overrides.instanceId ?? instanceId,
    name: overrides.name ?? "Claims runtime",
    slug: "claims-runtime",
    desiredLifecycle: "Running",
    observedLifecycle: "Ready",
    health: "Healthy",
    canOpen: false,
    audience: null,
    redirectUri: null,
    unavailableReason: null
  };
}

function healthFixture(status: string, diagnosticCode: string) {
  return {
    status,
    diagnosticCode,
    evaluatedAt: "2026-09-02T10:02:00Z",
    reconciledAt: "2026-09-02T10:01:00Z",
    operation: null,
    run: null,
    alerts: []
  };
}
