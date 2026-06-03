import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { WorkspaceContextProvider } from "@/app/WorkspaceContextProvider";
import { DeploymentsPage } from "@/features/deployments/DeploymentsPage";
import { DeploymentSetupPanel } from "@/features/deployments/DeploymentSetupPanel";
import type { DeploymentCockpit, WorkspaceDeploymentTier } from "@/features/deployments/deploymentModels";
import { AuthProvider } from "@/lib/auth/AuthProvider";

describe("DeploymentsPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders workflow applications and environment health without exposing credential values", async () => {
    renderDeployments();

    expect(await screen.findByRole("heading", { name: "Deployments" })).toBeInTheDocument();
    expect(screen.getAllByText("Claims Operations").length).toBeGreaterThan(0);
    expect(screen.getByText("Under Acme Corp")).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: /Prod Production/i })).toBeInTheDocument();
    expect(screen.getAllByText("Drift detected").length).toBeGreaterThan(0);
    expect(screen.queryByText(/password|token|secret value/i)).not.toBeInTheDocument();
  });

  it("shows an empty setup state when the live cockpit has no applications", async () => {
    renderDeployments({
      applications: [],
      engines: [],
      comparisons: [],
      observabilityBindings: [],
      history: [],
      driftReport: [],
      assistantPlans: []
    });

    expect(await screen.findByText("No deployment setup")).toBeInTheDocument();
    expect(screen.getByText("Create a workflow application, environment, and engine registration to start managing deployments.")).toBeInTheDocument();
    expect(screen.queryByText("Claims Operations")).not.toBeInTheDocument();
  });

  it("creates deployment setup from the empty state through live APIs", async () => {
    const fetchMock = renderDeployments({
      applications: [],
      engines: [],
      comparisons: [],
      observabilityBindings: [],
      history: [],
      driftReport: [],
      assistantPlans: []
    });

    await waitFor(() => expect(screen.getByLabelText("Tier")).toHaveValue("tier-production"));
    await userEvent.type(await screen.findByLabelText("Application"), "Claims Operations");
    await userEvent.clear(screen.getByLabelText("Environment"));
    await userEvent.type(screen.getByLabelText("Environment"), "Prod");
    await userEvent.type(screen.getByLabelText("Engine"), "claims-prod");
    await userEvent.type(screen.getByLabelText("Base URL"), "https://workflows.example.test/elsa");
    await userEvent.type(screen.getByLabelText("Credential reference"), "kv://claims/prod/elsa-api");
    await userEvent.click(screen.getByRole("button", { name: "Create setup" }));

    await waitFor(() => expect(screen.getAllByText("Claims Operations").length).toBeGreaterThan(0));
    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/applications`),
        expect.objectContaining({ method: "POST" })
      )
    );
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/applications/app-created/environments`),
      expect.objectContaining({ method: "POST" })
    );
    expect(requestBody(fetchMock, "POST", "/applications/app-created/environments")).toMatchObject({
      name: "Prod",
      tier: "Production",
      tierId: "tier-production"
    });
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/environments/env-created/engines`),
      expect.objectContaining({ method: "POST" })
    );
  });

  it("uses server-provided default tiers in empty setup", async () => {
    renderDeployments({
      applications: [],
      engines: [],
      comparisons: [],
      observabilityBindings: [],
      history: [],
      driftReport: [],
      assistantPlans: []
    });

    expect(await screen.findByText("No deployment setup")).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText("Tier")).toHaveValue("tier-production"));
    expect(screen.getByRole("option", { name: "Production" })).toBeInTheDocument();
    expect(screen.queryByText("Default tiers are used until workspace tiers finish loading.")).not.toBeInTheDocument();
  });

  it("renders a recoverable cockpit when setup exists without a registered engine", async () => {
    renderDeployments({
      ...deploymentCockpitFixture,
      engines: [],
      applications: [
        {
          ...deploymentCockpitFixture.applications[0],
          environments: deploymentCockpitFixture.applications[0].environments.map((environment) => ({ ...environment, engineIds: [] }))
        }
      ]
    });

    expect(await screen.findByRole("heading", { name: "Deployments" })).toBeInTheDocument();
    expect(screen.queryByText("Deployments could not load")).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Engine Registration" }));
    expect(screen.getByText("No engine registered")).toBeInTheDocument();
  });

  it("switches between multiple workflow applications from the application rail", async () => {
    renderDeployments(multipleApplicationsCockpit);

    expect(await screen.findByRole("heading", { name: "Deployments" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Claims Operations/i })).toHaveAttribute("aria-pressed", "true");

    await userEvent.click(screen.getByRole("button", { name: /Policy/i }));

    expect(screen.getByRole("button", { name: /Policy/i })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("heading", { name: "Policy" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: /Prod Production/i })).toBeInTheDocument();
  });

  it("does not offer legacy tier names before workspace tiers are available", async () => {
    const submit = vi.fn();

    render(
      <DeploymentSetupPanel
        canManageSetup
        tiers={[]}
        tiersLoading={false}
        isSubmitting={false}
        onSubmit={submit}
      />
    );

    expect(screen.getByLabelText("Tier")).toBeDisabled();
    expect(screen.queryByRole("option", { name: "Dev" })).not.toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "Production" })).not.toBeInTheDocument();
    expect(screen.getByText("No active deployment tiers are available for this workspace.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Create setup" })).toBeDisabled();
  });

  it("replaces a stale selected tier id when workspace tiers change", async () => {
    const submit = vi.fn();
    const firstProductionTier = tier("tier-production-old", "Production", 40, ["deployment.tier.production-like"]);
    const currentProductionTier = tier("tier-production-current", "Production", 40, ["deployment.tier.production-like"]);
    const { rerender } = render(
      <DeploymentSetupPanel
        canManageSetup
        tiers={[firstProductionTier]}
        isSubmitting={false}
        onSubmit={submit}
      />
    );

    await waitFor(() => expect(screen.getByLabelText("Tier")).toHaveValue("tier-production-old"));
    rerender(
      <DeploymentSetupPanel
        canManageSetup
        tiers={[currentProductionTier]}
        isSubmitting={false}
        onSubmit={submit}
      />
    );

    await waitFor(() => expect(screen.getByLabelText("Tier")).toHaveValue("tier-production-current"));
    await userEvent.type(screen.getByLabelText("Application"), "Policy");
    await userEvent.type(screen.getByLabelText("Engine"), "dev-01");
    await userEvent.type(screen.getByLabelText("Base URL"), "https://localhost:5001");
    await userEvent.type(screen.getByLabelText("Credential reference"), "foo");
    await userEvent.click(screen.getByRole("button", { name: "Create setup" }));

    expect(submit).toHaveBeenCalledWith(expect.objectContaining({ environmentTierId: "tier-production-current" }));
  });

  it("creates another deployment setup from a populated cockpit", async () => {
    const fetchMock = renderDeployments();

    await screen.findByRole("heading", { name: "Deployments" });
    await waitFor(() => expect(screen.getByRole("button", { name: "New application" })).toBeEnabled());
    await userEvent.click(screen.getByRole("button", { name: "New application" }));
    await userEvent.type(screen.getByLabelText("Application"), "Customer Care");
    await userEvent.clear(screen.getByLabelText("Environment"));
    await userEvent.type(screen.getByLabelText("Environment"), "Prod");
    await userEvent.type(screen.getByLabelText("Engine"), "care-prod");
    await userEvent.type(screen.getByLabelText("Base URL"), "https://care.example.test/elsa");
    await userEvent.type(screen.getByLabelText("Credential reference"), "kv://care/prod/elsa-api");
    await userEvent.click(screen.getByRole("button", { name: "Create setup" }));

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/applications`),
      expect.objectContaining({ method: "POST" })
    );
  });

  it("adds an environment and engine to the selected application", async () => {
    const fetchMock = renderDeployments(applicationWithoutSetupCockpit);

    expect(await screen.findByRole("heading", { name: "Deployments" })).toBeInTheDocument();
    expect(screen.getByText("No environments registered.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Add environment" }));
    expect(screen.getAllByText("Policy").length).toBeGreaterThan(0);
    await userEvent.clear(screen.getByLabelText("Environment"));
    await userEvent.type(screen.getByLabelText("Environment"), "Prod");
    await userEvent.type(screen.getByLabelText("Engine"), "policy-prod-weu-01");
    await userEvent.type(screen.getByLabelText("Base URL"), "https://policy-prod.example.test/elsa");
    await userEvent.type(screen.getByLabelText("Credential reference"), "kv://policy/prod/elsa-api");
    await userEvent.click(screen.getAllByRole("button", { name: "Add environment" }).at(-1)!);

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/applications/policy-app/environments`),
        expect.objectContaining({ method: "POST" })
      )
    );
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/environments/policy-prod/engines`),
      expect.objectContaining({ method: "POST" })
    );
    expect(requestBody(fetchMock, "POST", "/applications/policy-app/environments")).toMatchObject({
      name: "Prod",
      tier: "Production",
      tierId: "tier-production"
    });
  });

  it("sends custom tier identity when adding an environment", async () => {
    const fetchMock = renderDeployments(applicationWithoutSetupCockpit);

    await screen.findByRole("heading", { name: "Deployments" });
    await userEvent.click(screen.getByRole("button", { name: "Add environment" }));
    await userEvent.selectOptions(screen.getByLabelText("Tier"), "tier-uat");
    await userEvent.clear(screen.getByLabelText("Environment"));
    await userEvent.type(screen.getByLabelText("Environment"), "UAT");
    await userEvent.type(screen.getByLabelText("Engine"), "policy-uat-weu-01");
    await userEvent.type(screen.getByLabelText("Base URL"), "https://policy-uat.example.test/elsa");
    await userEvent.type(screen.getByLabelText("Credential reference"), "kv://policy/uat/elsa-api");
    await userEvent.click(screen.getAllByRole("button", { name: "Add environment" }).at(-1)!);

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/applications/policy-app/environments`),
        expect.objectContaining({ method: "POST" })
      )
    );
    expect(requestBody(fetchMock, "POST", "/applications/policy-app/environments")).toMatchObject({
      name: "UAT",
      tier: "Production",
      tierId: "tier-uat"
    });
  });

  it("edits application environment and engine metadata through live APIs", async () => {
    const fetchMock = renderDeployments();

    await screen.findByRole("heading", { name: "Deployments" });
    await waitFor(() => expect(screen.getByRole("button", { name: "Edit application" })).toBeEnabled());
    await userEvent.click(screen.getByRole("button", { name: "Edit application" }));
    await userEvent.clear(screen.getByLabelText("Application name"));
    await userEvent.type(screen.getByLabelText("Application name"), "Claims Platform");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    await userEvent.click(screen.getAllByRole("button", { name: "Edit" })[0]);
    await userEvent.clear(screen.getByLabelText("Environment"));
    await userEvent.type(screen.getByLabelText("Environment"), "Development");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    await userEvent.click(screen.getByRole("button", { name: "Engine Registration" }));
    await userEvent.click(screen.getByRole("button", { name: "Edit engine" }));
    await userEvent.clear(screen.getByLabelText("Base URL"));
    await userEvent.type(screen.getByLabelText("Base URL"), "https://dev-workflows-2.acme.example/elsa");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/applications/claims-ops`),
        expect.objectContaining({ method: "PUT" })
      )
    );
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/applications/claims-ops/environments/claims-dev`),
      expect.objectContaining({ method: "PUT" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/engines/dev-engine`),
      expect.objectContaining({ method: "PUT" })
    );
  });

  it("edits an environment endpoint and credential reference from the environment row", async () => {
    const fetchMock = renderDeployments();

    await screen.findByRole("heading", { name: "Deployments" });
    await userEvent.click(screen.getAllByRole("button", { name: "Edit" })[0]);
    await userEvent.clear(screen.getByLabelText("Base URL"));
    await userEvent.type(screen.getByLabelText("Base URL"), "https://dev-workflows-row-edit.acme.example/elsa");
    await userEvent.clear(screen.getByLabelText("Credential reference"));
    await userEvent.type(screen.getByLabelText("Credential reference"), "kv://acme-platform/dev/elsa-api-v2");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/engines/dev-engine`),
        expect.objectContaining({ method: "PUT" })
      )
    );
    expect(requestBody(fetchMock, "PUT", "/deployments/engines/dev-engine")).toMatchObject({
      baseUrl: "https://dev-workflows-row-edit.acme.example/elsa",
      credentialProvider: "Azure Key Vault",
      credentialReference: "kv://acme-platform/dev/elsa-api-v2"
    });
  });

  it("shows tier capabilities and sends tier identity when editing an environment", async () => {
    const fetchMock = renderDeployments({
      ...deploymentCockpitFixture,
      applications: [
        {
          ...deploymentCockpitFixture.applications[0],
          environments: deploymentCockpitFixture.applications[0].environments.map((environment) =>
            environment.id === "claims-prod"
              ? {
                  ...environment,
                  tierId: "tier-legacy-prod",
                  tierName: "Legacy Production",
                  tierStatus: "Archived",
                  tierCapabilities: ["deployment.tier.production-like"]
                }
              : environment
          )
        }
      ]
    });

    await screen.findByRole("heading", { name: "Deployments" });

    expect(screen.getByText("Legacy Production (archived)")).toBeInTheDocument();
    expect(screen.getAllByText("deployment.tier.production-like").length).toBeGreaterThan(0);
    await userEvent.click(screen.getAllByRole("button", { name: "Edit" })[3]);
    await userEvent.selectOptions(screen.getByLabelText("Tier"), "tier-uat");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/applications/claims-ops/environments/claims-prod`),
        expect.objectContaining({ method: "PUT" })
      )
    );
    expect(requestBody(fetchMock, "PUT", "/applications/claims-ops/environments/claims-prod")).toMatchObject({
      name: "Prod",
      tier: "Production",
      tierId: "tier-uat"
    });
  });

  it("verifies a selected workflow engine through the live API", async () => {
    const fetchMock = renderDeployments();

    await screen.findByRole("heading", { name: "Deployments" });
    await userEvent.click(screen.getByRole("button", { name: "Engine Registration" }));
    await userEvent.click(screen.getByRole("button", { name: "Verify" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/engines/dev-engine/verify`),
        expect.objectContaining({ method: "POST" })
      )
    );
    expect(await screen.findByRole("status")).toHaveTextContent("Endpoint responded successfully.");
  });

  it("shows only capability-supported engine controls and records selected operations", async () => {
    renderDeployments();

    await screen.findByRole("heading", { name: "Deployments" });
    await userEvent.click(screen.getByRole("button", { name: "Engine Registration" }));
    await userEvent.selectOptions(screen.getByLabelText("Environment"), "claims-stage");

    expect(screen.getAllByText("claims-stage-weu-01").length).toBeGreaterThan(0);
    expect(screen.getByText("kv://acme-platform/stage/elsa-api")).toBeInTheDocument();
    expect(screen.getByText("Pause Processing")).toBeInTheDocument();
    expect(screen.getAllByText("Reload Configuration").length).toBeGreaterThan(0);
    expect(screen.queryByText("Restart Shell")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Restart$/i })).not.toBeInTheDocument();

    await userEvent.click(screen.getAllByRole("button", { name: "Run" })[1]);

    expect(await screen.findByRole("status")).toHaveTextContent("Reload Configuration executed for claims-stage-weu-01.");
  });

  it("blocks deployment when promotion validation finds missing secrets and incompatible capabilities", async () => {
    renderDeployments();

    await screen.findByRole("heading", { name: "Deployments" });
    await userEvent.click(screen.getByRole("button", { name: "Promotion Diff" }));

    expect(screen.getAllByText("Payment Retry").length).toBeGreaterThan(0);
    expect(screen.getByText("Artifact references")).toBeInTheDocument();
    expect(screen.getByText("sha256:payment-retry-stage")).toBeInTheDocument();
    expect(screen.getAllByText("elsa.workflow-definition").length).toBeGreaterThan(0);
    expect(screen.getByText("displayName=Payment Retry, version=7")).toBeInTheDocument();
    expect(screen.getAllByText("Secret references").length).toBeGreaterThan(0);
    expect(screen.getByText("Payment API secret reference is missing or not verified in Prod.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Deploy Revision" })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Roll Back to r39/i })).toBeEnabled();
  });

  it("uses tier capability ids for rollback availability", async () => {
    renderDeployments({
      ...deploymentCockpitFixture,
      applications: [
        {
          ...deploymentCockpitFixture.applications[0],
          environments: deploymentCockpitFixture.applications[0].environments.map((environment) =>
            environment.id === "claims-prod"
              ? {
                  ...environment,
                  tierName: "Customer Live",
                  tierCapabilities: ["deployment.tier.production-like", "deployment.promotion.target", "deployment.confirmation.required"]
                }
              : environment
          )
        }
      ]
    });

    await screen.findByRole("heading", { name: "Deployments" });
    await userEvent.click(screen.getByRole("button", { name: "Promotion Diff" }));

    expect(screen.getByText("Rollback is not enabled for the target tier.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Roll Back to r39/i })).toBeDisabled();
  });

  it("enables deployment for a comparison with passing validations", async () => {
    renderDeployments();

    await screen.findByRole("heading", { name: "Deployments" });
    await userEvent.click(screen.getByRole("button", { name: "Promotion Diff" }));
    await userEvent.selectOptions(screen.getByLabelText("Source revision"), "claims-dev");
    await userEvent.selectOptions(screen.getByLabelText("Target revision"), "claims-test");

    expect(screen.getByText("Required secret references are verified for Test.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Deploy Revision" })).toBeEnabled();
  });

  it("refreshes live preview and queues deployment and rollback with confirmations", async () => {
    const fetchMock = renderDeployments();

    await screen.findByRole("heading", { name: "Deployments" });
    await userEvent.click(screen.getByRole("button", { name: "Promotion Diff" }));
    await userEvent.selectOptions(screen.getByLabelText("Source revision"), "claims-dev");
    await userEvent.selectOptions(screen.getByLabelText("Target revision"), "claims-test");
    await userEvent.click(screen.getByRole("button", { name: "Refresh Preview" }));

    expect(await screen.findByText("Live validation passed for Test.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Deploy Revision" }));
    expect(await screen.findByRole("status")).toHaveTextContent("Deployment run queued");

    await userEvent.selectOptions(screen.getByLabelText("Source revision"), "claims-stage");
    await userEvent.selectOptions(screen.getByLabelText("Target revision"), "claims-prod");
    await userEvent.click(screen.getByRole("button", { name: /Roll Back to r39/i }));
    expect(await screen.findByRole("status")).toHaveTextContent("Rollback run queued");

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/promotions/preview`),
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/runs`),
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/rollbacks`),
      expect.objectContaining({ method: "POST" })
    );
  }, 10000);

  it("shows an empty promotion preview state when the cockpit has no comparison", async () => {
    renderDeployments({ ...deploymentCockpitFixture, comparisons: [] });

    await screen.findByRole("heading", { name: "Deployments" });
    await userEvent.click(screen.getByRole("button", { name: "Promotion Diff" }));

    expect(screen.getByText("No comparison available")).toBeInTheDocument();
    expect(screen.getByText("Choose a supported source and target environment pair.")).toBeInTheDocument();
    expect(screen.getByText("No comparison")).toBeInTheDocument();
  });

  it("keeps assistant plans immutable and distinguishes proposed from executed actions", async () => {
    renderDeployments();

    await screen.findByRole("heading", { name: "Deployments" });
    await userEvent.click(screen.getByRole("button", { name: "Assistant Review" }));

    expect(screen.getByText("Immutable plan plan-20260522-001 v3")).toBeInTheDocument();
    expect(screen.getByText("Proposed actions")).toBeInTheDocument();
    expect(screen.getByText("Executed actions")).toBeInTheDocument();
    expect(screen.getByText("No platform mutations executed.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve Plan" })).toBeDisabled();

    await userEvent.click(screen.getByRole("button", { name: "Reject Plan" }));
    expect(screen.getByRole("status")).toHaveTextContent("Plan marked Rejected");
    expect(screen.getByText("No platform mutations executed.")).toBeInTheDocument();
  });

  it("shows an empty assistant review state when no plan is available", async () => {
    renderDeployments({ ...deploymentCockpitFixture, assistantPlans: [] });

    await screen.findByRole("heading", { name: "Deployments" });
    await userEvent.click(screen.getByRole("button", { name: "Assistant Review" }));

    expect(screen.getByText("No assistant plan available")).toBeInTheDocument();
    expect(screen.getByText("Assistant review will appear after a deployment plan is generated for this workspace.")).toBeInTheDocument();
  });

  it("shows deployment run history and confirmation actions", async () => {
    renderDeployments();

    await screen.findByRole("heading", { name: "Deployments" });
    await userEvent.click(screen.getByRole("button", { name: "Observability" }));

    expect(screen.getByText("Run history")).toBeInTheDocument();
    expect(screen.getByText("Mira Chen")).toBeInTheDocument();
    expect(screen.getByText("Latest Blocked")).toBeInTheDocument();
    expect(screen.getByText("Deploy Running")).toBeInTheDocument();
    expect(screen.getByText("Applying workflow definitions")).toBeInTheDocument();
    expect(screen.getByText("sha256:payment-retry-stage / elsa.workflow-definition / sha256:stage-digest")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Confirm Deployment" })).not.toBeInTheDocument();
  });
});

