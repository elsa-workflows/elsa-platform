import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router-dom";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AppShell } from "@/app/AppShell";

describe("AppShell", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders the four MVP navigation entries", async () => {
    renderAppShell();

    expect(screen.getAllByRole("link", { name: "Overview" }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("link", { name: "Sources" }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("link", { name: "Packages" }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("link", { name: "Sync Runs" }).length).toBeGreaterThan(0);
    expect(screen.queryByRole("link", { name: "Settings" })).not.toBeInTheDocument();
    expect(await screen.findAllByLabelText("Application build number")).toHaveLength(2);
  });

  it("shows the application build number", async () => {
    renderAppShell("2026.05.16.7");

    const buildLabels = await screen.findAllByLabelText("Application build number");
    expect(buildLabels).toHaveLength(2);
    buildLabels.forEach((label) => expect(label).toHaveTextContent("Build 2026.05.16.7"));
  });
});

function renderAppShell(buildNumber = "0.0.1") {
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

function TestQueryProvider({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false }
    }
  });

  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}
