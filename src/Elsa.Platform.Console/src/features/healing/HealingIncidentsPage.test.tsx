import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { WorkspaceContextProvider } from "@/app/WorkspaceContextProvider";
import { HealingIncidentPage } from "@/features/healing/HealingIncidentPage";
import { HealingIncidentsPage } from "@/features/healing/HealingIncidentsPage";
import { AuthProvider } from "@/lib/auth/AuthProvider";

describe("Healing incidents", () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it("lists safe incident summaries and applies server-side filters", async () => {
    const { fetchMock } = renderPage("list");

    expect(await screen.findByRole("heading", { name: "Healing incidents" }, { timeout: 5_000 })).toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: "Unhandled Request" }).length).toBeGreaterThan(0);
    expect(screen.getAllByText("3 occurrences").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Observation only").length).toBeGreaterThan(0);

    await userEvent.selectOptions(screen.getByLabelText("Incident status"), "NeedsHuman");
    await userEvent.selectOptions(screen.getByLabelText("Severity"), "Fatal");
    await userEvent.selectOptions(screen.getByLabelText("Repairability"), "false");
    fireEvent.change(screen.getByLabelText("Application ID"), { target: { value: applicationId } });
    fireEvent.change(screen.getByLabelText("Environment ID"), { target: { value: environmentId } });
    await userEvent.click(screen.getByRole("button", { name: "Apply filters" }));

    await waitFor(() => expect(fetchMock.mock.calls.some(([input]) => {
      const url = new URL(input instanceof Request ? input.url : input.toString(), "http://console.test");
      return url.pathname.endsWith(`/api/workspaces/${workspaceId}/healing/incidents`) &&
        url.searchParams.get("applicationId") === applicationId &&
        url.searchParams.get("environmentId") === environmentId &&
        url.searchParams.get("status") === "NeedsHuman" &&
        url.searchParams.get("severity") === "Fatal" &&
        url.searchParams.get("repairable") === "false";
    })).toBe(true));
  }, 15_000);

  it("renders explicit empty and error states", async () => {
    const empty = renderPage("list", { empty: true });
    expect(await screen.findByRole("heading", { name: "No healing incidents" })).toBeInTheDocument();
    empty.unmount();

    renderPage("list", { fail: true });
    expect(await screen.findByRole("heading", { name: "Healing incidents could not load" })).toBeInTheDocument();
  });

  it("shows bounded incident details without raw stack or evidence digests", async () => {
    renderPage("detail");

    expect(await screen.findByRole("heading", { name: "InvalidOperationException" })).toBeInTheDocument();
    expect(screen.getByText("Human merge only until the producing revision and reproduction status are verified.")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Overview" })).toHaveAttribute("aria-selected", "true");

    await userEvent.click(screen.getByRole("tab", { name: "Occurrences" }));
    expect(screen.getByRole("columnheader", { name: "Exception" })).toBeInTheDocument();
    expect(screen.getByText("Default Redacted")).toBeInTheDocument();
    expect(screen.queryByText("at Acme.SecretController.Handle")).not.toBeInTheDocument();
    expect(screen.queryByText("sha256:protected-evidence")).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("tab", { name: "Environments" }));
    expect(screen.getByText("Deployed—unverified")).toBeInTheDocument();
    expect(screen.getByText("2 of 3 occurrences")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("tab", { name: "Repair" }));
    const repair = screen.getByRole("tabpanel", { name: "Repair" });
    expect(within(repair).getByText("Provider work item #42")).toBeInTheDocument();
    expect(within(repair).getByText("Current")).toBeInTheDocument();
  });

  it("uses a not-found-safe state when incident detail cannot be resolved", async () => {
    renderPage("detail", { fail: true });
    expect(await screen.findByRole("heading", { name: "Healing incident not found" })).toBeInTheDocument();
  });
});

