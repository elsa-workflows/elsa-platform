import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DeploymentsPage } from "@/features/deployments/DeploymentsPage";
import type { DeploymentCockpit } from "@/features/deployments/deploymentModels";

describe("DeploymentsPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders workflow applications and environment health without exposing credential values", async () => {
    renderDeployments();

    expect(await screen.findByRole("heading", { name: "Deployments" })).toBeInTheDocument();
    expect(screen.getByText("Claims Operations")).toBeInTheDocument();
    expect(screen.getByText("Workspace tenant boundary")).toBeInTheDocument();
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

    await userEvent.type(await screen.findByLabelText("Application"), "Claims Operations");
    await userEvent.clear(screen.getByLabelText("Environment"));
    await userEvent.type(screen.getByLabelText("Environment"), "Prod");
    await userEvent.type(screen.getByLabelText("Engine"), "claims-prod");
    await userEvent.type(screen.getByLabelText("Base URL"), "https://workflows.example.test/elsa");
    await userEvent.type(screen.getByLabelText("Credential reference"), "kv://claims/prod/elsa-api");
    await userEvent.click(screen.getByRole("button", { name: "Create setup" }));

    await screen.findByText("Claims Operations");
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
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/environments/env-created/engines`),
      expect.objectContaining({ method: "POST" })
    );
  });

  it("creates another deployment setup from a populated cockpit", async () => {
    const fetchMock = renderDeployments();

    await screen.findByRole("heading", { name: "Deployments" });
    await waitFor(() => expect(screen.getByRole("button", { name: "New Deployment" })).toBeEnabled());
    await userEvent.click(screen.getByRole("button", { name: "New Deployment" }));
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

    expect(screen.getByText("Payment Retry")).toBeInTheDocument();
    expect(screen.getAllByText("Secret references").length).toBeGreaterThan(0);
    expect(screen.getByText("Payment API secret reference is missing or not verified in Prod.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Deploy Revision" })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Roll Back to r39/i })).toBeEnabled();
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
  });

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
    expect(screen.queryByRole("button", { name: "Confirm Deployment" })).not.toBeInTheDocument();
  });
});

function renderDeployments(cockpit: DeploymentCockpit = deploymentCockpitFixture) {
  const fetchMock = createDeploymentFetchMock(cockpit);
  vi.stubGlobal("fetch", fetchMock);
  render(
    <TestQueryProvider>
      <DeploymentsPage />
    </TestQueryProvider>
  );
  return fetchMock;
}

function createDeploymentFetchMock(cockpit: DeploymentCockpit) {
  let currentCockpit = cockpit;
  return vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = input instanceof Request ? input.url : input.toString();
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    if (url.endsWith("/api/me/workspaces")) {
      return jsonResponse({
        account: { id: "account-1", displayName: "Test User", email: "test@example.com" },
        workspaces: [{ id: workspaceId, name: "Acme Insurance", kind: "Personal", role: "Owner" }]
      });
    }
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

function TestQueryProvider({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false }
    }
  });

  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

const workspaceId = "00000000-0000-0000-0000-000000000010";

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
      ]
    }
  ],
  observabilityBindings: [
    { id: "prod-logs", kind: "Logs", provider: "Azure Monitor", status: "Connected", scope: "claims-prod / rev 40", correlatedRevision: 40, sample: "143 structured events in the last 30 minutes" }
  ],
  history: [
    { id: "00000000-0000-0000-0000-000000000410", status: "Blocked", revision: 41, actor: "Mira Chen", environmentId: "claims-prod", engineId: "prod-engine", validationOutcome: "Blocked", occurredAt: "2026-05-22T08:05:00Z", rollbackSourceRevision: null }
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
    capabilities,
    controls: [
      { id: "pause-processing", label: "Pause Processing", boundary: "Workflow", capabilityId: "workflow.pause-processing", description: "Stops new workflow dispatch without touching host infrastructure." },
      { id: "reload-configuration", label: "Reload Configuration", boundary: "EngineApi", capabilityId: "engine.reload-configuration", description: "Reloads engine API configuration from desired state." },
      { id: "restart-shell", label: "Restart Shell", boundary: "Shell", capabilityId: "shell.restart", description: "Hidden until the shell restart capability is advertised." }
    ],
    hostingProvider: null
  };
}

function capability(
  id: string,
  label: string,
  boundary: DeploymentCockpit["engines"][number]["capabilities"][number]["boundary"]
) {
  return { id, label, boundary };
}
