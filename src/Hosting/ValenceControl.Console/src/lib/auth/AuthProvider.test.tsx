import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter, Route, Routes, useSearchParams } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AuthProvider, RequireCustomerAuth, safeReturnUrl } from "@/lib/auth/AuthProvider";
import type { CustomerAuthSession } from "@/lib/auth/authModels";

describe("RequireCustomerAuth", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("sends an unauthenticated visitor to the login route with the page they asked for", async () => {
    renderGuarded({ authenticated: false }, "/admin/sources?tab=active");

    expect(await screen.findByTestId("login-route")).toHaveTextContent("/admin/sources?tab=active");
    expect(screen.queryByText("Guarded page")).not.toBeInTheDocument();
  });

  it("renders the guarded page for an authenticated visitor", async () => {
    renderGuarded({ authenticated: true }, "/admin/sources");

    expect(await screen.findByText("Guarded page")).toBeInTheDocument();
    expect(screen.queryByTestId("login-route")).not.toBeInTheDocument();
  });

  it("explains an unconfigured identity provider instead of redirecting into a dead end", async () => {
    renderGuarded({ loginEnabled: false, authenticated: false }, "/admin/sources");

    expect(await screen.findByText("Customer login is not configured")).toBeInTheDocument();
    expect(screen.queryByTestId("login-route")).not.toBeInTheDocument();
  });
});

describe("safeReturnUrl", () => {
  it.each([
    ["/admin/artifacts?page=2", "/admin/artifacts?page=2"],
    ["https://evil.example/admin", "/admin/overview"],
    ["//evil.example/admin", "/admin/overview"],
    ["admin/artifacts", "/admin/overview"],
    ["/admin/login", "/admin/overview"],
    ["/admin/login?returnUrl=%2Fadmin%2Fsources", "/admin/overview"],
    [null, "/admin/overview"],
    [undefined, "/admin/overview"]
  ])("maps %s to %s", (requested, expected) => {
    expect(safeReturnUrl(requested)).toBe(expected);
  });
});

function renderGuarded(session: Partial<CustomerAuthSession>, initialPath: string) {
  vi.stubGlobal("fetch", vi.fn(async () => Response.json({
    loginEnabled: true,
    authenticated: false,
    displayName: null,
    email: null,
    loginPath: "/api/auth/login",
    logoutPath: "/api/auth/logout",
    ...session
  } satisfies CustomerAuthSession)));

  render(
    <TestProviders initialPath={initialPath}>
      <Routes>
        <Route path="/admin/login" element={<LoginRouteProbe />} />
        <Route path="/admin/sources" element={<RequireCustomerAuth><p>Guarded page</p></RequireCustomerAuth>} />
      </Routes>
    </TestProviders>
  );
}

/** Stands in for the real login page so a redirect can be asserted through the return URL it receives. */
function LoginRouteProbe() {
  const [searchParams] = useSearchParams();
  return <p data-testid="login-route">{searchParams.get("returnUrl")}</p>;
}

function TestProviders({ children, initialPath }: { children: ReactNode; initialPath: string }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return (
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialPath]}>
        <AuthProvider>{children}</AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>
  );
}
