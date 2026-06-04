import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { WorkspaceContextProvider } from "@/app/WorkspaceContextProvider";
import { DeploymentTierCreatePage, DeploymentTierEditPage, DeploymentTiersPage } from "@/features/deployments/DeploymentTiersPage";
import type { DeploymentTierCapability, WorkspaceDeploymentTier } from "@/features/deployments/deploymentModels";
import { AuthProvider } from "@/lib/auth/AuthProvider";

describe("DeploymentTiersPanel", () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it("renders tiers as a list with dedicated create and edit routes", async () => {
    renderTierRoutes("/admin/deployments/tiers");

    expect(await screen.findByRole("heading", { name: "Workspace deployment tiers" })).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/tiers/new")).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/tiers/tier-production/edit")).toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: "Name" })).not.toBeInTheDocument();
  });

  it("posts a new tier from a dedicated create page", async () => {
    const fetchMock = renderTierRoutes("/admin/deployments/tiers/new");

    expect(await screen.findByRole("heading", { name: "New tier" })).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText("Name"), "UAT");
    await userEvent.type(screen.getByLabelText("Description"), "User acceptance");
    await userEvent.clear(screen.getByLabelText("Sort order"));
    await userEvent.type(screen.getByLabelText("Sort order"), "25");
    await userEvent.click(screen.getByRole("checkbox", { name: /Pre-production-like/i }));
    await userEvent.click(screen.getByRole("checkbox", { name: /Promotion target/i }));
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/tiers`),
        expect.objectContaining({ method: "POST" })
      )
    );
    const body = requestBody(fetchMock, "POST", "/deployments/tiers");
    expect(body).toMatchObject({
      name: "UAT",
      description: "User acceptance",
      sortOrder: 25,
      capabilities: ["deployment.promotion.target", "deployment.tier.preproduction-like"]
    });
  }, 15000);

  it("shows duplicate-name conflicts from the platform", async () => {
    renderTierRoutes("/admin/deployments/tiers/new", { duplicateCreate: true });

    expect(await screen.findByRole("heading", { name: "New tier" })).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText("Name"), "Production Gate");
    await userEvent.click(screen.getByRole("checkbox", { name: /^Production-like/i }));
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("An active deployment tier with the same name already exists.")).toBeInTheDocument();
  }, 15000);

  it("requires impact preview before saving edits on the dedicated edit page", async () => {
    const fetchMock = renderTierRoutes("/admin/deployments/tiers/tier-production/edit");

    expect(await screen.findByRole("heading", { name: "Edit Production Gate" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();

    await userEvent.click(screen.getByRole("checkbox", { name: /Confirmation required/i }));
    await userEvent.click(screen.getByRole("button", { name: "Preview impact" }));
    expect(await screen.findByText("2 environments affected")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/tiers/tier-production`),
        expect.objectContaining({ method: "PUT" })
      )
    );
    const body = requestBody(fetchMock, "PUT", "/deployments/tiers/tier-production");
    expect(body.impactAccepted).toBe(true);
    expect(body.capabilities).toContain("deployment.confirmation.required");
  }, 15000);

  it("archives and restores tiers through owner-only actions", async () => {
    const fetchMock = renderTierRoutes("/admin/deployments/tiers");

    expect(await screen.findByRole("heading", { name: "Workspace deployment tiers" })).toBeInTheDocument();
    await userEvent.click(screen.getAllByRole("button", { name: "Archive" })[0]);
    await userEvent.click(screen.getByRole("button", { name: "Restore" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/tiers/tier-production/archive`),
        expect.objectContaining({ method: "POST" })
      )
    );
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/tiers/tier-legacy/restore`),
      expect.objectContaining({ method: "POST" })
    );
  });

  it("disables management actions for non-owners", async () => {
    renderTierRoutes("/admin/deployments/tiers", { canManageTiers: false });

    expect(await screen.findByRole("heading", { name: "Workspace deployment tiers" })).toBeInTheDocument();
    expect(screen.getByText("Workspace owner access is required to manage tiers.")).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/tiers/new")).toHaveAttribute("aria-disabled", "true");
    expect(linkByHref("/admin/deployments/tiers/tier-production/edit")).toHaveAttribute("aria-disabled", "true");
    expect(screen.getAllByRole("button", { name: "Archive" })[0]).toBeDisabled();
  });
});

function renderTierRoutes(
  initialEntry: string,
  options: { canManageTiers?: boolean; duplicateCreate?: boolean } = {}
) {
  const fetchMock = createFetchMock(options);
  vi.stubGlobal("fetch", fetchMock);
  render(
    <TestQueryProvider>
      <MemoryRouter initialEntries={[initialEntry]}>
        <AuthProvider>
          <WorkspaceContextProvider>
            <Routes>
              <Route path="/admin/deployments/tiers" element={<DeploymentTiersPage />} />
              <Route path="/admin/deployments/tiers/new" element={<DeploymentTierCreatePage />} />
              <Route path="/admin/deployments/tiers/:tierId/edit" element={<DeploymentTierEditPage />} />
            </Routes>
          </WorkspaceContextProvider>
        </AuthProvider>
      </MemoryRouter>
    </TestQueryProvider>
  );
  return fetchMock;
}

