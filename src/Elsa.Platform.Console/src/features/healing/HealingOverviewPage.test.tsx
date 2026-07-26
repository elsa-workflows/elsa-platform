import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { WorkspaceContextProvider } from "@/app/WorkspaceContextProvider";
import { HealingAuditPage, HealingOverviewPage } from "@/features/healing/HealingOverviewPage";
import { HealingEmptyState, HealingErrorState, HealingLoadingState, HealingStaleState } from "@/features/healing/HealingStateViews";
import { AuthProvider } from "@/lib/auth/AuthProvider";

describe("Healing overview and audit", () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it("shows outcomes, bounded usage, audit decisions, and read-only permission state", async () => {
    renderPage("overview");

    expect(await screen.findByRole("heading", { name: "Healing overview" }, { timeout: 5_000 })).toBeInTheDocument();
    expect(screen.getByText("Read-only operational report")).toBeInTheDocument();
    expect(screen.getByText("Enabled applications")).toBeInTheDocument();
    expect(screen.getByText("1,200 / 5,000")).toBeInTheDocument();
    expect(screen.getByText("Merge blocked")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Unhandled Request" })).toHaveAttribute("href", `/admin/healing/incidents/${incidentId}`);
    expect(screen.queryByRole("button", { name: /configure/i })).not.toBeInTheDocument();
    expect(screen.queryByText("protected-production-payload")).not.toBeInTheDocument();
  });

  it("applies overview filters to workspace-scoped report requests", async () => {
    const { fetchMock } = renderPage("overview");
    await screen.findByRole("heading", { name: "Healing overview" });

    fireEvent.change(screen.getByLabelText("Application ID"), { target: { value: applicationId } });
    fireEvent.change(screen.getByLabelText("Environment ID"), { target: { value: environmentId } });
    await userEvent.selectOptions(screen.getByLabelText("Incident status"), "NeedsHuman");
    await userEvent.selectOptions(screen.getByLabelText("Severity"), "Fatal");
    await userEvent.selectOptions(screen.getByLabelText("Repairability"), "true");
    await userEvent.click(screen.getByRole("button", { name: "Apply filters" }));

    await waitFor(() => expect(fetchMock.mock.calls.some(([input]) => {
      const url = new URL(input instanceof Request ? input.url : input.toString(), "http://console.test");
      return url.pathname.endsWith(`/api/workspaces/${workspaceId}/healing/overview`) &&
        url.searchParams.get("applicationId") === applicationId &&
        url.searchParams.get("environmentId") === environmentId &&
        url.searchParams.get("status") === "NeedsHuman" &&
        url.searchParams.get("severity") === "Fatal" &&
        url.searchParams.get("repairable") === "true";
    })).toBe(true));
  });

  it("renders the accessible audit timeline and requests the next opaque page", async () => {
    const { fetchMock } = renderPage("audit");
    expect(await screen.findByRole("heading", { name: "Healing audit" }, { timeout: 5_000 })).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Healing audit decisions" })).toBeInTheDocument();
    expect(screen.getByText("Policy denied · Platform service healing-worker")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Load more decisions" }));
    await waitFor(() => expect(fetchMock.mock.calls.some(([input]) => {
      const url = new URL(input instanceof Request ? input.url : input.toString(), "http://console.test");
      return url.pathname.endsWith("/healing/audit") && url.searchParams.get("cursor") === "opaque-next-page";
    })).toBe(true));
  });

  it("provides explicit accessible loading, empty, error, and stale states", () => {
    const loading = render(<HealingLoadingState title="Loading Healing overview" />);
    expect(screen.getByRole("status")).toHaveAttribute("aria-busy", "true");
    loading.unmount();
    const empty = render(<HealingEmptyState title="No Healing activity" description="Discovery is not configured." />);
    expect(screen.getByRole("heading", { name: "No Healing activity" })).toBeInTheDocument();
    empty.unmount();
    const error = render(<HealingErrorState title="Healing overview could not load" />);
    expect(screen.getByRole("alert")).toBeInTheDocument();
    error.unmount();
    render(<HealingStaleState updatedAt="2026-07-16T12:00:00Z" />);
    expect(screen.getByRole("status")).toHaveTextContent("last authoritative Healing state");
  });
});

function renderPage(view: "overview" | "audit") {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(input instanceof Request ? input.url : input.toString(), "http://console.test");
    if (url.pathname.endsWith("/api/auth/session"))
      return json({ loginEnabled: true, authenticated: true, displayName: "Ada", email: "ada@example.test", loginPath: "/api/auth/login", logoutPath: "/api/auth/logout" });
    if (url.pathname.endsWith("/api/me/organizations")) return json(workspaceContextFixture());
    if (url.pathname.endsWith("/healing/overview")) return json(overviewFixture);
    if (url.pathname.endsWith("/healing/usage")) return json(usageFixture);
    if (url.pathname.endsWith("/healing/audit")) {
      if (url.searchParams.get("cursor")) return json({ items: [], nextCursor: null });
      return json({ items: [auditFixture], nextCursor: view === "audit" ? "opaque-next-page" : null });
    }
    return json({ title: "Not found" }, 404);
  });
  vi.stubGlobal("fetch", fetchMock);
  const route = view === "audit" ? "/admin/healing/audit" : "/admin/healing";
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } });
  return {
    ...render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[route]}>
          <AuthProvider>
            <WorkspaceContextProvider>
              <Routes>
                <Route path="/admin/healing" element={<HealingOverviewPage />} />
                <Route path="/admin/healing/audit" element={<HealingAuditPage />} />
              </Routes>
            </WorkspaceContextProvider>
          </AuthProvider>
        </MemoryRouter>
      </QueryClientProvider>
    ),
    fetchMock
  };
}

