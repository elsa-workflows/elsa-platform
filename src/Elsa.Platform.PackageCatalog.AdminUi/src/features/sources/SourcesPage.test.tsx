import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SourcesPage } from "@/features/sources/SourcesPage";
import { sourceFixture } from "@/test/fixtures";

function renderSourcesPageWithFetch(fetchMock: typeof fetch) {
  vi.stubGlobal("fetch", fetchMock);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <SourcesPage />
      </MemoryRouter>
    </QueryClientProvider>
  );
}

function renderSourcesPage(response: unknown, status = 200) {
  renderSourcesPageWithFetch(vi.fn(async () => new Response(JSON.stringify(response), { status, headers: { "Content-Type": "application/json" } })) as unknown as typeof fetch);
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("SourcesPage", () => {
  it("shows loading then empty state", async () => {
    renderSourcesPage([]);

    expect(screen.getByText("Loading sources")).toBeInTheDocument();
    expect(await screen.findByText("No package sources")).toBeInTheDocument();
  });

  it("shows populated source rows with health and sync evidence", async () => {
    renderSourcesPage([sourceFixture]);

    expect(await screen.findByRole("link", { name: sourceFixture.name })).toBeInTheDocument();
    expect(screen.getByText("Healthy")).toBeInTheDocument();
    expect(screen.getByText("12")).toBeInTheDocument();
  });

  it("shows syncing status while a source sync request is pending", async () => {
    let finishSync!: () => void;
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = input instanceof Request ? input.url : input.toString();
      if (url.endsWith("/api/admin/sync/sources/source-1")) {
        await new Promise<void>((resolve) => {
          finishSync = resolve;
        });
        return new Response(JSON.stringify({ id: "sync-1", status: "Completed" }), { headers: { "Content-Type": "application/json" } });
      }

      return new Response(JSON.stringify([sourceFixture]), { headers: { "Content-Type": "application/json" } });
    }) as unknown as typeof fetch;
    renderSourcesPageWithFetch(fetchMock);

    await screen.findByRole("link", { name: sourceFixture.name });
    await userEvent.click(screen.getByRole("button", { name: "Sync" }));

    expect(await screen.findAllByText("Syncing")).toHaveLength(2);
    await userEvent.type(screen.getByPlaceholderText("Filter sources"), "syncing");

    expect(screen.getByRole("link", { name: sourceFixture.name })).toBeInTheDocument();

    finishSync();
    await waitFor(() => expect(screen.queryByText("Syncing")).not.toBeInTheDocument());
  });

  it("shows syncing status reported by the source API", async () => {
    renderSourcesPage([{ ...sourceFixture, isSyncing: true }]);

    expect(await screen.findAllByText("Syncing")).toHaveLength(2);
    expect(screen.getByRole("button", { name: "Syncing" })).toBeDisabled();
  });

  it("links each source row to the edit form", async () => {
    renderSourcesPage([sourceFixture]);

    expect(await screen.findByRole("link", { name: "Edit" })).toHaveAttribute("href", "/admin/sources/source-1/edit");
  });

  it("shows the latest sync failure detail", async () => {
    renderSourcesPage([{ ...sourceFixture, status: "Error", lastSyncError: "Feed index is unreachable." }]);

    expect(await screen.findByText("Sync failing")).toBeInTheDocument();
    expect(screen.getByText("Feed index is unreachable.")).toBeInTheDocument();
  });

  it("filters populated source rows", async () => {
    renderSourcesPage([sourceFixture]);

    await screen.findByRole("link", { name: sourceFixture.name });
    await userEvent.type(screen.getByPlaceholderText("Filter sources"), "missing");

    expect(screen.getByText("No matching sources")).toBeInTheDocument();
  });

  it("shows error state when no source data is available", async () => {
    renderSourcesPage({ title: "Unavailable" }, 503);

    expect(await screen.findByText("Sources could not load")).toBeInTheDocument();
  });
});
