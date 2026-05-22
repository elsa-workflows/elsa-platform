import type { DeploymentCockpit } from "@/features/deployments/deploymentModels";

const deploymentCockpit: DeploymentCockpit = {
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
    },
    {
      id: "customer-care",
      name: "Customer Care",
      workspaceName: "Acme Insurance",
      environments: [
        {
          id: "care-dev",
          name: "Dev",
          tier: "Dev",
          health: "Healthy",
          desiredRevision: { revision: 18, commit: "6ad11a3", label: "Chat escalation route", authoredAt: "2026-05-20T09:15:00Z" },
          deployedRevision: 18,
          deploymentStatus: "Succeeded",
          driftStatus: "InSync",
          engineIds: ["care-dev-engine"]
        },
        {
          id: "care-prod",
          name: "Prod",
          tier: "Production",
          health: "Healthy",
          desiredRevision: { revision: 16, commit: "3d920bc", label: "Baseline care routing", authoredAt: "2026-05-18T11:00:00Z" },
          deployedRevision: 16,
          deploymentStatus: "Succeeded",
          driftStatus: "InSync",
          engineIds: ["care-prod-engine"]
        }
      ]
    }
  ],
  engines: [
    {
      id: "dev-engine",
      name: "claims-dev-weu-01",
      environmentId: "claims-dev",
      endpoint: {
        baseUrl: "https://dev-workflows.acme.example/elsa",
        region: "West Europe",
        version: "Elsa 4.0.1",
        certificateStatus: "Trusted"
      },
      credentialReference: {
        provider: "Azure Key Vault",
        reference: "kv://acme-platform/dev/elsa-api",
        verificationStatus: "Verified",
        lastVerifiedAt: "2026-05-22T07:50:00Z"
      },
      health: "Healthy",
      lastHeartbeatAt: "2026-05-22T08:16:30Z",
      hostingProvider: null,
      capabilities: [
        { id: "workflow.pause-processing", label: "Pause workflow processing", boundary: "Workflow" },
        { id: "engine.reload-configuration", label: "Reload engine configuration", boundary: "EngineApi" },
        { id: "shell.restart", label: "Restart shell", boundary: "Shell" }
      ],
      controls: [
        { id: "pause-processing", label: "Pause Processing", boundary: "Workflow", capabilityId: "workflow.pause-processing", description: "Stops new workflow dispatch without touching host infrastructure." },
        { id: "reload-configuration", label: "Reload Configuration", boundary: "EngineApi", capabilityId: "engine.reload-configuration", description: "Reloads engine API configuration from desired state." },
        { id: "restart-shell", label: "Restart Shell", boundary: "Shell", capabilityId: "shell.restart", description: "Restarts one Elsa shell through the engine API." }
      ]
    },
    {
      id: "test-engine",
      name: "claims-test-weu-01",
      environmentId: "claims-test",
      endpoint: {
        baseUrl: "https://test-workflows.acme.example/elsa",
        region: "West Europe",
        version: "Elsa 4.0.1",
        certificateStatus: "Trusted"
      },
      credentialReference: {
        provider: "Azure Key Vault",
        reference: "kv://acme-platform/test/elsa-api",
        verificationStatus: "Verified",
        lastVerifiedAt: "2026-05-22T07:42:00Z"
      },
      health: "Healthy",
      lastHeartbeatAt: "2026-05-22T08:15:20Z",
      hostingProvider: "Azure Container Apps",
      capabilities: [
        { id: "workflow.pause-processing", label: "Pause workflow processing", boundary: "Workflow" },
        { id: "engine.reload-configuration", label: "Reload engine configuration", boundary: "EngineApi" },
        { id: "hosting.restart-revision", label: "Restart container app revision", boundary: "Hosting" }
      ],
      controls: [
        { id: "pause-processing", label: "Pause Processing", boundary: "Workflow", capabilityId: "workflow.pause-processing", description: "Stops new workflow dispatch without touching host infrastructure." },
        { id: "reload-configuration", label: "Reload Configuration", boundary: "EngineApi", capabilityId: "engine.reload-configuration", description: "Reloads engine API configuration from desired state." },
        { id: "restart-container-app-revision", label: "Restart Container App Revision", boundary: "Hosting", capabilityId: "hosting.restart-revision", description: "Runs the configured hosting adapter action for this engine." }
      ]
    },
    {
      id: "stage-engine",
      name: "claims-stage-weu-01",
      environmentId: "claims-stage",
      endpoint: {
        baseUrl: "https://stage-workflows.acme.example/elsa",
        region: "West Europe",
        version: "Elsa 4.0.0",
        certificateStatus: "Expiring"
      },
      credentialReference: {
        provider: "Azure Key Vault",
        reference: "kv://acme-platform/stage/elsa-api",
        verificationStatus: "Verified",
        lastVerifiedAt: "2026-05-22T07:33:00Z"
      },
      health: "Degraded",
      lastHeartbeatAt: "2026-05-22T08:09:00Z",
      hostingProvider: null,
      capabilities: [
        { id: "workflow.pause-processing", label: "Pause workflow processing", boundary: "Workflow" },
        { id: "engine.reload-configuration", label: "Reload engine configuration", boundary: "EngineApi" }
      ],
      controls: [
        { id: "pause-processing", label: "Pause Processing", boundary: "Workflow", capabilityId: "workflow.pause-processing", description: "Stops new workflow dispatch without touching host infrastructure." },
        { id: "reload-configuration", label: "Reload Configuration", boundary: "EngineApi", capabilityId: "engine.reload-configuration", description: "Reloads engine API configuration from desired state." },
        { id: "restart-shell", label: "Restart Shell", boundary: "Shell", capabilityId: "shell.restart", description: "Hidden until the shell restart capability is advertised." }
      ]
    },
    {
      id: "prod-engine",
      name: "claims-prod-weu-01",
      environmentId: "claims-prod",
      endpoint: {
        baseUrl: "https://workflows.acme.example/elsa",
        region: "West Europe",
        version: "Elsa 4.0.0",
        certificateStatus: "Trusted"
      },
      credentialReference: {
        provider: "Azure Key Vault",
        reference: "kv://acme-platform/prod/elsa-api",
        verificationStatus: "Missing",
        lastVerifiedAt: null
      },
      health: "Unreachable",
      lastHeartbeatAt: null,
      hostingProvider: null,
      capabilities: [{ id: "workflow.pause-processing", label: "Pause workflow processing", boundary: "Workflow" }],
      controls: [
        { id: "pause-processing", label: "Pause Processing", boundary: "Workflow", capabilityId: "workflow.pause-processing", description: "Stops new workflow dispatch without touching host infrastructure." },
        { id: "reload-configuration", label: "Reload Configuration", boundary: "EngineApi", capabilityId: "engine.reload-configuration", description: "Hidden until the engine advertises support." }
      ]
    },
    {
      id: "care-dev-engine",
      name: "care-dev-weu-01",
      environmentId: "care-dev",
      endpoint: {
        baseUrl: "https://care-dev.acme.example/elsa",
        region: "West Europe",
        version: "Elsa 4.0.1",
        certificateStatus: "Trusted"
      },
      credentialReference: {
        provider: "Azure Key Vault",
        reference: "kv://acme-platform/care-dev/elsa-api",
        verificationStatus: "Verified",
        lastVerifiedAt: "2026-05-22T07:20:00Z"
      },
      health: "Healthy",
      lastHeartbeatAt: "2026-05-22T08:14:00Z",
      hostingProvider: null,
      capabilities: [{ id: "engine.reload-configuration", label: "Reload engine configuration", boundary: "EngineApi" }],
      controls: [
        { id: "reload-configuration", label: "Reload Configuration", boundary: "EngineApi", capabilityId: "engine.reload-configuration", description: "Reloads engine API configuration from desired state." }
      ]
    },
    {
      id: "care-prod-engine",
      name: "care-prod-weu-01",
      environmentId: "care-prod",
      endpoint: {
        baseUrl: "https://care.acme.example/elsa",
        region: "West Europe",
        version: "Elsa 4.0.1",
        certificateStatus: "Trusted"
      },
      credentialReference: {
        provider: "Azure Key Vault",
        reference: "kv://acme-platform/care-prod/elsa-api",
        verificationStatus: "Verified",
        lastVerifiedAt: "2026-05-22T07:25:00Z"
      },
      health: "Healthy",
      lastHeartbeatAt: "2026-05-22T08:13:00Z",
      hostingProvider: null,
      capabilities: [
        { id: "workflow.pause-processing", label: "Pause workflow processing", boundary: "Workflow" },
        { id: "engine.reload-configuration", label: "Reload engine configuration", boundary: "EngineApi" }
      ],
      controls: [
        { id: "pause-processing", label: "Pause Processing", boundary: "Workflow", capabilityId: "workflow.pause-processing", description: "Stops new workflow dispatch without touching host infrastructure." },
        { id: "reload-configuration", label: "Reload Configuration", boundary: "EngineApi", capabilityId: "engine.reload-configuration", description: "Reloads engine API configuration from desired state." }
      ]
    }
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
        { id: "workflow-policy-sync", category: "Workflows", name: "Policy Document Sync", sourceValue: "Added nightly sync", targetValue: "Not deployed", impact: "Added" },
        { id: "feature-fraud-score", category: "Features", name: "Fraud score enrichment", sourceValue: "Enabled", targetValue: "Disabled", impact: "Changed" },
        { id: "shell-claims", category: "Shell configuration", name: "claims-shell", sourceValue: "Concurrency 16", targetValue: "Concurrency 8", impact: "Changed" },
        { id: "runtime-http", category: "Runtime configuration", name: "HTTP activities", sourceValue: "Timeout 30s", targetValue: "Timeout 15s", impact: "Changed" },
        { id: "secret-payment-api", category: "Secret references", name: "Payment API", sourceValue: "kv://acme-platform/prod/payment-api:v3", targetValue: "Missing reference", impact: "Changed" },
        { id: "otel-binding", category: "Observability", name: "OpenTelemetry exporter", sourceValue: "otlp/acme-prod", targetValue: "otlp/acme-legacy", impact: "Changed" },
        { id: "engine-binding", category: "Engine bindings", name: "claims-prod-weu-01", sourceValue: "Requires engine.reload-configuration", targetValue: "Capability not advertised", impact: "Changed" }
      ],
      validations: [
        { id: "secret-payment-api", severity: "Blocker", scope: "Secret references", message: "Payment API secret reference is missing or not verified in Prod." },
        { id: "capability-reload", severity: "Blocker", scope: "Engine capabilities", message: "claims-prod-weu-01 does not advertise engine.reload-configuration." },
        { id: "entitlement-prod-deploy", severity: "Pass", scope: "Workspace entitlement", message: "Deployment entitlement active for Acme Insurance." },
        { id: "engine-reachability", severity: "Blocker", scope: "Engine health", message: "claims-prod-weu-01 is unreachable; validation fails closed." }
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
        { id: "runtime-http-test", category: "Runtime configuration", name: "HTTP activities", sourceValue: "Timeout 30s", targetValue: "Timeout 15s", impact: "Changed" },
        { id: "secret-payment-test", category: "Secret references", name: "Payment API", sourceValue: "kv://acme-platform/test/payment-api:v3", targetValue: "kv://acme-platform/test/payment-api:v2", impact: "Changed" }
      ],
      validations: [
        { id: "secret-payment-test", severity: "Pass", scope: "Secret references", message: "Required secret references are verified for Test." },
        { id: "capability-test", severity: "Pass", scope: "Engine capabilities", message: "claims-test-weu-01 supports required engine operations." },
        { id: "entitlement-test", severity: "Pass", scope: "Workspace entitlement", message: "Deployment entitlement active for Acme Insurance." }
      ]
    }
  ],
  observabilityBindings: [
    { id: "prod-logs", kind: "Logs", provider: "Azure Monitor", status: "Connected", scope: "claims-prod / rev 40", correlatedRevision: 40, sample: "143 structured events in the last 30 minutes" },
    { id: "prod-traces", kind: "Traces", provider: "OpenTelemetry Collector", status: "Degraded", scope: "claims-prod / claims-prod-weu-01", correlatedRevision: 40, sample: "Trace sampling delayed by 4 minutes" },
    { id: "prod-metrics", kind: "Metrics", provider: "Prometheus", status: "Connected", scope: "claims-prod / workflow instances", correlatedRevision: 40, sample: "p95 workflow dispatch 420 ms" },
    { id: "prod-console", kind: "Console", provider: "Engine console stream", status: "Unavailable", scope: "claims-prod / claims-prod-weu-01", correlatedRevision: 40, sample: "Credential reference must verify before console stream opens" }
  ],
  history: [
    { id: "deploy-410", status: "Blocked", revision: 41, actor: "Mira Chen", environmentId: "claims-prod", engineId: "prod-engine", validationOutcome: "Blocked", occurredAt: "2026-05-22T08:05:00Z", rollbackSourceRevision: null },
    { id: "deploy-409", status: "Succeeded", revision: 40, actor: "Owen Diaz", environmentId: "claims-prod", engineId: "prod-engine", validationOutcome: "Passed", occurredAt: "2026-05-21T17:20:00Z", rollbackSourceRevision: null },
    { id: "deploy-388", status: "RolledBack", revision: 39, actor: "Priya Shah", environmentId: "claims-prod", engineId: "prod-engine", validationOutcome: "Warnings", occurredAt: "2026-05-20T19:45:00Z", rollbackSourceRevision: 38 },
    { id: "deploy-342", status: "Succeeded", revision: 42, actor: "Mira Chen", environmentId: "claims-test", engineId: "test-engine", validationOutcome: "Passed", occurredAt: "2026-05-22T07:35:00Z", rollbackSourceRevision: null }
  ],
  driftReport: [
    { id: "drift-shell", environmentId: "claims-stage", engineId: "stage-engine", area: "Shell concurrency", desired: "16 workers", observed: "12 workers", action: "Review" },
    { id: "drift-feature", environmentId: "claims-stage", engineId: "stage-engine", area: "Fraud score enrichment", desired: "Enabled", observed: "Disabled", action: "Redeploy" },
    { id: "drift-prod", environmentId: "claims-prod", engineId: "prod-engine", area: "Engine observed state", desired: "Revision 40", observed: "Unavailable", action: "Review" }
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
        { id: "assistant-secret", severity: "Blocker", scope: "Secret references", message: "Payment API reference must verify before approval can execute." },
        { id: "assistant-capability", severity: "Blocker", scope: "Engine capabilities", message: "Engine reload capability is required but not advertised by Prod." }
      ],
      rollbackPath: "Redeploy revision 39 to claims-prod-weu-01 if revision 41 fails after validation clears.",
      allOrNothing: true,
      createdAt: "2026-05-22T08:02:00Z"
    }
  ]
};

export async function getDeploymentCockpit(): Promise<DeploymentCockpit> {
  return deploymentCockpit;
}
