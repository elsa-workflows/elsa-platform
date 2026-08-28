import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PackagesPage } from "@/features/packages/PackagesPage";
import { packageFixture } from "@/test/fixtures";

function renderPackagesPage(response: unknown, status = 200, initialEntry = "/admin/packages") {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = input.toString();
    if (init?.method === "POST") {
      return new Response(null, { status: 204 });
    }
    if (url.endsWith("/api/admin/packages")) {
      return new Response(JSON.stringify(response), { status, headers: { "Content-Type": "application/json" } });
    }
    return new Response(JSON.stringify({ title: "Not found" }), { status: 404, headers: { "Content-Type": "application/json" } });
  });

  vi.stubGlobal("fetch", fetchMock);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <PackagesPage />
      </MemoryRouter>
    </QueryClientProvider>
  );
  return fetchMock;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("PackagesPage", () => {
  it("shows loading then empty state", async () => {
    renderPackagesPage([]);

    expect(screen.getByText("Loading packages")).toBeInTheDocument();
    expect(await screen.findByText("No indexed packages")).toBeInTheDocument();
  });

  it("shows package rows with status and visibility evidence", async () => {
    renderPackagesPage([packageFixture]);

    expect(await screen.findByRole("link", { name: packageFixture.packageId })).toBeInTheDocument();
    expect(screen.getByText("1.0.2")).toBeInTheDocument();
    expect(screen.getAllByText("Pending").length).toBeGreaterThan(0);
    expect(screen.getByText("Valid")).toBeInTheDocument();
    expect(screen.getByText("00000000-0000-0000-0000-000000000001")).toBeInTheDocument();
    expect(screen.getByText("Listed")).toBeInTheDocument();
    expect(screen.getByText("3")).toBeInTheDocument();
  });

  it("filters package rows from URL-backed state", async () => {
    renderPackagesPage([packageFixture], 200, "/admin/packages?q=missing");

    expect(await screen.findByText("No matching packages")).toBeInTheDocument();
  });

  it("approves selected latest package versions", async () => {
    const fetchMock = renderPackagesPage([packageFixture]);

    await userEvent.click(await screen.findByRole("checkbox", { name: `Select ${packageFixture.packageId}` }));
    await userEvent.click(screen.getByRole("button", { name: "Approve Selected" }));

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        "/api/admin/packages/Elsa.Persistence.PostgreSql/versions/1.0.2/approve",
        expect.objectContaining({ method: "POST" })
      );
    });
    const approveCall = fetchMock.mock.calls.find(([url]) => url.toString().endsWith("/approve"));
    expect(approveCall?.[1]).toHaveProperty("body", JSON.stringify({ expectedStateToken: "state-102-pending-valid" }));
  });

  it("requires a rejection reason before rejecting selected versions", async () => {
    const fetchMock = renderPackagesPage([packageFixture]);

    await userEvent.click(await screen.findByRole("checkbox", { name: `Select ${packageFixture.packageId}` }));
    expect(screen.getByRole("button", { name: "Reject Selected" })).toBeDisabled();

    await userEvent.type(screen.getByLabelText("Rejection reason"), "Missing manifest details");
    await userEvent.click(screen.getByRole("button", { name: "Reject Selected" }));

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        "/api/admin/packages/Elsa.Persistence.PostgreSql/versions/1.0.2/reject",
        expect.objectContaining({ method: "POST" })
      );
    });
  });
  it("keeps the approval filter honest when the same package is indexed from two sources", async () => {
    // The catalog holds one entry per (packageId, source). Rendering both under the same React key
    // used to mismatch rows against their data, leaving rows visible that the filter had excluded.
    const pending = { ...packageFixture, sourceId: "00000000-0000-0000-0000-000000000001" };
    const approved = {
      ...packageFixture,
      sourceId: "00000000-0000-0000-0000-000000000002",
      approved: true,
      approvalStatus: "Approved",
      latestVersion: "0.9.0",
      versions: [{ ...packageFixture.versions[0], version: "0.9.0", approvalStatus: "Approved", versionStateToken: "state-090-approved" }]
    };

    renderPackagesPage([pending, approved]);

    // Both rows render first, then the filter narrows them: that re-render is where duplicate keys
    // let React carry stale rows across.
    expect(await screen.findByText("0.9.0")).toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText("Filter packages"), "Pending");

    const rows = within(screen.getByRole("table"));
    await waitFor(() => expect(rows.queryByText("0.9.0")).not.toBeInTheDocument());
    expect(rows.getByText("1.0.2")).toBeInTheDocument();
    expect(rows.queryByText("Approved")).not.toBeInTheDocument();
    expect(rows.getAllByText("Pending")).toHaveLength(1);
  });

  it("selects and clears every listed version from the header checkbox", async () => {
    const other = {
      ...packageFixture,
      sourceId: "00000000-0000-0000-0000-000000000002",
      versions: [{ ...packageFixture.versions[0], versionStateToken: "state-102-second-source" }]
    };
    renderPackagesPage([packageFixture, other]);

    const selectAll = await screen.findByRole("checkbox", { name: "Select all packages" });
    await userEvent.click(selectAll);

    expect(await screen.findByText("2 versions selected")).toBeInTheDocument();

    await userEvent.click(selectAll);
    await waitFor(() => expect(screen.queryByText(/versions selected/)).not.toBeInTheDocument());
  });

  it("ticks only the checkbox of the source that was selected", async () => {
    const other = {
      ...packageFixture,
      sourceId: "00000000-0000-0000-0000-000000000002",
      versions: [{ ...packageFixture.versions[0], versionStateToken: "state-102-second-source" }]
    };
    renderPackagesPage([packageFixture, other]);

    const checkboxes = await screen.findAllByRole("checkbox", { name: `Select ${packageFixture.packageId}` });
    await userEvent.click(checkboxes[0]);

    expect(await screen.findByText("1 version selected")).toBeInTheDocument();
    expect(checkboxes[1]).not.toBeChecked();
  });
});
