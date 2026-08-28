namespace ElsaControl.Workflows.RuntimeApplier;

public static class WorkflowArtifactRuntimeDiagnosticSanitizer
{
    private static readonly string[] UnsafeTerms =
    [
        "authorization",
        "bearer",
        "connection string",
        "connectionstring",
        "password",
        "private key",
        "secret",
        "token"
    ];

    public static string SafeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Workflow artifact runtime operation could not be completed.";

        var safe = message.Trim();
        foreach (var term in UnsafeTerms)
            safe = safe.Replace(term, "[redacted]", StringComparison.OrdinalIgnoreCase);
        return safe.Length <= 512 ? safe : safe[..512];
    }
}