function renderDeployments(cockpit: DeploymentCockpit = deploymentCockpitFixture) {
  const fetchMock = createDeploymentFetchMock(cockpit);
  vi.stubGlobal("fetch", fetchMock);
  render(
    <TestQueryProvider>
      <MemoryRouter>
        <AuthProvider>
          <WorkspaceContextProvider>
            <DeploymentsPage />
          </WorkspaceContextProvider>
        </AuthProvider>
      </MemoryRouter>
    </TestQueryProvider>
  );
  return fetchMock;
}

function createDeploymentFetchMock(cockpit: DeploymentCockpit) {
  let currentCockpit = cockpit;
  return vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = input instanceof Request ? input.url : input.toString();
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    if (url.endsWith("/api/auth/session"))
      return jsonResponse({ loginEnabled: true, authenticated: true, displayName: "Test User", email: "test@example.com", loginPath: "/api/auth/login", logoutPath: "/api/auth/logout" });
    if (url.endsWith("/api/me/organizations"))
      return jsonResponse(workspaceContextFixture());
    if (url.endsWith(`/api/workspaces/${workspaceId}/deployments/permissions`)) {
      return jsonResponse({
        permissions: [
          "deployments.read",
          "deployments.setup.manage",
          "deployments.promotion.preview",
          "deployments.run.execute",
          "deployments.rollback.execute",
          "deployments.controls.execute"
        ]
      });
    }
    if (url.endsWith(`/api/workspaces/${workspaceId}/deployments/tier-capabilities`)) {
      return jsonResponse({ capabilities: tierCapabilities });
    }
    if (url.endsWith(`/api/workspaces/${workspaceId}/deployments/tiers`)) {
      return jsonResponse({ tiers: deploymentTiers });
    }
    if (url.endsWith(`/api/workspaces/${workspaceId}/deployments/cockpit`)) {
      return jsonResponse(currentCockpit);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/applications`)) {
      return jsonResponse({ id: "app-created", workspaceId, name: "Claims Operations" }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/applications/app-created/environments`)) {
      return jsonResponse({ id: "env-created", workspaceId, applicationId: "app-created", name: "Prod" }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/environments/env-created/engines`)) {
      currentCockpit = {
        ...deploymentCockpitFixture,
        applications: [{ ...deploymentCockpitFixture.applications[0], id: "app-created", name: "Claims Operations" }]
      };
      return jsonResponse({ id: "engine-created", name: "claims-prod", environmentId: "env-created" }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/applications/policy-app/environments`)) {
      return jsonResponse({ id: "policy-prod", workspaceId, applicationId: "policy-app", name: "Prod" }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/environments/policy-prod/engines`)) {
      currentCockpit = {
        ...currentCockpit,
        applications: currentCockpit.applications.map((application) =>
          application.id === "policy-app"
            ? {
                ...application,
                environments: [
                  ...application.environments,
                  {
                    id: "policy-prod",
                    name: "Prod",
                    tier: "Production",
                    tierId: "tier-production",
                    tierName: "Production",
                    tierStatus: "Active",
                    tierCapabilities: [
                      "deployment.tier.production-like",
                      "deployment.promotion.target",
                      "deployment.confirmation.required",
                      "deployment.rollback.enabled",
                      "deployment.secret-verification.required",
                      "deployment.observability.required"
                    ],
                    health: "Healthy",
                    desiredRevision: { id: "00000000-0000-0000-0000-000000000250", revision: 1, commit: "initial", label: "Initial desired state", authoredAt: "2026-05-26T10:00:00Z" },
                    deployedRevision: null,
                    deploymentStatus: "Succeeded",
                    driftStatus: "Unknown",
                    engineIds: ["policy-prod-engine"]
                  }
                ]
              }
            : application
        ),
        engines: [
          ...currentCockpit.engines,
          engine("policy-prod-engine", "policy-prod-weu-01", "policy-prod", "Healthy", "Verified", [
            capability("engine.reload-configuration", "Reload engine configuration", "EngineApi")
          ])
        ]
      };
      return jsonResponse({ id: "policy-prod-engine", name: "policy-prod-weu-01", environmentId: "policy-prod" }, 201);
    }
    if (method === "PUT" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/applications/claims-ops`)) {
      currentCockpit = {
        ...currentCockpit,
        applications: currentCockpit.applications.map((application) =>
          application.id === "claims-ops" ? { ...application, name: "Claims Platform" } : application
        )
      };
      return jsonResponse({ id: "claims-ops", workspaceId, name: "Claims Platform" });
    }
    if (method === "PUT" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/applications/claims-ops/environments/claims-dev`)) {
      currentCockpit = {
        ...currentCockpit,
        applications: currentCockpit.applications.map((application) => ({
          ...application,
          environments: application.environments.map((environment) =>
            environment.id === "claims-dev" ? { ...environment, name: "Development" } : environment
          )
        }))
      };
      return jsonResponse({ id: "claims-dev", workspaceId, applicationId: "claims-ops", name: "Development" });
    }
    if (method === "PUT" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/engines/dev-engine`)) {
      currentCockpit = {
        ...currentCockpit,
        engines: currentCockpit.engines.map((item) =>
          item.id === "dev-engine"
            ? { ...item, endpoint: { ...item.endpoint, baseUrl: "https://dev-workflows-2.acme.example/elsa" } }
            : item
        )
      };
      return jsonResponse({ id: "dev-engine", name: "claims-dev-weu-01", environmentId: "claims-dev" });
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/engines/dev-engine/verify`)) {
      currentCockpit = {
        ...currentCockpit,
        engines: currentCockpit.engines.map((item) =>
          item.id === "dev-engine"
            ? {
                ...item,
                health: "Healthy",
                lastHeartbeatAt: "2026-05-26T10:05:00Z",
                lastVerificationAt: "2026-05-26T10:05:00Z",
                verificationMessage: "Endpoint responded successfully.",
                credentialReference: {
                  ...item.credentialReference,
                  verificationStatus: "Verified",
                  lastVerifiedAt: "2026-05-26T10:05:00Z"
                }
              }
            : item
        )
      };
      return jsonResponse({
        engineId: "dev-engine",
        environmentId: "claims-dev",
        health: "Healthy",
        version: "Elsa 4.0.1",
        certificateStatus: "Trusted",
        credentialVerificationStatus: "Verified",
        credentialLastVerifiedAt: "2026-05-26T10:05:00Z",
        lastHeartbeatAt: "2026-05-26T10:05:00Z",
        lastVerificationAt: "2026-05-26T10:05:00Z",
        message: "Endpoint responded successfully."
      });
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/confirmations`)) {
      const body = JSON.parse(init?.body?.toString() ?? "{}") as { actionType?: string; targetId?: string };
      return jsonResponse({ id: `${body.actionType ?? "action"}-confirmation-1`, workspaceId, actionType: body.actionType, targetId: body.targetId }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/promotions/preview`)) {
      return jsonResponse({
        ...deploymentCockpitFixture.comparisons[1],
        validations: [
          { id: "secret-payment-test", severity: "Pass", scope: "Secret references", message: "Live validation passed for Test." },
          { id: "capability-test", severity: "Pass", scope: "Engine capabilities", message: "claims-test-weu-01 supports required engine operations." }
        ]
      });
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/runs`)) {
      currentCockpit = {
        ...currentCockpit,
        history: [
          {
            id: "00000000-0000-0000-0000-000000000777",
            status: "Queued",
            revision: 42,
            actor: "account-1",
            environmentId: "claims-test",
            engineId: "test-engine",
            validationOutcome: "Passed",
            occurredAt: "2026-05-26T10:01:00Z",
            rollbackSourceRevision: null
          },
          ...currentCockpit.history
        ]
      };
      return jsonResponse({
        id: "00000000-0000-0000-0000-000000000777",
        workspaceId,
        applicationId: "claims-ops",
        environmentId: "claims-test",
        engineId: "test-engine",
        sourceRevisionId: "00000000-0000-0000-0000-000000000142",
        previousDeployedRevisionId: null,
        rollbackSourceRunId: null,
        status: "Queued",
        validationOutcome: "Passed",
        confirmationId: "Deploy-confirmation-1",
        actorAccountId: "account-1",
        queuedAt: "2026-05-26T10:01:00Z",
        startedAt: null,
        completedAt: null,
        createdAt: "2026-05-26T10:01:00Z",
        workerId: null,
        workerHeartbeatAt: null,
        attemptNumber: 1,
        recoveryReason: null,
        failureMessage: null
      }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/rollbacks`)) {
      currentCockpit = {
        ...currentCockpit,
        history: [
          {
            id: "00000000-0000-0000-0000-000000000778",
            status: "Queued",
            revision: 39,
            actor: "account-1",
            environmentId: "claims-prod",
            engineId: "prod-engine",
            validationOutcome: "Warnings",
            occurredAt: "2026-05-26T10:02:00Z",
            rollbackSourceRevision: 41
          },
          ...currentCockpit.history
        ]
      };
      return jsonResponse({
        id: "00000000-0000-0000-0000-000000000778",
        workspaceId,
        applicationId: "claims-ops",
        environmentId: "claims-prod",
        engineId: "prod-engine",
        sourceRevisionId: "00000000-0000-0000-0000-000000000139",
        previousDeployedRevisionId: "00000000-0000-0000-0000-000000000141",
        rollbackSourceRunId: "00000000-0000-0000-0000-000000000410",
        status: "Queued",
        validationOutcome: "Warnings",
        confirmationId: "Rollback-confirmation-1",
        actorAccountId: "account-1",
        queuedAt: "2026-05-26T10:02:00Z",
        startedAt: null,
        completedAt: null,
        createdAt: "2026-05-26T10:02:00Z",
        workerId: null,
        workerHeartbeatAt: null,
        attemptNumber: 1,
        recoveryReason: null,
        failureMessage: null
      }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/engines/stage-engine/controls/reload-configuration/run`)) {
      return jsonResponse({
        id: "control-execution-1",
        workspaceId,
        engineId: "stage-engine",
        environmentId: "claims-stage",
        controlId: "reload-configuration",
        controlLabel: "Reload Configuration",
        boundary: "EngineApi",
        requiredCapabilityId: "engine.reload-configuration",
        confirmationId: "confirmation-1",
        actorAccountId: "account-1",
        status: "Succeeded",
        createdAt: "2026-05-26T10:00:00Z",
        message: "Reload Configuration executed for claims-stage-weu-01."
      });
    }
    return jsonResponse({ title: "Not found" }, 404);
  });
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function requestBody(fetchMock: ReturnType<typeof createDeploymentFetchMock>, method: string, urlSuffix: string) {
  const call = fetchMock.mock.calls.find(([input, init]) => {
    const url = input instanceof Request ? input.url : input.toString();
    return url.includes(urlSuffix) && init?.method === method;
  });
  expect(call).toBeDefined();
  return JSON.parse(call![1]?.body?.toString() ?? "{}") as Record<string, unknown>;
}

