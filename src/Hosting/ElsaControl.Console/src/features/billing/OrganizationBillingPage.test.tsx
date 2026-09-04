import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { OrganizationBillingPage } from "@/features/billing/OrganizationBillingPage";
import { WorkspaceContextProvider } from "@/app/WorkspaceContextProvider";
import { AuthProvider } from "@/lib/auth/AuthProvider";

describe("OrganizationBillingPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    window.localStorage?.clear?.();
  });

  it("renders lifecycle, capacity, and policy-derived capability information", async () => {
    const requests: string[] = [];
    installFetch((input) => {
      const url = input instanceof Request ? input.url : input.toString();
      requests.push(url);
      if (url.endsWith("/api/auth/session"))
        return Response.json({ loginEnabled: true, authenticated: true, displayName: "Test User", email: "test@example.com", loginPath: "/api/auth/login", logoutPath: "/api/auth/logout" });
      if (url.endsWith("/api/me/organizations"))
        return Response.json(workspaceContextFixture("Owner"));
      if (url.endsWith("/api/organizations/00000000-0000-0000-0000-000000000001/billing/"))
        return Response.json(billingFixture());
      return Response.json({ title: "Not found" }, { status: 404 });
    });

    renderPage();

    expect(await screen.findByRole("heading", { name: "Billing & entitlements" })).toBeInTheDocument();
    expect(screen.getByText("Billing is active")).toBeInTheDocument();
    expect(screen.getAllByText("Active").length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText("2 used of 2")).toBeInTheDocument();
    expect(screen.getByText("1 used of 3")).toBeInTheDocument();
    expect(screen.getByText("3 capabilities")).toBeInTheDocument();
    expect(requests).toContain("/api/organizations/00000000-0000-0000-0000-000000000001/billing/");
    expect(screen.queryByText("Stripe")).not.toBeInTheDocument();
    expect(screen.queryByText("price_server_default")).not.toBeInTheDocument();
  });

  it("disables billing actions for an organization member while preserving the safe status view", async () => {
    installFetch((input) => {
      const url = input instanceof Request ? input.url : input.toString();
      if (url.endsWith("/api/auth/session"))
        return Response.json({ loginEnabled: true, authenticated: true, displayName: "Member", email: "member@example.com", loginPath: "/api/auth/login", logoutPath: "/api/auth/logout" });
      if (url.endsWith("/api/me/organizations"))
        return Response.json(workspaceContextFixture("Member"));
      if (url.endsWith("/api/organizations/00000000-0000-0000-0000-000000000001/billing/"))
        return Response.json(billingFixture());
      return Response.json({ title: "Not found" }, { status: 404 });
    });

    renderPage();

    const action = await screen.findByRole("button", { name: /Open billing workspace/i });
    expect(action).toBeDisabled();
    expect(screen.getByText(/Billing actions are available to billing administrators/i)).toBeInTheDocument();
  });

  it("shows a safe unavailable state without exposing provider details", async () => {
    installFetch((input) => {
      const url = input instanceof Request ? input.url : input.toString();
      if (url.endsWith("/api/auth/session"))
        return Response.json({ loginEnabled: true, authenticated: true, displayName: "Test User", email: "test@example.com", loginPath: "/api/auth/login", logoutPath: "/api/auth/logout" });
      if (url.endsWith("/api/me/organizations"))
        return Response.json(workspaceContextFixture("Owner"));
      if (url.endsWith("/api/organizations/00000000-0000-0000-0000-000000000001/billing/"))
        return Response.json({ title: "Unavailable" }, { status: 503 });
      return Response.json({ title: "Not found" }, { status: 404 });
    });

    renderPage();

    expect(await screen.findByRole("alert")).toHaveTextContent("Billing service is unavailable");
    expect(screen.queryByText("Stripe")).not.toBeInTheDocument();
  });

  it("labels the past-due transition time without presenting it as a deadline", async () => {
    installFetch((input) => {
      const url = input instanceof Request ? input.url : input.toString();
      if (url.endsWith("/api/auth/session"))
        return Response.json({ loginEnabled: true, authenticated: true, displayName: "Test User", email: "test@example.com", loginPath: "/api/auth/login", logoutPath: "/api/auth/logout" });
      if (url.endsWith("/api/me/organizations"))
        return Response.json(workspaceContextFixture("Owner"));
      if (url.endsWith("/api/organizations/00000000-0000-0000-0000-000000000001/billing/"))
        return Response.json(billingFixture("PastDue"));
      return Response.json({ title: "Not found" }, { status: 404 });
    });

    renderPage();

    expect(await screen.findByText("Past due since")).toBeInTheDocument();
    expect(screen.queryByText("Next deadline")).not.toBeInTheDocument();
  });

  it("renders an error when the server returns an unsafe billing session URL", async () => {
    installFetch((input) => {
      const url = input instanceof Request ? input.url : input.toString();
      if (url.endsWith("/api/auth/session"))
        return Response.json({ loginEnabled: true, authenticated: true, displayName: "Test User", email: "test@example.com", loginPath: "/api/auth/login", logoutPath: "/api/auth/logout" });
      if (url.endsWith("/api/me/organizations"))
        return Response.json(workspaceContextFixture("Owner"));
      if (url.endsWith("/billing/portal"))
        return Response.json({ url: "javascript:alert(1)" });
      if (url.endsWith("/api/organizations/00000000-0000-0000-0000-000000000001/billing/"))
        return Response.json(billingFixture());
      return Response.json({ title: "Not found" }, { status: 404 });
    });

    renderPage();
    await screen.findByRole("heading", { name: "Billing & entitlements" });
    await userEvent.click(screen.getByRole("button", { name: /Open billing workspace/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("No trusted billing session was opened");
  });
});

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } }
  });

  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AuthProvider>
          <WorkspaceContextProvider>
            <OrganizationBillingPage />
          </WorkspaceContextProvider>
        </AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

