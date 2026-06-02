import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
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
  });

  it("renders the unified platform navigation with package catalog active links", async () => {
    renderAppShell();

    expect(screen.getAllByRole("link", { name: "Overview" }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("link", { name: "Sources" }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("link", { name: "Packages" }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("link", { name: "Sync Runs" }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("link", { name: "Deployments" }).length).toBeGreaterThan(0);
    expect(screen.getAllByText("Artifacts").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Runtime Builder").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Managed Runtimes").length).toBeGreaterThan(0);
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
