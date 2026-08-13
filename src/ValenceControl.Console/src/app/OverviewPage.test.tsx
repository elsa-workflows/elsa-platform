import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { OverviewPage } from "@/app/OverviewPage";
import { WorkspaceContextProvider } from "@/app/WorkspaceContextProvider";
import { AuthProvider } from "@/lib/auth/AuthProvider";

describe("OverviewPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    if (typeof window.localStorage?.clear === "function") {
      window.localStorage.clear();
    }
  });

  it("shows the real artifact count and links deployment readiness to artifacts", async () => {
    renderOverview();

    const readinessLink = await screen.findByRole("link", { name: /Deployment readiness\s+2 artifacts/i });
    const applicationsLink = await screen.findByRole("link", { name: /Applications\s+2 applications/i });
    const packageLink = await screen.findByRole("link", { name: /Package approvals\s+2 pending/i });

    expect(readinessLink).toHaveAttribute("href", "/admin/artifacts");
    expect(screen.getByText("Registered artifacts available for revision creation and deployment promotion.")).toBeInTheDocument();
    expect(applicationsLink).toHaveAttribute("href", "/admin/deployments/applications");
    expect(screen.getByText("3 environments and 3 engines registered for deployment management.")).toBeInTheDocument();
    expect(packageLink).toHaveAttribute("href", "/admin/packages?approval=Pending");
    expect(screen.getByText("3 packages indexed; 2 packages awaiting approval.")).toBeInTheDocument();
  });
});

function renderOverview() {
  installLocalStorageStub();
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
    const url = input instanceof Request ? input.url : input.toString();
    if (url.endsWith("/api/auth/session"))
      return Response.json({ loginEnabled: true, authenticated: true, displayName: "Test User", email: "test@example.com", loginPath: "/api/auth/login", logoutPath: "/api/auth/logout" });
    if (url.endsWith("/api/me/organizations"))
      return Response.json(workspaceContextFixture());
    if (url.endsWith(`/api/workspaces/${workspaceId}/artifacts`))
      return Response.json({ items: [{ id: "artifact-1" }, { id: "artifact-2" }] });
    if (url.endsWith(`/api/workspaces/${workspaceId}/deployments/cockpit`))
      return Response.json(deploymentCockpitFixture());
    if (url.endsWith("/api/admin/packages"))
      return Response.json([
        packageItem("Elsa.Workflows", "Pending"),
        packageItem("Elsa.Http", "Approved"),
        packageItem("Elsa.Timers", "Pending")
      ]);
    return Response.json({ title: "Not found" }, { status: 404 });
  }));

  render(
    <TestQueryProvider>
      <MemoryRouter>
        <AuthProvider>
          <WorkspaceContextProvider>
            <OverviewPage />
          </WorkspaceContextProvider>
        </AuthProvider>
      </MemoryRouter>
    </TestQueryProvider>
  );
}

function packageItem(packageId: string, approvalStatus: "Pending" | "Approved" | "Rejected") {
  return {
    packageId,
    approved: approvalStatus === "Approved",
    listed: true,
    latestVersion: "1.0.0",
    approvalStatus,
    validationStatus: "Valid",
    versions: [
      {
        version: "1.0.0",
        approvalStatus,
        validationStatus: "Valid",
        isListed: true,
        suspiciousChangeDetected: false
      }
    ]
  };
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

function deploymentCockpitFixture() {
  return {
    applications: [
      {
        id: "claims",
        name: "Claims",
        description: null,
        environments: [
          { id: "claims-dev", name: "Dev", tier: "Development", tierId: null, desiredStateRevision: null },
          { id: "claims-prod", name: "Prod", tier: "Production", tierId: null, desiredStateRevision: null }
        ]
      },
      {
        id: "policies",
        name: "Policies",
        description: null,
        environments: [
          { id: "policies-dev", name: "Dev", tier: "Development", tierId: null, desiredStateRevision: null }
        ]
      }
    ],
    engines: [
      { id: "claims-dev-engine", environmentId: "claims-dev" },
      { id: "claims-prod-engine", environmentId: "claims-prod" },
      { id: "policies-dev-engine", environmentId: "policies-dev" }
    ],
    comparisons: [],
    observabilityBindings: [],
    history: [],
    driftReport: [],
    assistantPlans: []
  };
}

function installLocalStorageStub() {
  const storage = new Map<string, string>();

  Object.defineProperty(window, "localStorage", {
    configurable: true,
    value: {
      getItem: (key: string) => storage.get(key) ?? null,
      setItem: (key: string, value: string) => storage.set(key, value),
      removeItem: (key: string) => storage.delete(key),
      clear: () => storage.clear()
    }
  });
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
