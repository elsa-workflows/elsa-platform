using Elsa.Platform.Deployment.Core.Workspace;

namespace Elsa.Platform.Deployment.Core.Cockpit;

public sealed class InMemoryDeploymentCockpitStore : IDeploymentCockpitStore, IWorkspaceDeploymentStore
{
    public Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Seed(workspaceId));

    public Task<WorkspaceDeploymentApplication> CreateApplicationAsync(
        Guid workspaceId,
        CreateWorkflowApplicationRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The in-memory cockpit store is read-only.");

    public Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(
        Guid workspaceId,
        CreateDeploymentEnvironmentRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The in-memory cockpit store is read-only.");

    public Task<WorkspaceDeploymentApplication> UpdateApplicationAsync(
        Guid workspaceId,
        Guid applicationId,
        UpdateWorkflowApplicationRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The in-memory cockpit store is read-only.");

    public Task<WorkspaceDeploymentEnvironment> UpdateEnvironmentAsync(
        Guid workspaceId,
        Guid environmentId,
        UpdateDeploymentEnvironmentRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The in-memory cockpit store is read-only.");

    public Task<WorkspaceWorkflowEngine> RegisterEngineAsync(
        Guid workspaceId,
        RegisterWorkflowEngineRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The in-memory cockpit store is read-only.");

    public Task<WorkspaceWorkflowEngine> UpdateEngineAsync(
        Guid workspaceId,
        Guid engineId,
        UpdateWorkflowEngineRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The in-memory cockpit store is read-only.");

    public Task<WorkspaceDesiredStateRevision> CreateRevisionAsync(
        Guid workspaceId,
        CreateDesiredStateRevisionRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The in-memory cockpit store is read-only.");

    public Task<WorkspaceDesiredStateRevision?> GetRevisionAsync(
        Guid workspaceId,
        Guid revisionId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The in-memory cockpit store is read-only.");

    public Task<WorkspaceDesiredStateRevision?> GetLatestRevisionAsync(
        Guid workspaceId,
        Guid environmentId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The in-memory cockpit store is read-only.");

    public Task<WorkspaceWorkflowEngine?> GetEngineAsync(
        Guid workspaceId,
        Guid engineId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The in-memory cockpit store is read-only.");

    private static DeploymentCockpit Seed(Guid workspaceId)
    {
        var workspaceName = $"Workspace {workspaceId.ToString("N")[..8]}";
        return new DeploymentCockpit(
            Applications(workspaceName),
            Engines(),
            Comparisons(),
            ObservabilityBindings(),
            History(),
            DriftReport(),
            AssistantPlans(workspaceName));
    }

    private static IReadOnlyList<WorkflowApplication> Applications(string workspaceName) =>
    [
        new(
            "claims-ops",
            "Claims Operations",
            workspaceName,
            [
                new(
                    "claims-dev",
                    "Dev",
                    EnvironmentTier.Dev,
                    DeploymentHealth.Healthy,
                    new DesiredStateRevision("00000000-0000-0000-0000-000000000142", 42, "8f6a9c1", "Payment retry workflow", Parse("2026-05-21T08:30:00Z")),
                    42,
                    DeploymentStatus.Succeeded,
                    DriftStatus.InSync,
                    ["dev-engine"]),
                new(
                    "claims-test",
                    "Test",
                    EnvironmentTier.Test,
                    DeploymentHealth.Healthy,
                    new DesiredStateRevision("00000000-0000-0000-0000-000000000139", 39, "79d1b07", "Fraud review tuning", Parse("2026-05-20T13:20:00Z")),
                    39,
                    DeploymentStatus.Succeeded,
                    DriftStatus.InSync,
                    ["test-engine"]),
                new(
                    "claims-stage",
                    "Stage",
                    EnvironmentTier.Stage,
                    DeploymentHealth.Degraded,
                    new DesiredStateRevision("00000000-0000-0000-0000-000000000141", 41, "c174f2a", "Policy document sync", Parse("2026-05-21T06:10:00Z")),
                    40,
                    DeploymentStatus.Running,
                    DriftStatus.DriftDetected,
                    ["stage-engine"]),
                new(
                    "claims-prod",
                    "Prod",
                    EnvironmentTier.Production,
                    DeploymentHealth.Unreachable,
                    new DesiredStateRevision("00000000-0000-0000-0000-000000000140", 40, "11ec9d4", "Baseline production", Parse("2026-05-19T15:45:00Z")),
                    40,
                    DeploymentStatus.Blocked,
                    DriftStatus.Unknown,
                    ["prod-engine"])
            ]),
        new(
            "customer-care",
            "Customer Care",
            workspaceName,
            [
                new(
                    "care-dev",
                    "Dev",
                    EnvironmentTier.Dev,
                    DeploymentHealth.Healthy,
                    new DesiredStateRevision("00000000-0000-0000-0000-000000000118", 18, "6ad11a3", "Chat escalation route", Parse("2026-05-20T09:15:00Z")),
                    18,
                    DeploymentStatus.Succeeded,
                    DriftStatus.InSync,
                    ["care-dev-engine"]),
                new(
                    "care-prod",
                    "Prod",
                    EnvironmentTier.Production,
                    DeploymentHealth.Healthy,
                    new DesiredStateRevision("00000000-0000-0000-0000-000000000116", 16, "3d920bc", "Baseline care routing", Parse("2026-05-18T11:00:00Z")),
                    16,
                    DeploymentStatus.Succeeded,
                    DriftStatus.InSync,
                    ["care-prod-engine"])
            ])
    ];

    private static IReadOnlyList<WorkflowEngineRegistration> Engines() =>
    [
        Engine(
            "dev-engine",
            "claims-dev-weu-01",
            "claims-dev",
            "https://dev-workflows.acme.example/elsa",
            "kv://acme-platform/dev/elsa-api",
            DeploymentHealth.Healthy,
            CredentialVerificationStatus.Verified,
            Parse("2026-05-22T08:16:30Z"),
            null,
            [
                Capability("workflow.pause-processing", "Pause workflow processing", CapabilityBoundary.Workflow),
                Capability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi),
                Capability("shell.restart", "Restart shell", CapabilityBoundary.Shell)
            ],
            [
                Control("pause-processing", "Pause Processing", CapabilityBoundary.Workflow, "workflow.pause-processing", "Stops new workflow dispatch without touching host infrastructure."),
                Control("reload-configuration", "Reload Configuration", CapabilityBoundary.EngineApi, "engine.reload-configuration", "Reloads engine API configuration from desired state."),
                Control("restart-shell", "Restart Shell", CapabilityBoundary.Shell, "shell.restart", "Restarts one Elsa shell through the engine API.")
            ]),
        Engine(
            "test-engine",
            "claims-test-weu-01",
            "claims-test",
            "https://test-workflows.acme.example/elsa",
            "kv://acme-platform/test/elsa-api",
            DeploymentHealth.Healthy,
            CredentialVerificationStatus.Verified,
            Parse("2026-05-22T08:15:20Z"),
            "Azure Container Apps",
            [
                Capability("workflow.pause-processing", "Pause workflow processing", CapabilityBoundary.Workflow),
                Capability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi),
                Capability("hosting.restart-revision", "Restart container app revision", CapabilityBoundary.Hosting)
            ],
            [
                Control("pause-processing", "Pause Processing", CapabilityBoundary.Workflow, "workflow.pause-processing", "Stops new workflow dispatch without touching host infrastructure."),
                Control("reload-configuration", "Reload Configuration", CapabilityBoundary.EngineApi, "engine.reload-configuration", "Reloads engine API configuration from desired state."),
                Control("restart-container-app-revision", "Restart Container App Revision", CapabilityBoundary.Hosting, "hosting.restart-revision", "Runs the configured hosting adapter action for this engine.")
            ]),
        Engine(
            "stage-engine",
            "claims-stage-weu-01",
            "claims-stage",
            "https://stage-workflows.acme.example/elsa",
            "kv://acme-platform/stage/elsa-api",
            DeploymentHealth.Degraded,
            CredentialVerificationStatus.Verified,
            Parse("2026-05-22T08:09:00Z"),
            null,
            [
                Capability("workflow.pause-processing", "Pause workflow processing", CapabilityBoundary.Workflow),
                Capability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi)
            ],
            [
                Control("pause-processing", "Pause Processing", CapabilityBoundary.Workflow, "workflow.pause-processing", "Stops new workflow dispatch without touching host infrastructure."),
                Control("reload-configuration", "Reload Configuration", CapabilityBoundary.EngineApi, "engine.reload-configuration", "Reloads engine API configuration from desired state."),
                Control("restart-shell", "Restart Shell", CapabilityBoundary.Shell, "shell.restart", "Hidden until the shell restart capability is advertised.")
            ],
            CertificateStatus.Expiring,
            "Elsa 4.0.0"),
        Engine(
            "prod-engine",
            "claims-prod-weu-01",
            "claims-prod",
            "https://workflows.acme.example/elsa",
            "kv://acme-platform/prod/elsa-api",
            DeploymentHealth.Unreachable,
            CredentialVerificationStatus.Missing,
            null,
            null,
            [Capability("workflow.pause-processing", "Pause workflow processing", CapabilityBoundary.Workflow)],
            [
                Control("pause-processing", "Pause Processing", CapabilityBoundary.Workflow, "workflow.pause-processing", "Stops new workflow dispatch without touching host infrastructure."),
                Control("reload-configuration", "Reload Configuration", CapabilityBoundary.EngineApi, "engine.reload-configuration", "Hidden until the engine advertises support.")
            ],
            version: "Elsa 4.0.0"),
        Engine(
            "care-dev-engine",
            "care-dev-weu-01",
            "care-dev",
            "https://care-dev.acme.example/elsa",
            "kv://acme-platform/care-dev/elsa-api",
            DeploymentHealth.Healthy,
            CredentialVerificationStatus.Verified,
            Parse("2026-05-22T08:14:00Z"),
            null,
            [Capability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi)],
            [Control("reload-configuration", "Reload Configuration", CapabilityBoundary.EngineApi, "engine.reload-configuration", "Reloads engine API configuration from desired state.")]),
        Engine(
            "care-prod-engine",
            "care-prod-weu-01",
            "care-prod",
            "https://care.acme.example/elsa",
            "kv://acme-platform/care-prod/elsa-api",
            DeploymentHealth.Healthy,
            CredentialVerificationStatus.Verified,
            Parse("2026-05-22T08:13:00Z"),
            null,
            [
                Capability("workflow.pause-processing", "Pause workflow processing", CapabilityBoundary.Workflow),
                Capability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi)
            ],
            [
                Control("pause-processing", "Pause Processing", CapabilityBoundary.Workflow, "workflow.pause-processing", "Stops new workflow dispatch without touching host infrastructure."),
                Control("reload-configuration", "Reload Configuration", CapabilityBoundary.EngineApi, "engine.reload-configuration", "Reloads engine API configuration from desired state.")
            ])
    ];

    private static IReadOnlyList<PromotionComparison> Comparisons() =>
    [
        new(
            "claims-stage",
            "claims-prod",
            "00000000-0000-0000-0000-000000000141",
            41,
            40,
            [
                Diff("workflow-payment-retry", DiffCategory.Workflows, "Payment Retry", "v7 with idempotent retry", "v6", DiffImpact.Changed),
                Diff("workflow-policy-sync", DiffCategory.Workflows, "Policy Document Sync", "Added nightly sync", "Not deployed", DiffImpact.Added),
                Diff("feature-fraud-score", DiffCategory.Features, "Fraud score enrichment", "Enabled", "Disabled", DiffImpact.Changed),
                Diff("shell-claims", DiffCategory.ShellConfiguration, "claims-shell", "Concurrency 16", "Concurrency 8", DiffImpact.Changed),
                Diff("runtime-http", DiffCategory.RuntimeConfiguration, "HTTP activities", "Timeout 30s", "Timeout 15s", DiffImpact.Changed),
                Diff("secret-payment-api", DiffCategory.SecretReferences, "Payment API", "kv://acme-platform/prod/payment-api:v3", "Missing reference", DiffImpact.Changed),
                Diff("otel-binding", DiffCategory.Observability, "OpenTelemetry exporter", "otlp/acme-prod", "otlp/acme-legacy", DiffImpact.Changed),
                Diff("engine-binding", DiffCategory.EngineBindings, "claims-prod-weu-01", "Requires engine.reload-configuration", "Capability not advertised", DiffImpact.Changed)
            ],
            [
                Validation("secret-payment-api", ValidationSeverity.Blocker, "Secret references", "Payment API secret reference is missing or not verified in Prod."),
                Validation("capability-reload", ValidationSeverity.Blocker, "Engine capabilities", "claims-prod-weu-01 does not advertise engine.reload-configuration."),
                Validation("entitlement-prod-deploy", ValidationSeverity.Pass, "Workspace entitlement", "Deployment entitlement active for this workspace."),
                Validation("engine-reachability", ValidationSeverity.Blocker, "Engine health", "claims-prod-weu-01 is unreachable; validation fails closed.")
            ],
            39,
            "00000000-0000-0000-0000-000000000139"),
        new(
            "claims-dev",
            "claims-test",
            "00000000-0000-0000-0000-000000000142",
            42,
            39,
            [
                Diff("workflow-payment-retry-test", DiffCategory.Workflows, "Payment Retry", "v8", "v6", DiffImpact.Changed),
                Diff("runtime-http-test", DiffCategory.RuntimeConfiguration, "HTTP activities", "Timeout 30s", "Timeout 15s", DiffImpact.Changed),
                Diff("secret-payment-test", DiffCategory.SecretReferences, "Payment API", "kv://acme-platform/test/payment-api:v3", "kv://acme-platform/test/payment-api:v2", DiffImpact.Changed)
            ],
            [
                Validation("secret-payment-test", ValidationSeverity.Pass, "Secret references", "Required secret references are verified for Test."),
                Validation("capability-test", ValidationSeverity.Pass, "Engine capabilities", "claims-test-weu-01 supports required engine operations."),
                Validation("entitlement-test", ValidationSeverity.Pass, "Workspace entitlement", "Deployment entitlement active for this workspace.")
            ],
            38,
            "00000000-0000-0000-0000-000000000138")
    ];

    private static IReadOnlyList<ObservabilityBinding> ObservabilityBindings() =>
    [
        new("prod-logs", ObservabilityBindingKind.Logs, "Azure Monitor", ObservabilityBindingStatus.Connected, "claims-prod / rev 40", 40, "143 structured events in the last 30 minutes"),
        new("prod-traces", ObservabilityBindingKind.Traces, "OpenTelemetry Collector", ObservabilityBindingStatus.Degraded, "claims-prod / claims-prod-weu-01", 40, "Trace sampling delayed by 4 minutes"),
        new("prod-metrics", ObservabilityBindingKind.Metrics, "Prometheus", ObservabilityBindingStatus.Connected, "claims-prod / workflow runs", 40, "p95 workflow dispatch 420 ms"),
        new("prod-console", ObservabilityBindingKind.Console, "Engine console stream", ObservabilityBindingStatus.Unavailable, "claims-prod / claims-prod-weu-01", 40, "Credential reference must verify before console stream opens")
    ];

    private static IReadOnlyList<DeploymentHistoryEvent> History() =>
    [
        new("00000000-0000-0000-0000-000000000410", DeploymentStatus.Blocked.ToString(), 41, "Mira Chen", "claims-prod", "prod-engine", DeploymentValidationOutcome.Blocked, Parse("2026-05-22T08:05:00Z"), null),
        new("00000000-0000-0000-0000-000000000409", DeploymentStatus.Succeeded.ToString(), 40, "Owen Diaz", "claims-prod", "prod-engine", DeploymentValidationOutcome.Passed, Parse("2026-05-21T17:20:00Z"), null),
        new("00000000-0000-0000-0000-000000000388", DeploymentStatus.RolledBack.ToString(), 39, "Priya Shah", "claims-prod", "prod-engine", DeploymentValidationOutcome.Warnings, Parse("2026-05-20T19:45:00Z"), 38),
        new("00000000-0000-0000-0000-000000000342", DeploymentStatus.Succeeded.ToString(), 42, "Mira Chen", "claims-test", "test-engine", DeploymentValidationOutcome.Passed, Parse("2026-05-22T07:35:00Z"), null)
    ];

    private static IReadOnlyList<DriftReportItem> DriftReport() =>
    [
        new("drift-shell", "claims-stage", "stage-engine", "Shell concurrency", "16 workers", "12 workers", DriftAction.Review),
        new("drift-feature", "claims-stage", "stage-engine", "Fraud score enrichment", "Enabled", "Disabled", DriftAction.Redeploy),
        new("drift-prod", "claims-prod", "prod-engine", "Engine observed state", "Revision 40", "Unavailable", DriftAction.Review)
    ];

    private static IReadOnlyList<AssistantPlan> AssistantPlans(string workspaceName) =>
    [
        new(
            "plan-20260522-001",
            3,
            AssistantPlanStatus.Proposed,
            workspaceName,
            "claims-prod",
            "prod-engine",
            "Promote revision 41 from Stage to Prod after validating secrets, reachability, and engine reload capability.",
            [
                "Verify Prod secret references and provider access",
                "Run desired-state diff from Stage revision 41 to Prod revision 40",
                "Apply revision 41 to claims-prod-weu-01 as one deployment",
                "Keep rollback path to revision 39 available"
            ],
            [],
            [
                Validation("assistant-scope", ValidationSeverity.Pass, "Workspace authorization", "Plan is scoped to the selected workspace only."),
                Validation("assistant-secret", ValidationSeverity.Blocker, "Secret references", "Payment API reference must verify before approval can execute."),
                Validation("assistant-capability", ValidationSeverity.Blocker, "Engine capabilities", "Engine reload capability is required but not advertised by Prod.")
            ],
            "Redeploy revision 39 to claims-prod-weu-01 if revision 41 fails after validation clears.",
            true,
            Parse("2026-05-22T08:02:00Z"))
    ];

    private static WorkflowEngineRegistration Engine(
        string id,
        string name,
        string environmentId,
        string endpoint,
        string credentialReference,
        DeploymentHealth health,
        CredentialVerificationStatus credentialStatus,
        DateTimeOffset? lastHeartbeatAt,
        string? hostingProvider,
        IReadOnlyList<EngineCapability> capabilities,
        IReadOnlyList<RuntimeControl> controls,
        CertificateStatus certificateStatus = CertificateStatus.Trusted,
        string version = "Elsa 4.0.1") =>
        new(
            id,
            name,
            environmentId,
            new EngineEndpointMetadata(endpoint, "West Europe", version, certificateStatus),
            new EngineCredentialReference("Azure Key Vault", credentialReference, credentialStatus, credentialStatus == CredentialVerificationStatus.Verified ? Parse("2026-05-22T07:50:00Z") : null),
            health,
            lastHeartbeatAt,
            capabilities,
            controls,
            hostingProvider);

    private static EngineCapability Capability(string id, string label, CapabilityBoundary boundary) =>
        new(id, label, boundary);

    private static RuntimeControl Control(string id, string label, CapabilityBoundary boundary, string capabilityId, string description) =>
        new(id, label, boundary, capabilityId, description);

    private static DeploymentDiffItem Diff(string id, DiffCategory category, string name, string sourceValue, string targetValue, DiffImpact impact) =>
        new(id, category, name, sourceValue, targetValue, impact);

    private static DeploymentValidation Validation(string id, ValidationSeverity severity, string scope, string message) =>
        new(id, severity, scope, message);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.AssumeUniversal);
}
