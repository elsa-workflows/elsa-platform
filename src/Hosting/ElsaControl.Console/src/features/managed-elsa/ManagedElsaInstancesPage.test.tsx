import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { WorkspaceContextProvider } from "@/app/WorkspaceContextProvider";
import { AuthProvider } from "@/lib/auth/AuthProvider";
import { ManagedElsaInstancesPage } from "@/features/managed-elsa/ManagedElsaInstancesPage";
import type { ManagedElsaInstance } from "@/features/managed-elsa/managedElsaModels";

const organizationId = "00000000-0000-0000-0000-000000000001";
const workspaceId = "00000000-0000-0000-0000-000000000010";
const healthyInstanceId = "00000000-0000-0000-0000-000000000101";
const unavailableInstanceId = "00000000-0000-0000-0000-000000000102";
const unboundInstanceId = "00000000-0000-0000-0000-000000000103";
const callbackUri = "https://managed.example.test/managed-elsa/handoff/callback";
const state = "state-value-that-is-long-enough";
const codeChallenge = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFG";

describe("ManagedElsaInstancesPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    window.sessionStorage.clear();
  });

  it("shows Open only for a healthy instance with a current binding", async () => {
    installFetch({
      instances: [
        instanceFixture(),
        instanceFixture({
          instanceId: unavailableInstanceId,
          name: "Deleting runtime",
          slug: "deleting-runtime",
          desiredLifecycle: "Deleting",
          observedLifecycle: "Deleting",
          health: "Unknown",
          canOpen: false,
          audience: null,
          redirectUri: null,
          unavailableReason: "This instance is not currently available."
        }),
        instanceFixture({
          instanceId: unboundInstanceId,
          name: "Unbound runtime",
          slug: "unbound-runtime",
          canOpen: false,
          audience: null,
          redirectUri: null,
          unavailableReason: "The current instance binding is unavailable."
        })
      ]
    });

    renderPage();

    expect(await screen.findByRole("button", { name: "Open" })).toBeInTheDocument();
    expect(screen.getAllByText("Unavailable")).toHaveLength(2);
    expect(screen.getAllByRole("button", { name: "Open" })).toHaveLength(1);
    expect(screen.queryByText(/urn:elsa:instance/)).not.toBeInTheDocument();
  });

  it("issues for the runtime challenge and posts only code and state to the exact callback", async () => {
    const issue = {
      token: "signed-handoff-token",
      tokenType: "Bearer",
      audience: "urn:elsa:instance:00000000-0000-0000-0000-000000000101",
      redirectUri: callbackUri,
      issuedAt: "2026-08-31T12:00:00Z",
      expiresAt: "2026-08-31T12:01:00Z"
    };
    const fetchMock = installFetch({ instances: [instanceFixture()], issue });
    const submit = vi.spyOn(HTMLFormElement.prototype, "submit").mockImplementation(() => undefined);

    renderPage(`?instance_id=${healthyInstanceId}&state=${state}&code_challenge=${codeChallenge}`);

    await waitFor(() => expect(submit).toHaveBeenCalledTimes(1));
    const issueCall = fetchMock.mock.calls.find(([input]) => String(input).endsWith("/api/managed-elsa/handoff/issue"));
    const issueBody = JSON.parse(String(issueCall?.[1]?.body));
    expect(issueBody).toEqual({
      organizationId,
      instanceId: healthyInstanceId,
      audience: instanceFixture().audience,
      redirectUri: callbackUri,
      codeChallenge
    });

    const form = document.querySelector("form");
    expect(form).toHaveAttribute("method", "post");
    expect(form).toHaveAttribute("action", callbackUri);
    expect(form).not.toHaveAttribute("action", expect.stringContaining(issue.token));
    expect(form?.querySelector("[name=code]")).toHaveValue(issue.token);
    expect(form?.querySelector("[name=state]")).toHaveValue(state);
    expect(form?.querySelector("[name=code_verifier]")).toBeNull();
  });

  it("does not blindly retry an issue request after a 401", async () => {
    const fetchMock = installFetch({
      instances: [instanceFixture()],
      issueResponses: [Response.json({ title: "expired Control session" }, { status: 401 })]
    });

    renderPage(`?instance_id=${healthyInstanceId}&state=${state}&code_challenge=${codeChallenge}`);

    expect(await screen.findByRole("alert")).toHaveTextContent("Your Control session could not authorize this handoff. Sign in again and retry.");
    expect(fetchMock.mock.calls.filter(([input]) => String(input).endsWith("/api/managed-elsa/handoff/issue"))).toHaveLength(1);
  });

  it("maps a runtime 403 continuation to safe actionable copy", async () => {
    installFetch({ instances: [instanceFixture()] });

    renderPage(`?instance_id=${healthyInstanceId}&handoff_status=403`);

    expect(await screen.findByRole("alert")).toHaveTextContent("This managed instance is no longer available to your account.");
    expect(screen.queryByText(/signed-handoff-token|code_verifier|state-value/)).not.toBeInTheDocument();
  });
});

function renderPage(search = "") {
  render(
    <TestQueryProvider>
      <MemoryRouter initialEntries={[`/admin/runtimes${search}`]}>
        <AuthProvider>
          <WorkspaceContextProvider>
            <ManagedElsaInstancesPage />
          </WorkspaceContextProvider>
        </AuthProvider>
      </MemoryRouter>
    </TestQueryProvider>
  );
}

function installFetch({
  instances,
  issue,
  issueResponses = []
}: {
  instances: ManagedElsaInstance[];
  issue?: Record<string, string>;
  issueResponses?: Response[];
}) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = input instanceof Request ? input.url : input.toString();
    if (url.endsWith("/api/auth/session"))
      return Response.json({ loginEnabled: true, authenticated: true, displayName: "Test User", email: "test@example.com" });
    if (url.endsWith("/api/me/organizations"))
      return Response.json({
        account: { id: "account-1", displayName: "Test User", email: "test@example.com" },
        organizations: [{ id: organizationId, name: "Acme Corp", role: "Owner" }],
        workspaces: [{ id: workspaceId, name: "Acme Insurance", kind: "Shared", role: "Owner", organizationId, organizationName: "Acme Corp", organizationRole: "Owner" }]
      });
    if (url.endsWith(`/api/workspaces/${workspaceId}/managed-elsa/instances`))
      return Response.json(instances);
    if (url.endsWith("/api/managed-elsa/handoff/issue")) {
      const response = issueResponses.shift();
      if (response)
        return response;
      return Response.json(issue ?? {
        token: "signed-handoff-token",
        tokenType: "Bearer",
        audience: instanceFixture().audience,
        redirectUri: callbackUri,
        issuedAt: "2026-08-31T12:00:00Z",
        expiresAt: "2026-08-31T12:01:00Z"
      });
    }
    return Response.json({ title: "Not found" }, { status: 404 });
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function instanceFixture(overrides: Partial<ManagedElsaInstance> = {}): ManagedElsaInstance {
  return {
    organizationId,
    instanceId: healthyInstanceId,
    name: "Claims runtime",
    slug: "claims-runtime",
    desiredLifecycle: "Running" as const,
    observedLifecycle: "Ready" as const,
    health: "Healthy" as const,
    canOpen: true,
    audience: "urn:elsa:instance:00000000-0000-0000-0000-000000000101",
    redirectUri: callbackUri,
    unavailableReason: null,
    ...overrides
  };
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