function createFetchMock({ canManageTiers = true, duplicateCreate = false }: { canManageTiers?: boolean; duplicateCreate?: boolean } = {}) {
  return vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = input instanceof Request ? input.url : input.toString();
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    if (url.endsWith("/api/auth/session"))
      return jsonResponse({ loginEnabled: true, authenticated: true, displayName: "Test User", email: "test@example.com", loginPath: "/api/auth/login", logoutPath: "/api/auth/logout" });
    if (url.endsWith("/api/me/organizations"))
      return jsonResponse(workspaceContextFixture(canManageTiers));
    if (duplicateCreate && method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/tiers`))
      return jsonResponse({ title: "An active deployment tier with the same name already exists." }, 409);
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/tiers`))
      return jsonResponse({ ...tiers[0], id: "tier-uat", name: "UAT" }, 201);
    if (url.endsWith(`/api/workspaces/${workspaceId}/deployments/tier-capabilities`))
      return jsonResponse({ capabilities });
    if (url.endsWith(`/api/workspaces/${workspaceId}/deployments/tiers`))
      return jsonResponse({ tiers });
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/tiers/tier-production/impact-preview`))
      return jsonResponse({
        tierId: "tier-production",
        currentCapabilities: ["deployment.tier.production-like"],
        proposedCapabilities: ["deployment.confirmation.required", "deployment.tier.production-like"],
        addedCapabilities: ["deployment.confirmation.required"],
        removedCapabilities: [],
        affectedEnvironmentCount: 2,
        affectedEnvironmentSamples: [],
        changedSafeguards: ["Deployment confirmation will be required."]
      });
    if (method === "PUT" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/tiers/tier-production`))
      return jsonResponse({ ...tiers[0], capabilities: ["deployment.confirmation.required", "deployment.tier.production-like"] });
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/tiers/tier-production/archive`))
      return jsonResponse({ ...tiers[0], status: "Archived" });
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/tiers/tier-legacy/restore`))
      return jsonResponse({ ...tiers[1], status: "Active" });
    return jsonResponse({ title: "Not found" }, 404);
  });
}

function workspaceContextFixture(canManageTiers: boolean) {
  return {
    account: { id: "account-1", displayName: "Test User", email: "test@example.com" },
    organizations: [{ id: organizationId, name: "Acme Corp", role: "Owner" }],
    workspaces: [
      { id: workspaceId, name: "Acme Insurance", kind: "Shared", role: canManageTiers ? "Owner" : "Reader", organizationId, organizationName: "Acme Corp", organizationRole: "Owner" }
    ]
  };
}

function requestBody(fetchMock: ReturnType<typeof createFetchMock>, method: string, urlSuffix: string) {
  const call = fetchMock.mock.calls.find(([input, init]) => {
    const url = input instanceof Request ? input.url : input.toString();
    return url.includes(urlSuffix) && init?.method === method;
  });
  expect(call).toBeDefined();
  return JSON.parse(call![1]?.body?.toString() ?? "{}") as Record<string, unknown>;
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function linkByHref(href: string) {
  const link = screen.getAllByRole("link").find((item) => item.getAttribute("href") === href);
  expect(link).toBeDefined();
  return link!;
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

const workspaceId = "00000000-0000-0000-0000-000000000010";
const organizationId = "00000000-0000-0000-0000-000000000001";

const capabilities: DeploymentTierCapability[] = [
  {
    id: "deployment.tier.production-like",
    label: "Production-like",
    description: "Production runtime behavior.",
    category: "Classification",
    isDeprecated: false
  },
  {
    id: "deployment.tier.preproduction-like",
    label: "Pre-production-like",
    description: "Late-stage validation runtime behavior.",
    category: "Classification",
    isDeprecated: false
  },
  {
    id: "deployment.promotion.target",
    label: "Promotion target",
    description: "Can receive promoted artifacts.",
    category: "Promotion",
    isDeprecated: false
  },
  {
    id: "deployment.confirmation.required",
    label: "Confirmation required",
    description: "Requires explicit confirmation before deployment.",
    category: "Safeguards",
    isDeprecated: false
  }
];

const tiers: WorkspaceDeploymentTier[] = [
  {
    id: "tier-production",
    workspaceId,
    name: "Production Gate",
    description: "Customer-facing runtime",
    sortOrder: 40,
    isDefault: false,
    status: "Active",
    capabilities: ["deployment.tier.production-like"],
    environmentCount: 2,
    createdAt: "2026-05-28T10:00:00Z",
    updatedAt: "2026-05-28T10:00:00Z",
    createdByAccountId: null,
    updatedByAccountId: null,
    archivedAt: null,
    archivedByAccountId: null
  },
  {
    id: "tier-legacy",
    workspaceId,
    name: "Legacy",
    description: null,
    sortOrder: 50,
    isDefault: false,
    status: "Archived",
    capabilities: ["deployment.tier.preproduction-like"],
    environmentCount: 0,
    createdAt: "2026-05-28T10:00:00Z",
    updatedAt: "2026-05-28T10:00:00Z",
    createdByAccountId: null,
    updatedByAccountId: null,
    archivedAt: "2026-05-28T10:00:00Z",
    archivedByAccountId: null
  }
];
