using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed record WorkspaceObservabilityBinding(
    Guid Id,
    Guid WorkspaceId,
    Guid EnvironmentId,
    Guid? EngineId,
    ObservabilityBindingKind Kind,
    string Provider,
    ObservabilityBindingStatus Status,
    string Scope,
    Guid? CorrelatedRevisionId,
    string? Sample);

public sealed record WorkspaceDriftReportItem(
    Guid Id,
    Guid WorkspaceId,
    Guid EnvironmentId,
    Guid EngineId,
    string Area,
    string Desired,
    string Observed,
    DriftAction Action,
    DateTimeOffset DetectedAt);
