import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter, RouterProvider } from "react-router-dom";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AppShell } from "@/app/AppShell";
import { AuthProvider } from "@/lib/auth/AuthProvider";

describe("AppShell", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    if (typeof window.localStorage?.clear === "function") {
      window.localStorage.clear();
    }
    document.documentElement.classList.remove("dark");
    document.documentElement.removeAttribute("data-theme-accent");
  });

  it("renders the unified platform navigation with package catalog active links", async () => {
    renderAppShell();

    const navigationText = screen.getAllByRole("navigation", { name: "Primary" })[0].textContent ?? "";
    expect(screen.getAllByRole("link", { name: "Overview" }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("link", { name: "Sources" }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("link", { name: "Packages" }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("link", { name: "Sync Runs" }).length).toBeGreaterThan(0);
    expect(screen.getAllByText("Deployments").length).toBeGreaterThan(0);
    expect(screen.getAllByRole("link", { name: "Applications" }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("link", { name: "Tiers" }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("link", { name: "Artifacts" }).length).toBeGreaterThan(0);
    expect(screen.getAllByText("Runtime Builder").length).toBeGreaterThan(0);
    expect(screen.getAllByRole("link", { name: "Build configurations" }).length).toBeGreaterThan(0);
    expect(screen.getAllByText("Managed Runtimes").length).toBeGreaterThan(0);
    expect(navigationText).toContain("PlatformOverview");
    expect(navigationText).toContain("DeploymentsOverviewApplicationsArtifactsTiers");
    expect(navigationText).toContain("Runtime BuilderBuild configurations");
    expect(screen.queryByRole("link", { name: "Settings" })).not.toBeInTheDocument();
    expect(await screen.findAllByRole("combobox", { name: "Organization" })).toHaveLength(2);
    expect(screen.getAllByRole("combobox", { name: "Workspace" })).toHaveLength(2);
    expect(await screen.findAllByLabelText("Application build number")).toHaveLength(2);
  });

  it("shows the application build number", async () => {
    renderAppShell("2026.05.16.7");

    const buildLabels = await screen.findAllByLabelText("Application build number");
    expect(buildLabels).toHaveLength(2);
    buildLabels.forEach((label) => expect(label).toHaveTextContent("Build 2026.05.16.7"));
  });

  it("toggles between light and dark mode", async () => {
    renderAppShell();

    expect(document.documentElement).not.toHaveClass("dark");

    await userEvent.click(screen.getAllByRole("button", { name: "Switch to dark mode" })[0]);

    expect(document.documentElement).toHaveClass("dark");
    expect(window.localStorage.getItem("elsa-console-theme")).toBe("dark");
    expect(screen.getAllByRole("button", { name: "Switch to light mode" }).length).toBeGreaterThan(0);
  });

  it("stores the selected accent theme", async () => {
    renderAppShell();

    const accentPickers = screen.getAllByRole("combobox", { name: "Theme accent" });
    expect(accentPickers).toHaveLength(2);
    expect(accentPickers[0]).toHaveValue("teal");
    await waitFor(() => expect(document.documentElement).toHaveAttribute("data-theme-accent", "teal"));

    await userEvent.selectOptions(accentPickers[0], "violet");

    expect(accentPickers[0]).toHaveValue("violet");
    expect(document.documentElement).toHaveAttribute("data-theme-accent", "violet");
    expect(window.localStorage.getItem("elsa-console-theme-accent")).toBe("violet");
  });

  it("opens Weaver as a global assistant drawer", async () => {
    renderAppShell();

    await userEvent.click(screen.getAllByRole("button", { name: "Open Weaver assistant" })[0]);

    expect(screen.getByRole("complementary", { name: "Weaver assistant" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Weaver" })).toBeInTheDocument();
    expect(screen.getByText("/admin")).toBeInTheDocument();
    expect(screen.getByLabelText("Mode")).toHaveValue("plan");
    expect(screen.getByLabelText("Guardrail")).toHaveValue("review");

    await userEvent.click(screen.getByRole("button", { name: "Suggest prompt" }));
    expect(screen.getByLabelText("Message Weaver")).toHaveValue("Summarize the current page and recommended next actions.");

    await userEvent.selectOptions(screen.getByLabelText("Mode"), "inspect");
    await userEvent.click(screen.getByRole("button", { name: "Queue" }));
    expect(screen.getByText("Queued work")).toBeInTheDocument();
    expect(screen.getByText(/Inspect · Review required/i)).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Run next" }));
    expect(screen.getByText("Queued: Summarize the current page and recommended next actions.")).toBeInTheDocument();
    expect(screen.getByText(/Mode: Inspect\. Guardrail: Review required/i)).toBeInTheDocument();
    expect(screen.queryByText("Queued work")).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Suggest prompt" }));
    await userEvent.click(screen.getByRole("button", { name: "Send" }));
    expect(screen.getByText("Summarize the current page and recommended next actions.")).toBeInTheDocument();
    expect(screen.getAllByText(/Assistant execution is not connected yet/i).length).toBeGreaterThan(0);

    await userEvent.click(screen.getByRole("button", { name: "Close Weaver assistant" }));
    expect(screen.queryByRole("complementary", { name: "Weaver assistant" })).not.toBeInTheDocument();
  });

  it("keeps workspace choices scoped to the selected organization", async () => {
    renderAppShell("0.0.1", multiOrganizationContextFixture());

    const organizationSelect = (await screen.findAllByRole("combobox", { name: "Organization" }))[0];
    const workspaceSelect = screen.getAllByRole("combobox", { name: "Workspace" })[0];

    expect(workspaceSelect).toHaveDisplayValue("Claims");

    await userEvent.selectOptions(organizationSelect, "org-beta");

    expect(workspaceSelect).toHaveDisplayValue("Research");
    expect(screen.queryByRole("option", { name: "Claims" })).not.toBeInTheDocument();
  });
});

function renderAppShell(buildNumber = "0.0.1", workspaceContext = workspaceContextFixture()) {
  installLocalStorageStub();
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
    const url = input instanceof Request ? input.url : input.toString();
    if (url.endsWith("/api/auth/session"))
      return Response.json({ loginEnabled: true, authenticated: true, displayName: "Test User", email: "test@example.com", loginPath: "/api/auth/login", logoutPath: "/api/auth/logout" });
    if (url.endsWith("/api/me/organizations"))
      return Response.json(workspaceContext);
    return Response.json({ name: "Elsa.Platform.Api", buildNumber });
  }));
  const router = createMemoryRouter([{ path: "/admin", element: <AppShell /> }], {
    initialEntries: ["/admin"]
  });

  render(
    <TestQueryProvider>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </TestQueryProvider>
  );
}

function workspaceContextFixture() {
  const organizationId = "00000000-0000-0000-0000-000000000001";
  return {
    account: { id: "account-1", displayName: "Test User", email: "test@example.com" },
    organizations: [{ id: organizationId, name: "Acme Corp", role: "Owner" }],
    workspaces: [
      { id: "00000000-0000-0000-0000-000000000010", name: "Acme Insurance", kind: "Shared", role: "Owner", organizationId, organizationName: "Acme Corp", organizationRole: "Owner" }
    ]
  };
}

function multiOrganizationContextFixture() {
  return {
    account: { id: "account-1", displayName: "Test User", email: "test@example.com" },
    organizations: [
      { id: "org-alpha", name: "Alpha Corp", role: "Owner" },
      { id: "org-beta", name: "Beta Labs", role: "Administrator" }
    ],
    workspaces: [
      { id: "workspace-claims", name: "Claims", kind: "Shared", role: "Owner", organizationId: "org-alpha", organizationName: "Alpha Corp", organizationRole: "Owner" },
      { id: "workspace-billing", name: "Billing", kind: "Shared", role: "Reader", organizationId: "org-alpha", organizationName: "Alpha Corp", organizationRole: "Owner" },
      { id: "workspace-research", name: "Research", kind: "Shared", role: "Owner", organizationId: "org-beta", organizationName: "Beta Labs", organizationRole: "Administrator" }
    ]
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
      queries: { retry: false }
    }
  });

  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}
