import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DeploymentTiersPanel } from "@/features/deployments/DeploymentTiersPanel";
import type { DeploymentTierCapability, WorkspaceDeploymentTier } from "@/features/deployments/deploymentModels";

describe("DeploymentTiersPanel", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("posts a new tier with selected capabilities", async () => {
    const fetchMock = renderPanel();

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
  });

  it("shows duplicate-name conflicts from the platform", async () => {
    renderPanel({ duplicateCreate: true });

    await userEvent.type(screen.getByLabelText("Name"), "Production Gate");
    await userEvent.click(screen.getByRole("checkbox", { name: /^Production-like/i }));
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("An active deployment tier with the same name already exists.")).toBeInTheDocument();
  });

  it("requires impact preview before saving edits", async () => {
    const fetchMock = renderPanel();

    await userEvent.click(screen.getAllByRole("button", { name: "Edit" })[0]);
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
  });

  it("archives and restores tiers through owner-only actions", async () => {
    const fetchMock = renderPanel();

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

  it("disables management actions for non-owners", () => {
    renderPanel({ canManageTiers: false });

    expect(screen.getByText("Workspace owner access is required to manage tiers.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
    expect(screen.getAllByRole("button", { name: "Edit" })[0]).toBeDisabled();
    expect(screen.getAllByRole("button", { name: "Archive" })[0]).toBeDisabled();
  });
});

function renderPanel({ canManageTiers = true, duplicateCreate = false }: { canManageTiers?: boolean; duplicateCreate?: boolean } = {}) {
  const fetchMock = createFetchMock({ duplicateCreate });
  vi.stubGlobal("fetch", fetchMock);
  render(
    <TestQueryProvider>
      <DeploymentTiersPanel workspaceId={workspaceId} canManageTiers={canManageTiers} tiers={tiers} capabilities={capabilities} />
    </TestQueryProvider>
  );
  return fetchMock;
}

function createFetchMock({ duplicateCreate = false }: { duplicateCreate?: boolean } = {}) {
  return vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = input instanceof Request ? input.url : input.toString();
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    if (duplicateCreate && method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/tiers`))
      return jsonResponse({ title: "An active deployment tier with the same name already exists." }, 409);
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/tiers`))
      return jsonResponse({ ...tiers[0], id: "tier-uat", name: "UAT" }, 201);
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
