import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter, RouterProvider } from "react-router-dom";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AppShell } from "@/app/AppShell";

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
    expect(screen.getAllByText("Deployments").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Artifacts").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Runtime Builder").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Managed Runtimes").length).toBeGreaterThan(0);
    expect(screen.queryByRole("link", { name: "Settings" })).not.toBeInTheDocument();
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
});

function renderAppShell(buildNumber = "0.0.1") {
  installLocalStorageStub();
  vi.stubGlobal("fetch", vi.fn(async () => Response.json({ name: "Elsa.Platform.PackageCatalog.Api", buildNumber })));
  const router = createMemoryRouter([{ path: "/admin", element: <AppShell /> }], {
    initialEntries: ["/admin"]
  });

  render(
    <TestQueryProvider>
      <RouterProvider router={router} />
    </TestQueryProvider>
  );
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
