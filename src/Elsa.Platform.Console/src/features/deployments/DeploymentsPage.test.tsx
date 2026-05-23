import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
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

    expect(screen.getByRole("status")).toHaveTextContent("Reload Configuration queued as a EngineApi control");
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
});

function renderDeployments(cockpit: DeploymentCockpit = deploymentCockpitFixture) {
  vi.stubGlobal("fetch", createDeploymentFetchMock(cockpit));
  render(
    <TestQueryProvider>
      <DeploymentsPage />
    </TestQueryProvider>
  );
}

function createDeploymentFetchMock(cockpit: DeploymentCockpit) {
  return vi.fn(async (input: RequestInfo | URL) => {
    const url = input instanceof Request ? input.url : input.toString();
    if (url.endsWith("/api/me/workspaces")) {
      return jsonResponse({
        account: { id: "account-1", displayName: "Test User", email: "test@example.com" },
        workspaces: [{ id: workspaceId, name: "Acme Insurance", kind: "Personal", role: "Owner" }]
      });
    }
    if (url.endsWith(`/api/workspaces/${workspaceId}/deployments/cockpit`)) {
      return jsonResponse(cockpit);
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
          desiredRevision: { revision: 42, commit: "8f6a9c1", label: "Payment retry workflow", authoredAt: "2026-05-21T08:30:00Z" },
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
          desiredRevision: { revision: 39, commit: "79d1b07", label: "Fraud review tuning", authoredAt: "2026-05-20T13:20:00Z" },
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
          desiredRevision: { revision: 41, commit: "c174f2a", label: "Policy document sync", authoredAt: "2026-05-21T06:10:00Z" },
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
          desiredRevision: { revision: 40, commit: "11ec9d4", label: "Baseline production", authoredAt: "2026-05-19T15:45:00Z" },
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
      sourceRevision: 41,
      targetRevision: 40,
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
      sourceRevision: 42,
      targetRevision: 39,
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
    { id: "deploy-410", status: "Blocked", revision: 41, actor: "Mira Chen", environmentId: "claims-prod", engineId: "prod-engine", validationOutcome: "Blocked", occurredAt: "2026-05-22T08:05:00Z", rollbackSourceRevision: null }
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