const workspaceId = "00000000-0000-0000-0000-000000000001";
const applicationId = "00000000-0000-0000-0000-000000000002";
const environmentId = "00000000-0000-0000-0000-000000000003";
const incidentId = "00000000-0000-0000-0000-000000000004";

const usageFixture = {
  from: null, to: null, attempts: 3, completedAttempts: 1, failedAttempts: 1,
  inputUnits: 800, outputUnits: 400, agentDurationSeconds: 75, repositoryRunDurationSeconds: 40,
  repositoryRuns: 2, providerOperations: 4, failedProviderOperations: 1,
  inferenceBudget: 5000, repositoryRunBudget: 5, timeBudgetSeconds: 600, concurrencyBudget: 2
};

const overviewFixture = {
  updatedAt: "2026-07-16T12:00:00Z",
  applications: { total: 2, enabled: 1, disabled: 0, stopped: 1 },
  environments: { total: 3, enabled: 2, disabled: 0, stopped: 1 },
  openIncidents: 2,
  incidentStates: [{ name: "NeedsHuman", count: 1 }, { name: "FailedVerification", count: 1 }],
  severities: [{ name: "Error", count: 1 }, { name: "Fatal", count: 1 }],
  repairability: { repairable: 1, observationOnly: 1 },
  repairActivity: { activeAttempts: 1, blockedAttempts: 1, openPullRequests: 1, blockedPullRequests: 1 },
  verificationOutcomes: [{ name: "FailedVerification", count: 1 }, { name: "Healed", count: 1 }],
  usage: usageFixture,
  recentIncidents: [{ id: incidentId, applicationId, status: "NeedsHuman", severity: "Error", classification: "UnhandledRequest", occurrenceCount: 4, repairable: true, lastSeenAt: "2026-07-16T11:00:00Z" }],
  permissions: ["healing.read"]
};

const auditFixture = {
  id: "00000000-0000-0000-0000-000000000005", sequence: 7, aggregateType: "incident", aggregateId: incidentId,
  eventType: "merge-blocked", reasonCode: "policy-denied", actorType: "platform-service", actorId: "healing-worker",
  correlationId: incidentId, causationId: null, policyVersion: "1", inputHash: null, outputHash: null,
  details: { gateReason: "revision-unverified" }, occurredAt: "2026-07-16T12:00:00Z"
};

function workspaceContextFixture() {
  return {
    organizations: [{ id: "org-1", name: "Acme" }],
    workspaces: [{ id: workspaceId, organizationId: "org-1", name: "Production", role: "Owner", status: "Active" }],
    memberships: []
  };
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}