function renderPage(view: "list" | "detail", options: { empty?: boolean; fail?: boolean } = {}) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = input instanceof Request ? input.url : input.toString();
    if (url.endsWith("/api/auth/session"))
      return json({ loginEnabled: true, authenticated: true, displayName: "Ada", email: "ada@example.test", loginPath: "/api/auth/login", logoutPath: "/api/auth/logout" });
    if (url.endsWith("/api/me/organizations")) return json(workspaceContextFixture());
    if (url.includes("/healing/incidents")) {
      if (options.fail) return json({ title: "Unavailable" }, view === "detail" ? 404 : 503);
      if (view === "detail") return json(detailFixture);
      return json({ items: options.empty ? [] : [summaryFixture], nextCursor: null });
    }
    return json({ title: "Not found" }, 404);
  });
  vi.stubGlobal("fetch", fetchMock);

  const route = view === "detail" ? `/admin/healing/incidents/${incidentId}` : "/admin/healing/incidents";
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } });
  return {
    ...render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[route]}>
          <AuthProvider>
            <WorkspaceContextProvider>
              <Routes>
                <Route path="/admin/healing/incidents" element={<HealingIncidentsPage />} />
                <Route path="/admin/healing/incidents/:incidentId" element={<HealingIncidentPage />} />
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
const episodeId = "00000000-0000-0000-0000-000000000005";

const impactFixture = {
  episodeId,
  environmentId,
  firstSeenAt: "2026-07-16T09:00:00Z",
  lastSeenAt: "2026-07-16T10:00:00Z",
  occurrenceCount: 2,
  producingRevisions: ["revision-a"],
  currentDeployedRevision: "revision-b",
  verificationStatus: "DeployedUnverified",
  occurrenceThreshold: 3,
  debounceWindow: "00:05:00",
  thresholdReachedAt: null,
  readyAfter: "2026-07-16T10:05:00Z"
};

const summaryFixture = {
  id: incidentId,
  applicationId,
  status: "ObservationOnly",
  severity: "Error",
  classification: "UnhandledRequest",
  firstSeenAt: "2026-07-16T09:00:00Z",
  lastSeenAt: "2026-07-16T10:00:00Z",
  occurrenceCount: 3,
  activeEpisodeId: episodeId,
  repairable: false,
  needsHumanReason: "RevisionUnverified",
  readyAfter: null,
  environmentImpacts: [impactFixture]
};

const detailFixture = {
  ...summaryFixture,
  status: "PullRequestOpen",
  repairable: true,
  episodes: [{
    id: episodeId,
    previousEpisodeId: null,
    openedAt: "2026-07-16T09:00:00Z",
    closedAt: null,
    producingRevisions: ["revision-a"],
    targetRevision: "revision-b",
    outcome: "Active",
    regressionReason: null
  }],
  environmentImpacts: [impactFixture],
  occurrences: [{
    id: "00000000-0000-0000-0000-000000000006",
    environmentId,
    revisionId: null,
    occurredAt: "2026-07-16T10:00:00Z",
    acceptedAt: "2026-07-16T10:00:01Z",
    classification: "UnhandledRequest",
    severity: "Error",
    exceptionType: "InvalidOperationException",
    operationName: "GET /claims",
    retryState: "None",
    evidenceTier: "DefaultRedacted",
    normalizedStack: "at Acme.SecretController.Handle",
    evidenceDigest: "sha256:protected-evidence"
  }],
  attributions: [{
    id: "00000000-0000-0000-0000-000000000007",
    occurrenceId: "00000000-0000-0000-0000-000000000006",
    componentEntryId: "00000000-0000-0000-0000-000000000008",
    bindingId: "00000000-0000-0000-0000-000000000009",
    confidence: 0.97,
    basis: "StackFrame, Assembly",
    resolution: "Selected",
    reasonCodes: ["trusted-manifest"]
  }],
  workItem: {
    id: "00000000-0000-0000-0000-000000000010",
    episodeId,
    number: 42,
    url: "https://github.com/acme/claims/issues/42",
    providerState: "open",
    projectionStatus: "Current",
    lastProjectedAt: "2026-07-16T10:01:00Z",
    lastObservedAt: "2026-07-16T10:02:00Z"
  }
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
