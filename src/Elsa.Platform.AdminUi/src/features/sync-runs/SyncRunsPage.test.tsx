import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SyncRunDetailsPage } from "@/features/sync-runs/SyncRunDetailsPage";
import { SyncRunsPage } from "@/features/sync-runs/SyncRunsPage";
import { syncRunFixture } from "@/test/fixtures";

function renderWithQueryClient(ui: ReactNode, response: unknown, status = 200, routePath?: string) {
  vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify(response), { status, headers: { "Content-Type": "application/json" } })));
  renderWithClient(ui, routePath);
}

function renderWithFetch(ui: ReactNode, fetch: ReturnType<typeof vi.fn>, routePath?: string) {
  vi.stubGlobal("fetch", fetch);
  renderWithClient(ui, routePath);
}

function renderWithClient(ui: ReactNode, routePath?: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/admin/sync-runs/${syncRunFixture.id}`]}>
        {routePath ? (
          <Routes>
            <Route path={routePath} element={ui} />
          </Routes>
        ) : (
          ui
        )}
      </MemoryRouter>
    </QueryClientProvider>
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("SyncRunsPage", () => {
  it("shows loading then empty state", async () => {
    renderWithQueryClient(<SyncRunsPage />, []);

    expect(screen.getByText("Loading sync runs")).toBeInTheDocument();
    expect(await screen.findByText("No sync runs")).toBeInTheDocument();
  });

  it("shows populated sync run rows with counters", async () => {
    renderWithQueryClient(<SyncRunsPage />, [syncRunFixture]);

    expect((await screen.findAllByText("Completed with errors")).length).toBeGreaterThan(0);
    expect(screen.getByRole("link", { name: "Elsa Official" })).toHaveAttribute("href", "/admin/sources/source-1");
    expect(screen.getByText("52")).toBeInTheDocument();
    expect(screen.getByText("4")).toBeInTheDocument();
    expect(screen.getAllByText("1").length).toBeGreaterThan(0);
  });

  it("can cancel a running sync run", async () => {
    const runningRun = { ...syncRunFixture, status: "Running", completedAt: null, error: null };
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = input.toString();
      if (path.endsWith(`/api/admin/sync-runs/${runningRun.id}/cancel`) && init?.method === "POST") {
        return new Response(JSON.stringify({ ...runningRun, status: "Canceled", error: "Sync canceled by operator." }), {
          status: 200,
          headers: { "Content-Type": "application/json" }
        });
      }

      return new Response(JSON.stringify([runningRun]), { status: 200, headers: { "Content-Type": "application/json" } });
    });
    vi.stubGlobal("fetch", fetchMock);
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });

    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <SyncRunsPage />
        </MemoryRouter>
      </QueryClientProvider>
    );

    await userEvent.click(await screen.findByRole("button", { name: "Cancel Sync" }));

    expect(fetchMock).toHaveBeenCalledWith(`/api/admin/sync-runs/${runningRun.id}/cancel`, expect.objectContaining({ method: "POST" }));
  });

  it("filters populated sync run rows", async () => {
    renderWithQueryClient(<SyncRunsPage />, [syncRunFixture]);

    await screen.findAllByText("Completed with errors");
    await userEvent.type(screen.getByPlaceholderText("Filter sync runs"), "missing-source");

    expect(screen.getByText("No matching sync runs")).toBeInTheDocument();
  });

  it("filters populated sync run rows by status", async () => {
    renderWithQueryClient(<SyncRunsPage />, [syncRunFixture]);

    await screen.findAllByText("Completed with errors");
    await userEvent.selectOptions(screen.getByLabelText("Filter by status"), "Running");

    expect(screen.getByText("No matching sync runs")).toBeInTheDocument();
  });

  it("shows error state when no sync run data is available", async () => {
    renderWithQueryClient(<SyncRunsPage />, { title: "Unavailable" }, 503);

    expect(await screen.findByText("Sync runs could not load")).toBeInTheDocument();
  });

  it("deletes a terminal sync run after confirmation", async () => {
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "DELETE" && url.endsWith(`/api/admin/sync-runs/${syncRunFixture.id}`)) {
        return jsonResponse({ deletedRunCount: 1, deletedItemCount: 1, excludedRunCount: 0, notFoundRunCount: 0, deletedRunIds: [syncRunFixture.id] });
      }
      return jsonResponse([syncRunFixture]);
    });

    renderWithFetch(<SyncRunsPage />, fetch);
    await screen.findAllByText("Completed with errors");
    await userEvent.click(screen.getByRole("button", { name: /^Delete$/ }));

    expect(confirm).toHaveBeenCalled();
    expect(fetch).toHaveBeenCalledWith(`/api/admin/sync-runs/${syncRunFixture.id}`, expect.objectContaining({ method: "DELETE", headers: expect.any(Headers) }));
  });

  it("does not show row delete action for running sync runs", async () => {
    renderWithQueryClient(<SyncRunsPage />, [{ ...syncRunFixture, status: "Running" }]);

    expect((await screen.findAllByText("Running")).length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: /^Cancel$/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Delete$/ })).not.toBeInTheDocument();
  });

  it("previews and runs bulk cleanup", async () => {
    const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url.includes("/api/admin/sync-runs/deletion-preview")) {
        return jsonResponse({
          completedBefore: "2026-05-01T00:00:00Z",
          eligibleRunCount: 2,
          eligibleItemCount: 3,
          excludedRunCount: 1
        });
      }
      if (init?.method === "DELETE" && url.includes("/api/admin/sync-runs?completedBefore=")) {
        return jsonResponse({ deletedRunCount: 2, deletedItemCount: 3, excludedRunCount: 1, notFoundRunCount: 0, deletedRunIds: [] });
      }
      return jsonResponse([syncRunFixture]);
    });

    renderWithFetch(<SyncRunsPage />, fetch);
    await screen.findAllByText("Completed with errors");
    await userEvent.type(screen.getByLabelText("Cleanup cutoff"), "2026-05-01T00:00");
    await userEvent.click(screen.getByRole("button", { name: "Preview" }));

    expect(await screen.findByText("Eligible runs: 2")).toBeInTheDocument();
    expect(screen.getByText("Item records: 3")).toBeInTheDocument();
    expect(screen.getByText("Excluded active runs: 1")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /Delete Eligible/ }));
    expect(fetch).toHaveBeenCalledWith(expect.stringContaining("/api/admin/sync-runs?completedBefore="), expect.objectContaining({ method: "DELETE", headers: expect.any(Headers) }));
  });
});

describe("SyncRunDetailsPage", () => {
  it("shows run diagnostics and failed items", async () => {
    renderWithQueryClient(<SyncRunDetailsPage />, syncRunFixture, 200, "/admin/sync-runs/:runId");

    expect(await screen.findByText("Sync Run sync-123")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Elsa Official" })).toHaveAttribute("href", "/admin/sources/source-1");
    expect(screen.getAllByText("Package download failed.").length).toBeGreaterThan(0);
    expect(screen.getByRole("link", { name: "Elsa.Persistence.PostgreSql" })).toBeInTheDocument();
  });

  it("shows when diagnostic panels are abbreviated", async () => {
    const failedItems = Array.from({ length: 6 }, (_, index) => ({
      ...syncRunFixture.items[0],
      id: `item-${index + 1}`,
      packageId: `Elsa.Failed.${index + 1}`
    }));

    renderWithQueryClient(<SyncRunDetailsPage />, { ...syncRunFixture, items: failedItems }, 200, "/admin/sync-runs/:runId");

    expect(await screen.findByText("1 more item is shown in the full table below.")).toBeInTheDocument();
  });
});

function jsonResponse(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
}