function workspaceContextFixture() {
  return {
    account: { id: "account-1", displayName: "Test User", email: "test@example.com" },
    organizations: [{ id: organizationId, name: "Acme Corp", role: "Owner" }],
    workspaces: [
      { id: workspaceId, name: "Acme Insurance", kind: "Shared", role: "Owner", organizationId, organizationName: "Acme Corp", organizationRole: "Owner" },
      { id: "00000000-0000-0000-0000-000000000011", name: "Acme Labs", kind: "Shared", role: "Reader", organizationId, organizationName: "Acme Corp", organizationRole: "Owner" }
    ]
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

const organizationId = "00000000-0000-0000-0000-000000000001";
const workspaceId = "00000000-0000-0000-0000-000000000010";
const tierCapabilities = [
  capabilityDefinition("deployment.tier.development-like", "Development-like", "Classification"),
  capabilityDefinition("deployment.tier.test-like", "Test-like", "Classification"),
  capabilityDefinition("deployment.tier.preproduction-like", "Pre-production-like", "Classification"),
  capabilityDefinition("deployment.tier.production-like", "Production-like", "Classification"),
  capabilityDefinition("deployment.promotion.source", "Promotion source", "Promotion"),
  capabilityDefinition("deployment.promotion.target", "Promotion target", "Promotion"),
  capabilityDefinition("deployment.confirmation.required", "Confirmation required", "Safeguards"),
  capabilityDefinition("deployment.rollback.enabled", "Rollback enabled", "Rollback"),
  capabilityDefinition("deployment.secret-verification.required", "Secret verification required", "Validation"),
  capabilityDefinition("deployment.observability.required", "Observability required", "Observability")
];
const deploymentTiers = [
  tier("tier-dev", "Dev", 10, ["deployment.tier.development-like", "deployment.promotion.source"]),
  tier("tier-test", "Test", 20, ["deployment.tier.test-like", "deployment.promotion.source", "deployment.promotion.target"]),
  tier("tier-stage", "Stage", 30, ["deployment.tier.preproduction-like", "deployment.promotion.source", "deployment.promotion.target", "deployment.secret-verification.required"]),
  tier("tier-production", "Production", 40, [
    "deployment.tier.production-like",
    "deployment.promotion.target",
    "deployment.confirmation.required",
    "deployment.rollback.enabled",
    "deployment.secret-verification.required",
    "deployment.observability.required"
  ]),
  tier("tier-uat", "UAT", 35, ["deployment.tier.preproduction-like", "deployment.promotion.target"]),
  tier("tier-legacy-prod", "Legacy Production", 50, ["deployment.tier.production-like"], "Archived")
];

const deploymentCockpitFixture: DeploymentCockpit = {
  applications: [
    {
      id: "claims-ops",
      name: "Claims Operations",
      workspaceName: "Acme Insurance",
      environments: [
        {
          id: "claims-dev",
          name: "Dev",
          tier: "Dev",
          tierId: "tier-dev",
          tierName: "Dev",
          tierStatus: "Active",
          tierCapabilities: ["deployment.tier.development-like", "deployment.promotion.source"],
          health: "Healthy",
          desiredRevision: { id: "00000000-0000-0000-0000-000000000142", revision: 42, commit: "8f6a9c1", label: "Payment retry workflow", authoredAt: "2026-05-21T08:30:00Z" },
          deployedRevision: 42,
          deploymentStatus: "Succeeded",
          driftStatus: "InSync",
          engineIds: ["dev-engine"]
        },
        {
          id: "claims-test",
          name: "Test",
          tier: "Test",
          tierId: "tier-test",
          tierName: "Test",
          tierStatus: "Active",
          tierCapabilities: ["deployment.tier.test-like", "deployment.promotion.source", "deployment.promotion.target"],
          health: "Healthy",
          desiredRevision: { id: "00000000-0000-0000-0000-000000000139", revision: 39, commit: "79d1b07", label: "Fraud review tuning", authoredAt: "2026-05-20T13:20:00Z" },
          deployedRevision: 39,
          deploymentStatus: "Succeeded",
          driftStatus: "InSync",
          engineIds: ["test-engine"]
        },
        {
          id: "claims-stage",
          name: "Stage",
          tier: "Stage",
          tierId: "tier-stage",
          tierName: "Stage",
          tierStatus: "Active",
          tierCapabilities: ["deployment.tier.preproduction-like", "deployment.promotion.source", "deployment.promotion.target", "deployment.secret-verification.required"],
          health: "Degraded",
          desiredRevision: { id: "00000000-0000-0000-0000-000000000141", revision: 41, commit: "c174f2a", label: "Policy document sync", authoredAt: "2026-05-21T06:10:00Z" },
          deployedRevision: 40,
          deploymentStatus: "Running",
          driftStatus: "DriftDetected",
          engineIds: ["stage-engine"]
        },
        {
          id: "claims-prod",
          name: "Prod",
          tier: "Production",
          tierId: "tier-production",
          tierName: "Production",
          tierStatus: "Active",
          tierCapabilities: [
            "deployment.tier.production-like",
            "deployment.promotion.target",
            "deployment.confirmation.required",
            "deployment.rollback.enabled",
            "deployment.secret-verification.required",
            "deployment.observability.required"
          ],
          health: "Unreachable",
          desiredRevision: { id: "00000000-0000-0000-0000-000000000140", revision: 40, commit: "11ec9d4", label: "Baseline production", authoredAt: "2026-05-19T15:45:00Z" },
          deployedRevision: 40,
          deploymentStatus: "Blocked",
          driftStatus: "Unknown",
          engineIds: ["prod-engine"]
        }
      ]
    }
  ],
  engines: [
    engine("dev-engine", "claims-dev-weu-01", "claims-dev", "Healthy", "Verified", [
      capability("workflow.pause-processing", "Pause workflow processing", "Workflow"),
      capability("engine.reload-configuration", "Reload engine configuration", "EngineApi")
    ]),
    engine("test-engine", "claims-test-weu-01", "claims-test", "Healthy", "Verified", [
      capability("workflow.pause-processing", "Pause workflow processing", "Workflow"),
      capability("engine.reload-configuration", "Reload engine configuration", "EngineApi")
    ]),
    engine("stage-engine", "claims-stage-weu-01", "claims-stage", "Degraded", "Verified", [
      capability("workflow.pause-processing", "Pause workflow processing", "Workflow"),
      capability("engine.reload-configuration", "Reload engine configuration", "EngineApi")
    ]),
    engine("prod-engine", "claims-prod-weu-01", "claims-prod", "Unreachable", "Missing", [
      capability("workflow.pause-processing", "Pause workflow processing", "Workflow")
    ])
  ],
  comparisons: [
    {
      sourceEnvironmentId: "claims-stage",
      targetEnvironmentId: "claims-prod",
      sourceRevisionId: "00000000-0000-0000-0000-000000000141",
      sourceRevision: 41,
      targetRevision: 40,
      rollbackRevisionId: "00000000-0000-0000-0000-000000000139",
      rollbackRevision: 39,
      diff: [
        { id: "workflow-payment-retry", category: "Workflows", name: "Payment Retry", sourceValue: "v7 with idempotent retry", targetValue: "v6", impact: "Changed" },
        { id: "secret-payment-api", category: "SecretReferences", name: "Payment API", sourceValue: "kv://acme-platform/prod/payment-api:v3", targetValue: "Missing reference", impact: "Changed" }
      ],
      validations: [
        { id: "secret-payment-api", severity: "Blocker", scope: "Secret references", message: "Payment API secret reference is missing or not verified in Prod." },
        { id: "capability-reload", severity: "Blocker", scope: "Engine capabilities", message: "claims-prod-weu-01 does not advertise engine.reload-configuration." }
      ],
      artifacts: [
        artifactComparison("Payment Retry", "sha256:payment-retry-stage", "stage-digest", "Changed", {
          displayName: "Payment Retry",
          version: "7"
        })
      ]
    },
    {
      sourceEnvironmentId: "claims-dev",
      targetEnvironmentId: "claims-test",
      sourceRevisionId: "00000000-0000-0000-0000-000000000142",
      sourceRevision: 42,
      targetRevision: 39,
      rollbackRevisionId: "00000000-0000-0000-0000-000000000138",
      rollbackRevision: 38,
      diff: [
        { id: "workflow-payment-retry-test", category: "Workflows", name: "Payment Retry", sourceValue: "v8", targetValue: "v6", impact: "Changed" },
        { id: "secret-payment-test", category: "SecretReferences", name: "Payment API", sourceValue: "kv://acme-platform/test/payment-api:v3", targetValue: "kv://acme-platform/test/payment-api:v2", impact: "Changed" }
      ],
      validations: [
        { id: "secret-payment-test", severity: "Pass", scope: "Secret references", message: "Required secret references are verified for Test." },
        { id: "capability-test", severity: "Pass", scope: "Engine capabilities", message: "claims-test-weu-01 supports required engine operations." }
      ],
      artifacts: [
        artifactComparison("Payment Retry", "sha256:payment-retry-dev", "dev-digest", "Changed", {
          displayName: "Payment Retry",
          version: "8"
        })
      ]
    }
  ],
  observabilityBindings: [
    { id: "prod-logs", kind: "Logs", provider: "Azure Monitor", status: "Connected", scope: "claims-prod / rev 40", correlatedRevision: 40, sample: "143 structured events in the last 30 minutes" }
  ],
  history: [
    {
      id: "00000000-0000-0000-0000-000000000410",
      status: "Blocked",
      revision: 41,
      actor: "Mira Chen",
      environmentId: "claims-prod",
      engineId: "prod-engine",
      validationOutcome: "Blocked",
      occurredAt: "2026-05-22T08:05:00Z",
      rollbackSourceRevision: null,
      commands: [
        {
          id: "command-1",
          workspaceId,
          runId: "00000000-0000-0000-0000-000000000410",
          environmentId: "claims-prod",
          engineId: "prod-engine",
          action: "Deploy",
          status: "Running",
          artifact: commandArtifact("sha256:payment-retry-stage", "stage-digest"),
          workerId: "runtime-worker-1",
          claimedAt: "2026-05-22T08:06:00Z",
          leaseExpiresAt: "2026-05-22T08:11:00Z",
          heartbeatAt: "2026-05-22T08:07:00Z",
          attemptNumber: 1,
          percentComplete: 75,
          progressMessage: "Applying workflow definitions",
          observedArtifactDigest: null,
          runtimeReference: "elsa://workflows/payment-retry",
          diagnostics: [],
          createdAt: "2026-05-22T08:05:00Z",
          updatedAt: "2026-05-22T08:07:00Z",
          completedAt: null
        }
      ]
    }
  ],
  driftReport: [
    { id: "drift-shell", environmentId: "claims-stage", engineId: "stage-engine", area: "Shell concurrency", desired: "16 workers", observed: "12 workers", action: "Review" }
  ],
  assistantPlans: [
    {
      id: "plan-20260522-001",
      version: 3,
      status: "Proposed",
      workspaceName: "Acme Insurance",
      targetEnvironmentId: "claims-prod",
      targetEngineId: "prod-engine",
      summary: "Promote revision 41 from Stage to Prod after validating secrets, reachability, and engine reload capability.",
      proposedActions: [
        "Verify Prod secret references and provider access",
        "Run desired-state diff from Stage revision 41 to Prod revision 40",
        "Apply revision 41 to claims-prod-weu-01 as one deployment",
        "Keep rollback path to revision 39 available"
      ],
      executedActions: [],
      validations: [
        { id: "assistant-scope", severity: "Pass", scope: "Workspace authorization", message: "Plan is scoped to Acme Insurance only." },
        { id: "assistant-secret", severity: "Blocker", scope: "Secret references", message: "Payment API reference must verify before approval can execute." }
      ],
      rollbackPath: "Redeploy revision 39 to claims-prod-weu-01 if revision 41 fails after validation clears.",
      allOrNothing: true,
      createdAt: "2026-05-22T08:02:00Z"
    }
  ]
};

const applicationWithoutSetupCockpit: DeploymentCockpit = {
  applications: [
    {
      id: "policy-app",
      name: "Policy",
      workspaceName: "Acme Insurance",
      environments: []
    }
  ],
  engines: [],
  comparisons: [],
  observabilityBindings: [],
  history: [],
  driftReport: [],
  assistantPlans: []
};

const multipleApplicationsCockpit: DeploymentCockpit = {
  ...deploymentCockpitFixture,
  applications: [
    ...deploymentCockpitFixture.applications,
    {
      id: "policy-app",
      name: "Policy",
      workspaceName: "Acme Insurance",
      environments: [
        {
          id: "policy-prod",
          name: "Prod",
          tier: "Production",
          tierId: "tier-production",
          tierName: "Production",
          tierStatus: "Active",
          tierCapabilities: ["deployment.tier.production-like", "deployment.promotion.target"],
          health: "Healthy",
          desiredRevision: { id: "00000000-0000-0000-0000-000000000250", revision: 1, commit: "initial", label: "Initial desired state", authoredAt: "2026-05-26T10:00:00Z" },
          deployedRevision: 1,
          deploymentStatus: "Succeeded",
          driftStatus: "InSync",
          engineIds: ["policy-prod-engine"]
        }
      ]
    }
  ],
  engines: [
    ...deploymentCockpitFixture.engines,
    engine("policy-prod-engine", "policy-prod-weu-01", "policy-prod", "Healthy", "Verified", [
      capability("engine.reload-configuration", "Reload engine configuration", "EngineApi")
    ])
  ]
};

function engine(
  id: string,
  name: string,
  environmentId: string,
  health: DeploymentCockpit["engines"][number]["health"],
  verificationStatus: DeploymentCockpit["engines"][number]["credentialReference"]["verificationStatus"],
  capabilities: DeploymentCockpit["engines"][number]["capabilities"]
): DeploymentCockpit["engines"][number] {
  return {
    id,
    name,
    environmentId,
    endpoint: {
      baseUrl: `https://${name}.example/elsa`,
      region: "West Europe",
      version: "Elsa 4.0.1",
      certificateStatus: "Trusted"
    },
    credentialReference: {
      provider: "Azure Key Vault",
      reference: `kv://acme-platform/${environmentId.replace("claims-", "")}/elsa-api`,
      verificationStatus,
      lastVerifiedAt: verificationStatus === "Verified" ? "2026-05-22T07:50:00Z" : null
    },
    health,
    lastHeartbeatAt: health === "Unreachable" ? null : "2026-05-22T08:16:30Z",
    lastVerificationAt: health === "Unreachable" ? null : "2026-05-22T08:12:30Z",
    verificationMessage: health === "Unreachable" ? "Engine has not been verified." : "Endpoint responded successfully.",
    capabilities,
    controls: [
      { id: "pause-processing", label: "Pause Processing", boundary: "Workflow", capabilityId: "workflow.pause-processing", description: "Stops new workflow dispatch without touching host infrastructure." },
      { id: "reload-configuration", label: "Reload Configuration", boundary: "EngineApi", capabilityId: "engine.reload-configuration", description: "Reloads engine API configuration from desired state." },
      { id: "restart-shell", label: "Restart Shell", boundary: "Shell", capabilityId: "shell.restart", description: "Hidden until the shell restart capability is advertised." }
    ],
    hostingProvider: null
  };
}

function artifactComparison(
  name: string,
  artifactId: string,
  digest: string,
  impact: DeploymentCockpit["comparisons"][number]["artifacts"][number]["impact"],
  metadata: Record<string, string>
): DeploymentCockpit["comparisons"][number]["artifacts"][number] {
  return {
    name,
    source: {
      artifactRecordId: "00000000-0000-0000-0000-000000000900",
      artifactId,
      artifactTypeId: "elsa.workflow-definition",
      contentDigest: { algorithm: "sha256", value: digest },
      metadata,
      configuration: { environment: "stage" },
      compatibilityHints: ["workflow-definition.apply"]
    },
    target: {
      artifactRecordId: "00000000-0000-0000-0000-000000000899",
      artifactId: "sha256:payment-retry-prod",
      artifactTypeId: "elsa.workflow-definition",
      contentDigest: { algorithm: "sha256", value: "prod-digest" },
      metadata: { displayName: "Payment Retry", version: "6" },
      configuration: { environment: "prod" },
      compatibilityHints: ["workflow-definition.apply"]
    },
    impact,
    runtimeCompatibility: [{ id: `${artifactId}-runtime`, severity: "Pass", scope: "Runtime compatibility", message: "Workflow runtime capability is present." }]
  };
}

function commandArtifact(artifactId: string, digest: string): NonNullable<DeploymentCockpit["history"][number]["commands"]>[number]["artifact"] {
  return {
    artifactRecordId: "00000000-0000-0000-0000-000000000900",
    artifactId,
    artifactTypeId: "elsa.workflow-definition",
    contentDigest: { algorithm: "sha256", value: digest }
  };
}

function capability(
  id: string,
  label: string,
  boundary: DeploymentCockpit["engines"][number]["capabilities"][number]["boundary"]
) {
  return { id, label, boundary };
}

function capabilityDefinition(id: string, label: string, category: string) {
  return {
    id,
    label,
    description: label,
    category,
    isDeprecated: false
  };
}

function tier(id: string, name: string, sortOrder: number, capabilities: string[], status: WorkspaceDeploymentTier["status"] = "Active"): WorkspaceDeploymentTier {
  return {
    id,
    workspaceId,
    name,
    description: null,
    sortOrder,
    isDefault: name === "Dev" || name === "Test" || name === "Stage" || name === "Production",
    status,
    capabilities,
    environmentCount: 0,
    createdAt: "2026-05-28T10:00:00Z",
    updatedAt: "2026-05-28T10:00:00Z",
    createdByAccountId: null,
    updatedByAccountId: null,
    archivedAt: status === "Archived" ? "2026-05-28T10:00:00Z" : null,
    archivedByAccountId: null
  };
}