function installFetch(handler: (input: RequestInfo | URL) => Response | Promise<Response>) {
  vi.stubGlobal("fetch", vi.fn(handler));
}

function workspaceContextFixture(role: string) {
  return {
    account: { id: "account-1", displayName: "Test User", email: "test@example.com" },
    organizations: [{ id: organizationId, name: "Acme Corp", role }],
    workspaces: [{ id: workspaceId, name: "Claims", kind: "Shared", role: "Owner", organizationId, organizationName: "Acme Corp", organizationRole: role }]
  };
}

function billingFixture(state = "Active") {
  return {
    organizationId,
    subscription: {
      state,
      trialStartedAt: "2026-08-01T00:00:00Z",
      trialEndsAt: "2026-08-15T00:00:00Z",
      activatedAt: "2026-08-10T00:00:00Z",
      pastDueAt: state === "PastDue" ? "2026-09-01T00:00:00Z" : null,
      constrainedAt: null,
      suspendedAt: null,
      retainedAt: null,
      deletedAt: null,
      updatedAt: "2026-09-01T00:00:00Z"
    },
    entitlements: {
      canCreateCustomSources: true,
      maxSources: 20,
      maxWorkspaces: 3,
      maxInstances: 2,
      maxPackagesIndexed: 500,
      maxVersionsPerPackage: 20,
      maxSyncsPerDay: 25,
      privateFeedsEnabled: true,
      managedHostingEnabled: true,
      deploymentTargetsEnabled: true,
      syncedAt: "2026-09-01T00:00:00Z"
    },
    capacity: {
      managedInstancesUsed: 2,
      managedInstancesLimit: 2,
      workspacesUsed: 1,
      workspacesLimit: 3
    },
    capabilities: ["managed-hosting", "deployment-targets", "custom-sources"]
  };
}

const organizationId = "00000000-0000-0000-0000-000000000001";
const workspaceId = "00000000-0000-0000-0000-000000000010";
