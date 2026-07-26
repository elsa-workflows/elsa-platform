namespace ValenceControl.Deployment.Abstractions.Diagnostics;

/// <summary>
/// Classifies deployment diagnostics by severity.
/// </summary>
public enum DeploymentDiagnosticSeverity
{
    Info,
    Warning,
    Error,
    Fatal
}
