import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PackageDetailsPage } from "@/features/packages/PackageDetailsPage";
import { handleMockAdminRequest } from "@/test/adminApiHandlers";
import type { MockResponse } from "@/test/adminApiHandlers";
import { packageDetailsFixture } from "@/test/fixtures";

export function renderPackageDetailsPage(
  initialEntry = "/admin/packages/Elsa.Persistence.PostgreSql",
  handler: (path: string, method?: string) => MockResponse = handleMockAdminRequest
) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const response = handler(input.toString(), init?.method ?? "GET");
    return new Response(response.body === undefined ? null : JSON.stringify(response.body), {
      status: response.status,
      headers: response.body === undefined ? undefined : { "Content-Type": "application/json" }
    });
  });

  vi.stubGlobal("fetch", fetchMock);
  vi.stubGlobal("confirm", vi.fn(() => true));
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });

  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/admin/packages/:packageId" element={<PackageDetailsPage />} />
          <Route path="/admin/packages/:packageId/versions/:version" element={<PackageDetailsPage />} />
          <Route path="/admin/packages/:packageId/versions/:version/:section" element={<PackageDetailsPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );

  return fetchMock;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("PackageDetailsPage", () => {
  it("renders summary, source, selected latest version, visibility reasons, and canonical casing", async () => {
    renderPackageDetailsPage("/admin/packages/elsa.persistence.postgresql");

    expect(await screen.findByRole("heading", { name: "Elsa.Persistence.PostgreSql" })).toBeInTheDocument();
    expect(screen.getByText(/Version 1\.0\.2/)).toBeInTheDocument();
    expect(screen.getByText("Elsa Official")).toBeInTheDocument();
    expect(screen.getByText("VersionPendingApproval")).toBeInTheDocument();
  });

  it("shows an empty state for packages without indexed versions", async () => {
    renderPackageDetailsPage("/admin/packages/Elsa.Empty");

    expect(await screen.findByRole("heading", { name: "Elsa.Empty" })).toBeInTheDocument();
    expect(screen.getByText("No indexed versions")).toBeInTheDocument();
  });

  it("shows not found state for missing packages", async () => {
    renderPackageDetailsPage("/admin/packages/Elsa.Missing");

    expect(await screen.findByText("Package not found")).toBeInTheDocument();
  });

  it("does not show stale package data after access is denied", async () => {
    renderPackageDetailsPage("/admin/packages/Elsa.Persistence.PostgreSql", () => ({ status: 403, body: { title: "Forbidden" } }));

    expect(await screen.findByText("Access problem")).toBeInTheDocument();
    expect(screen.queryByText("Elsa.Persistence.PostgreSql")).not.toBeInTheDocument();
  });

  it("renders version badges and deep links while preserving the active section", async () => {
    renderPackageDetailsPage("/admin/packages/Elsa.Persistence.PostgreSql/versions/1.0.2/manifest");

    expect(await screen.findByRole("heading", { name: "Elsa.Persistence.PostgreSql" })).toBeInTheDocument();
    expect(screen.getAllByText("Pending").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Valid").length).toBeGreaterThan(0);
    expect(screen.getByRole("link", { name: /1\.0\.1/ })).toHaveAttribute(
      "href",
      "/admin/packages/Elsa.Persistence.PostgreSql/versions/1.0.1/manifest"
    );
  });

  it("renders unique deep-link targets for dependencies and actions", async () => {
    renderPackageDetailsPage("/admin/packages/Elsa.Persistence.PostgreSql/versions/1.0.2/actions");

    expect(await screen.findByRole("heading", { name: "Elsa.Persistence.PostgreSql" })).toBeInTheDocument();
    expect(document.querySelectorAll("#package-details-dependencies")).toHaveLength(1);
    expect(document.querySelector("#package-details-actions")).toHaveTextContent("Version Actions");
    expect(document.querySelector("#package-details-validation")).toHaveTextContent("Validation Findings");
  });

  it("recovers when a direct version link no longer matches an indexed version", async () => {
    renderPackageDetailsPage("/admin/packages/Elsa.Persistence.PostgreSql/versions/9.9.9");

    expect(await screen.findByText("Version not available")).toBeInTheDocument();
    expect(screen.getByText(/Showing the latest available version/)).toBeInTheDocument();
  });

  it("shows validation findings", async () => {
    renderPackageDetailsPage();

    expect(await screen.findByText("RecommendedDescription")).toBeInTheDocument();
  });

  it("keeps package details visible when validation findings fail to load", async () => {
    renderPackageDetailsPage("/admin/packages/Elsa.Persistence.PostgreSql", (path, method) => {
      if (path.endsWith("/validation")) return { status: 503, body: { title: "Unavailable" } };
      return handleMockAdminRequest(path, method);
    });

    expect(await screen.findByRole("heading", { name: "Elsa.Persistence.PostgreSql" })).toBeInTheDocument();
    expect(await screen.findByText("Validation findings could not load")).toBeInTheDocument();
  });

  it("renders features, settings, compatibility, and manifest content", async () => {
    renderPackageDetailsPage();

    expect(await screen.findByText("PostgreSQL Persistence")).toBeInTheDocument();
    expect(screen.getByText("Connection string")).toBeInTheDocument();
    expect(screen.getByText("[4.0.0,5.0.0)")).toBeInTheDocument();
    expect(screen.getAllByText(/Elsa.Persistence.PostgreSql/).length).toBeGreaterThan(0);
  });

  it("requires rejection reasons and sends version actions with the selected version", async () => {
    const fetchMock = renderPackageDetailsPage();

    expect(await screen.findByRole("button", { name: "Approve Version" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reject Version" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Rejection reason"), { target: { value: "Not ready" } });
    fireEvent.click(screen.getByRole("button", { name: "Reject Version" }));

    await screen.findByText("Version rejected.");
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/api/admin/packages/Elsa.Persistence.PostgreSql/versions/1.0.2/reject"),
      expect.objectContaining({ method: "POST" })
    );
  });

  it("refreshes the reviewed state token before a version action", async () => {
    let detailsRequests = 0;
    const refreshedDetails = {
      ...packageDetailsFixture,
      versions: packageDetailsFixture.versions.map((version) =>
        version.version === "1.0.2" ? { ...version, versionStateToken: "state-102-refreshed" } : version
      )
    };
    const fetchMock = renderPackageDetailsPage("/admin/packages/Elsa.Persistence.PostgreSql", (path, method) => {
      if (path.endsWith("/api/admin/packages/Elsa.Persistence.PostgreSql")) {
        detailsRequests += 1;
        return { status: 200, body: detailsRequests > 1 ? refreshedDetails : packageDetailsFixture };
      }

      return handleMockAdminRequest(path, method);
    });

    expect(await screen.findByRole("button", { name: "Approve Version" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Refresh" }));
    await waitFor(() => expect(detailsRequests).toBeGreaterThan(1));
    fireEvent.click(screen.getByRole("button", { name: "Approve Version" }));

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([url, init]) => {
          if (!url.toString().includes("/api/admin/packages/Elsa.Persistence.PostgreSql/versions/1.0.2/approve") || init?.method !== "POST")
            return false;

          const body = JSON.parse(init.body?.toString() ?? "{}") as { expectedStateToken?: string };
          return body.expectedStateToken === "state-102-refreshed";
        })
      ).toBe(true)
    );
  });
});
