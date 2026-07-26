import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { WorkspaceContextProvider } from "@/app/WorkspaceContextProvider";
import { HealingComponentsPage, HealingConfigurationPage } from "@/features/healing/HealingConfigurationPage";
import { AuthProvider } from "@/lib/auth/AuthProvider";

describe("Healing configuration", () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it("shows effective controls and persists an authorized configuration change", async () => {
    const { fetchMock } = renderPage("configuration");

    expect(await screen.findByRole("heading", { name: "Healing configuration" }, { timeout: 5_000 })).toBeInTheDocument();
    expect(screen.getByText("Acme Claims API")).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "Automatic exception discovery" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Repair dispatch" })).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Production discovery" })).toBeChecked();
    expect(screen.getByRole("spinbutton", { name: "Production occurrence threshold" })).toHaveValue(3);

    await userEvent.click(screen.getByRole("checkbox", { name: "Repair dispatch" }));
    await userEvent.click(screen.getByRole("button", { name: "Save configuration" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/healing/applications/${applicationId}/configuration`),
      expect.objectContaining({ method: "PUT" })
    ));
    const request = requestBody(fetchMock, "PUT", "/configuration");
    expect(request.repairDispatchEnabled).toBe(true);
  });

  it("requires a target-specific confirmation before emergency stop", async () => {
    const { fetchMock } = renderPage("configuration");
    expect(await screen.findByRole("heading", { name: "Healing configuration" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Activate emergency stop" }));

    const dialog = screen.getByRole("dialog", { name: "Stop Healing for Acme Claims API" });
    expect(dialog).toHaveTextContent("New repair dispatch, publication, and automatic merge will stop immediately");
    const confirm = screen.getByRole("button", { name: "Stop Healing now" });
    expect(confirm).toHaveFocus();
    await userEvent.click(confirm);

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/healing/applications/${applicationId}/confirmations`),
      expect.objectContaining({ method: "POST" })
    ));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/healing/applications/${applicationId}/stop`),
      expect.objectContaining({ method: "POST" })
    ));
    expect(requestBody(fetchMock, "POST", "/stop")).toEqual({ confirmationId: "confirmation-stop" });
    expect(await screen.findByText("Emergency stop active")).toBeInTheDocument();
  });

  it("restores emergency-stop trigger focus when confirmation is cancelled", async () => {
    renderPage("configuration");
    const trigger = await screen.findByRole("button", { name: "Activate emergency stop" });
    await userEvent.click(trigger);
    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(trigger).toHaveFocus());
  });

  it("keeps the emergency-stop dialog retryable and announces mutation failures", async () => {
    renderPage("configuration", { failEmergencyMutation: true });
    await userEvent.click(await screen.findByRole("button", { name: "Activate emergency stop" }));
    await userEvent.click(screen.getByRole("button", { name: "Stop Healing now" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Healing could not be stopped");
    expect(screen.getByRole("dialog", { name: "Stop Healing for Acme Claims API" })).toBeInTheDocument();
  });

  it("requires confirmation to resume from an emergency stop", async () => {
    const { fetchMock } = renderPage("configuration", { applicationKillSwitch: true });
    const trigger = await screen.findByRole("button", { name: "Resume Healing" });
    await userEvent.click(trigger);
    const dialog = screen.getByRole("dialog", { name: "Resume Healing for Acme Claims API" });
    expect(dialog).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole("button", { name: "Resume Healing" }));
    await waitFor(() => expect(requestBody(fetchMock, "POST", "/confirmations"))
      .toMatchObject({ actionType: "HealingEmergencyResume" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/healing/applications/${applicationId}/resume`),
      expect.objectContaining({ method: "POST" })
    ));
  });

  it("keeps the emergency-resume dialog retryable and announces mutation failures", async () => {
    renderPage("configuration", { applicationKillSwitch: true, failEmergencyMutation: true });
    await userEvent.click(await screen.findByRole("button", { name: "Resume Healing" }));
    const dialog = screen.getByRole("dialog", { name: "Resume Healing for Acme Claims API" });
    await userEvent.click(within(dialog).getByRole("button", { name: "Resume Healing" }));
    expect(await within(dialog).findByRole("alert")).toHaveTextContent("Healing could not be resumed");
  });

  it("shows the exact automatic-merge target before requesting a server confirmation", async () => {
    const { fetchMock } = renderPage("configuration", { permissions: ["healing.read", "healing.configure", "healing.automerge.configure"] });
    expect(await screen.findByRole("heading", { name: "Healing configuration" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("checkbox", { name: "Automatic merge" }));
    await userEvent.click(screen.getByRole("button", { name: "Save configuration" }));
    expect(screen.getByRole("dialog", { name: "Enable automatic merge for Acme Claims API" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Confirm automatic merge change" }));
    await waitFor(() => expect(requestBody(fetchMock, "POST", "/confirmations")).toMatchObject({ actionType: "HealingAutomaticMerge", automaticMergeEnabled: true }));
    await waitFor(() => expect(requestBody(fetchMock, "PUT", "/configuration")).toMatchObject({ automaticMergeEnabled: true, confirmationId: "confirmation-stop" }));
  });

  it("restores the configuration trigger after cancelling automatic merge confirmation", async () => {
    renderPage("configuration", { permissions: ["healing.read", "healing.configure", "healing.automerge.configure"] });
    expect(await screen.findByRole("heading", { name: "Healing configuration" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("checkbox", { name: "Automatic merge" }));
    const trigger = screen.getByRole("button", { name: "Save configuration" });
    await userEvent.click(trigger);
    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(trigger).toHaveFocus());
  });

  it("keeps mutation controls unavailable without healing.configure", async () => {
    renderPage("configuration", { permissions: ["healing.read"] });

    expect(await screen.findByRole("heading", { name: "Healing configuration" })).toBeInTheDocument();
    expect(screen.getByText("You can review effective policy, but healing.configure is required to make changes.")).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "Automatic exception discovery" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Save configuration" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Activate emergency stop" })).toBeDisabled();
  });

  it("shows manifest trust, unauthorized suggestions, and ambiguity blockers", async () => {
    renderPage("components");

    expect(await screen.findByRole("heading", { name: "Components and source ownership" })).toBeInTheDocument();
    expect(await screen.findByRole("columnheader", { name: "Component" })).toBeInTheDocument();
    expect(screen.getByText("Elsa.Acme.Claims")).toBeInTheDocument();
    expect(screen.getByText("Owner verified — observation only")).toBeInTheDocument();
    expect(screen.getByText("Suggested—not authorized")).toBeInTheDocument();
    expect(screen.getByText("Ambiguous—repair blocked")).toBeInTheDocument();
    expect(screen.getByText("Acme.Claims repair ownership")).toBeInTheDocument();
  });

  it("registers and verifies manifests, then activates and suspends owner-approved bindings", async () => {
    const { fetchMock } = renderPage("components");
    expect(await screen.findByRole("heading", { name: "Components and source ownership" })).toBeInTheDocument();

    await userEvent.type(await screen.findByLabelText("Revision ID"), "00000000-0000-0000-0000-000000000042");
    fireEvent.change(screen.getByLabelText("Manifest JSON"), { target: { value: canonicalManifestJson } });
    await userEvent.click(screen.getByRole("button", { name: "Register manifest" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/revisions/00000000-0000-0000-0000-000000000042/component-manifests"),
      expect.objectContaining({ method: "POST" })
    ));
    const uploadCall = fetchMock.mock.calls.find(([, init]) => init?.method === "POST" && new Headers(init.headers).has("Content-Digest"));
    expect(uploadCall).toBeDefined();

    await userEvent.click(await screen.findByRole("button", { name: "Verify manifest" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining("/component-manifests/manifest-new/verify"), expect.objectContaining({ method: "POST" })));

    await userEvent.type(screen.getByLabelText("Binding name"), "Claims owner");
    await userEvent.type(screen.getByLabelText("Package selector"), "Elsa.Acme.*");
    await userEvent.selectOptions(screen.getByLabelText("Provider connection"), "provider-1");
    await userEvent.selectOptions(screen.getByLabelText("Path policy"), "path-policy-1");
    await userEvent.selectOptions(screen.getByLabelText("Evidence policy"), "evidence-policy-1");
    await userEvent.selectOptions(screen.getByLabelText("Merge policy"), "merge-policy-1");
    await userEvent.type(screen.getByLabelText("Workflow identity"), ".github/workflows/healing.yml");
    await userEvent.type(screen.getByLabelText("Workflow branch or tag"), "refs/tags/valence-control-healing-v1");
    await userEvent.type(screen.getByLabelText("Workflow revision"), "refs/heads/main");
    await userEvent.click(screen.getByRole("button", { name: "Create binding draft" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining("/source-ownership-bindings"), expect.objectContaining({ method: "POST" })));
    await userEvent.click(await screen.findByRole("button", { name: "Activate" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining("/source-ownership-bindings/binding-draft/activate"), expect.objectContaining({ method: "POST" })));
    await userEvent.click(await screen.findByRole("button", { name: "Suspend" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining("/source-ownership-bindings/binding-draft/suspend"), expect.objectContaining({ method: "POST" })));
  }, 20_000);

  it("creates an authorized GitHub repository and safe policy profile without raw IDs", async () => {
    const { fetchMock } = renderPage("components", { permissions: ["healing.read", "healing.configure", "healing.automerge.configure"] });
    expect(await screen.findByRole("heading", { name: "Connect a GitHub repair repository" })).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText("GitHub installation ID"), "42");
    await userEvent.type(screen.getByLabelText("GitHub repository owner"), "elsa-workflows");
    await userEvent.type(screen.getByLabelText("GitHub repository name"), "elsa-core");
    await userEvent.selectOptions(screen.getByLabelText("GitHub App credential"), "credential-1");
    await userEvent.selectOptions(screen.getByLabelText("GitHub webhook HMAC credential"), "credential-2");
    await userEvent.click(screen.getByRole("checkbox", { name: "Allow automatic merge when all gates pass" }));
    await userEvent.click(screen.getByRole("button", { name: "Create pending connection and policies" }));
    expect(screen.getByRole("dialog", { name: "Enable automatic merge for this repair authority" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Confirm automatic merge change" }));
    await waitFor(() => expect(requestBody(fetchMock, "POST", "/confirmations"))
      .toMatchObject({ actionType: "HealingAutomaticMerge", automaticMergeEnabled: true }));
    await waitFor(() => expect(requestBody(fetchMock, "POST", "/authority-profiles")).toMatchObject({
      repositoryOwner: "elsa-workflows",
      repositoryName: "elsa-core",
      credentialReferenceId: "credential-1",
      webhookSecretCredentialReferenceId: "credential-2",
      requireReproduction: false,
      allowHighConfidenceInference: true,
      automaticMergeEnabled: true,
      confirmationId: "confirmation-stop"
    }));
    expect(await screen.findByText("Policy profile created. Validate the provider connection before creating an active binding.")).toBeInTheDocument();
    await userEvent.click(await screen.findByRole("button", { name: "Validate GitHub connection" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/provider-connections/provider-1/validate"),
      expect.objectContaining({ method: "POST" })
    ));
    await userEvent.click(screen.getByRole("button", { name: "Suspend provider" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/provider-connections/provider-1/suspend"),
      expect.objectContaining({ method: "POST" })
    ));
    await userEvent.click(await screen.findByRole("button", { name: "Revalidate and activate" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/provider-connections/provider-1/validate"),
      expect.objectContaining({ method: "POST" })
    ));
  }, 20_000);

  it("lets configure-only members draft ownership without approval controls", async () => {
    renderPage("components", { canApproveOwnership: false });
    expect(await screen.findByRole("button", { name: "Create binding draft" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Revoke manifest trust" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Suspend" })).not.toBeInTheDocument();
  });

  it("renders a configuration error instead of an indefinite loading state", async () => {
    renderPage("configuration", { failHealing: true });
    expect(await screen.findByRole("heading", { name: "Healing configuration could not load" })).toBeInTheDocument();
  });

  it("renders explicit empty and error states", async () => {
    const { rerenderWithFailure } = renderPage("components", { emptyComponents: true });

    expect(await screen.findByRole("heading", { name: "No component manifests registered" })).toBeInTheDocument();
    rerenderWithFailure();
    expect(await screen.findByRole("heading", { name: "Healing components could not load" })).toBeInTheDocument();
  });
});

function renderPage(
  view: "configuration" | "components",
  options: { permissions?: string[]; emptyComponents?: boolean; failHealing?: boolean; failEmergencyMutation?: boolean; canApproveOwnership?: boolean; applicationKillSwitch?: boolean } = {}
) {
  let fail = options.failHealing ?? false;
  let manifestState = manifestFixture;
  let bindingItems = [bindingFixture];
  let providerState = providerFixture;
  const emptyComponents = Object.prototype.hasOwnProperty.call(options, "emptyComponents") && options.emptyComponents === true;
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = input instanceof Request ? input.url : input.toString();
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    if (url.endsWith("/api/auth/session"))
      return json({ loginEnabled: true, authenticated: true, displayName: "Ada", email: "ada@example.test", loginPath: "/api/auth/login", logoutPath: "/api/auth/logout" });
    if (url.endsWith("/api/me/organizations"))
      return json(workspaceContextFixture());
    if (fail && url.includes("/healing/"))
      return json({ title: "Unavailable" }, 503);
    if (method === "POST" && url.endsWith(`/applications/${applicationId}/confirmations`))
      return json({ id: "confirmation-stop", actionType: "HealingEmergencyStop", targetId: applicationId, expiresAt: "2026-07-16T12:05:00Z" }, 201);
    if (method === "POST" && url.endsWith(`/applications/${applicationId}/stop`))
      return options.failEmergencyMutation
        ? json({ title: "Unavailable" }, 503)
        : json({ ...configurationFixture(options.permissions), applicationKillSwitch: true });
    if (method === "POST" && url.endsWith(`/applications/${applicationId}/resume`))
      return options.failEmergencyMutation
        ? json({ title: "Unavailable" }, 503)
        : json({ ...configurationFixture(options.permissions), applicationKillSwitch: false });
    if (method === "PUT" && url.endsWith(`/applications/${applicationId}/configuration`))
      return json({ ...configurationFixture(options.permissions), ...JSON.parse(init?.body?.toString() ?? "{}") });
    if (url.endsWith(`/applications/${applicationId}/configuration`))
      return json({ ...configurationFixture(options.permissions), applicationKillSwitch: options.applicationKillSwitch ?? false });
    if (method === "POST" && url.includes("/revisions/") && url.endsWith("/component-manifests"))
    {
      manifestState = { ...manifestFixture, id: "manifest-new", trustState: "Unverified" };
      return json(manifestState, 201);
    }
    if (method === "POST" && url.includes("/component-manifests/"))
    {
      manifestState = { ...manifestState, trustState: url.endsWith("/verify") ? "Verified" : "Revoked" };
      return json(manifestState);
    }
    if (url.endsWith(`/applications/${applicationId}/component-manifests`))
      return json({ items: emptyComponents ? [] : [manifestState], canApproveOwnership: options.canApproveOwnership ?? true });
    if (method === "POST" && url.endsWith("/authority-profiles"))
    {
      providerState = { ...providerFixture, status: "PendingValidation" };
      return json({ ...authorityProfileFixture, providerConnection: providerState }, 201);
    }
    if (method === "POST" && url.includes("/provider-connections/"))
    {
      providerState = { ...providerState, status: url.endsWith("/activate") || url.endsWith("/validate") ? "Active" : url.endsWith("/suspend") ? "Suspended" : "Revoked", version: "BAUG" };
      return json(providerState);
    }
    if (url.endsWith(`/applications/${applicationId}/authority-catalog`))
      return json({ ...authorityCatalogFixture, providerConnections: [providerState] });
    if (method === "POST" && url.endsWith("/source-ownership-bindings"))
    {
      const draft = { ...bindingFixture, id: "binding-draft", name: "Claims owner", status: "Draft" };
      bindingItems = [draft];
      return json(draft);
    }
    if (method === "POST" && url.includes("/source-ownership-bindings/"))
    {
      const status = url.endsWith("/activate") ? "Active" : url.endsWith("/suspend") ? "Suspended" : "Revoked";
      bindingItems = bindingItems.map((item) => ({ ...item, status }));
      return json(bindingItems[0]);
    }
    if (url.endsWith(`/applications/${applicationId}/source-ownership-bindings`))
      return json({ items: emptyComponents ? [] : bindingItems, permissions: options.permissions ?? ["healing.read", "healing.configure"], canApproveOwnership: options.canApproveOwnership ?? true });
    if (url.endsWith(`/api/workspaces/${workspaceId}/deployments/credential-references`))
      return json({ items: [
        { id: "credential-1", name: "Healing GitHub App", secretStoreName: "Local protected credentials", status: "Active" },
        { id: "credential-2", name: "Healing webhook HMAC", secretStoreName: "Local protected credentials", status: "Active" }
      ] });
    return json({ title: "Not found" }, 404);
  });
  vi.stubGlobal("fetch", fetchMock);
  vi.stubGlobal("confirm", vi.fn(() => true));

  const route = `/admin/healing/applications/${applicationId}/${view}`;
  const renderTree = () => render(
    <TestQueryProvider>
      <MemoryRouter initialEntries={[route]}>
        <AuthProvider>
          <WorkspaceContextProvider>
            <Routes>
              <Route path="/admin/healing/applications/:applicationId/configuration" element={<HealingConfigurationPage />} />
              <Route path="/admin/healing/applications/:applicationId/components" element={<HealingComponentsPage />} />
            </Routes>
          </WorkspaceContextProvider>
        </AuthProvider>
      </MemoryRouter>
    </TestQueryProvider>
  );
  const result = renderTree();

  return {
    ...result,
    fetchMock,
    rerenderWithFailure() {
      fail = true;
      result.unmount();
      renderTree();
    }
  };
}

function configurationFixture(permissions = ["healing.read", "healing.configure"]) {
  return {
    applicationId,
    applicationName: "Acme Claims API",
    discoveryEnabled: true,
    repairDispatchEnabled: false,
    automaticMergeEnabled: false,
    applicationKillSwitch: false,
    signalProfileVersion: "1.0",
    defaultAttemptLimit: 2,
    verificationWindow: "00:15:00",
    timeBudget: "00:30:00",
    concurrencyBudget: 4,
    inferenceBudget: 200000,
    repositoryRunBudget: 2,
    version: "AQID",
    manifestReadiness: "Ready",
    providerReadiness: "Ready",
    environments: [{ environmentId: "env-production", name: "Production", discoveryEnabled: true, repairDispatchEnabled: false, environmentKillSwitch: false, occurrenceThreshold: 3, debounceWindow: "00:05:00" }],
    permissions
  };
}

const manifestFixture = {
  id: "manifest-1",
  revisionId: "revision-42",
  sourceRevision: "abc123",
  manifestDigest: "sha256:manifest",
  trustState: "Verified",
  createdAt: "2026-07-16T12:00:00Z",
  entries: [
    {
      componentKey: "package:Elsa.Acme.Claims/1.2.3",
      kind: "Package",
      name: "Elsa.Acme.Claims",
      version: "1.2.3",
      contentHash: "sha256:component",
      repositorySuggestion: "github.com/acme/claims",
      bindingId: null,
      ownershipResolution: "Suggested",
      repairEligibility: "Ambiguous"
    }
  ]
};

const bindingFixture = {
  id: "binding-1",
  name: "Acme.Claims repair ownership",
  selectorKind: "Package",
  selectorPattern: "Elsa.Acme.*",
  repository: "acme/claims",
  targetBranch: "main",
  workflowIdentity: ".github/workflows/valence-control-healing.yml",
  workflowReference: "refs/tags/valence-control-healing-v1",
  status: "Active",
  version: "AQID"
};

const providerFixture = {
  id: "provider-1",
  provider: "GitHub",
  installationId: "42",
  repositoryProviderId: "github-repository-1",
  repositoryOwner: "acme",
  repositoryName: "claims",
  status: "Active",
  updatedAt: "2026-07-16T12:00:00Z",
  version: "AQID"
};

const pathPolicyFixture = { id: "path-policy-1", name: "Default paths", policyVersion: "1", policyHash: "sha256:path" };
const evidencePolicyFixture = { id: "evidence-policy-1", name: "Default evidence", policyVersion: "1", policyHash: "sha256:evidence" };
const mergePolicyFixture = { id: "merge-policy-1", name: "Default merge", policyVersion: "1", policyHash: "sha256:merge" };
const authorityCatalogFixture = {
  providerConnections: [providerFixture],
  pathPolicies: [pathPolicyFixture],
  evidencePolicies: [evidencePolicyFixture],
  mergePolicies: [mergePolicyFixture]
};
const authorityProfileFixture = {
  providerConnection: providerFixture,
  pathPolicy: pathPolicyFixture,
  evidencePolicy: evidencePolicyFixture,
  mergePolicy: mergePolicyFixture
};

const canonicalManifestJson = '{"application":{"name":"Claims","runtimeIdentifier":null,"targetFramework":"net10.0","version":"1.0.0"},"components":[],"manifestDigest":"sha256:0000000000000000000000000000000000000000000000000000000000000000","revision":{"buildId":null,"createdAt":"2026-07-16T12:00:00+00:00","repositoryUrl":null,"sourceRevision":"0000000000000000000000000000000000000000"},"schemaVersion":"1.0"}';

function workspaceContextFixture() {
  return {
    account: { id: "account-1", displayName: "Ada", email: "ada@example.test" },
    organizations: [{ id: "org-1", name: "Acme", role: "Owner" }],
    workspaces: [{ id: workspaceId, name: "Acme", kind: "Shared", role: "Owner", organizationId: "org-1", organizationName: "Acme", organizationRole: "Owner" }]
  };
}

function requestBody(fetchMock: ReturnType<typeof vi.fn>, method: string, suffix: string) {
  const call = fetchMock.mock.calls.find(([input, init]) => {
    const url = input instanceof Request ? input.url : input.toString();
    return url.endsWith(suffix) && init?.method === method;
  });
  expect(call).toBeDefined();
  return JSON.parse(call![1]?.body?.toString() ?? "{}") as Record<string, unknown>;
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function TestQueryProvider({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

const workspaceId = "00000000-0000-0000-0000-000000000010";
const applicationId = "00000000-0000-0000-0000-000000000020";
