using Elsa.Platform.Deployment.Abstractions;
using Elsa.Platform.Deployment.Abstractions.Resources;
using static Elsa.Platform.Deployment.Abstractions.DeploymentGuard;

namespace Elsa.Platform.Deployment.Abstractions.Diagnostics;

/// <summary>
/// Structured deployment diagnostic emitted by validation, planning, dry-run, apply, or history recording.
/// </summary>
public sealed record DeploymentDiagnostic
{
    public DeploymentDiagnostic(
        string code,
        DeploymentDiagnosticSeverity severity,
        string message,
        DeploymentResourceId? resourceId = null,
        IReadOnlyDictionary<string, string>? details = null)
    {
        Code = Require(code, nameof(code));
        Severity = severity;
        Message = Require(message, nameof(message));
        ResourceId = resourceId;
        Details = details ?? EmptyDetails;
    }

    public string Code { get; }

    public DeploymentDiagnosticSeverity Severity { get; }

    public string Message { get; }

    public DeploymentResourceId? ResourceId { get; }

    public IReadOnlyDictionary<string, string> Details { get; }

    private static readonly IReadOnlyDictionary<string, string> EmptyDetails = DeploymentEmpty.StringDictionary;
}
