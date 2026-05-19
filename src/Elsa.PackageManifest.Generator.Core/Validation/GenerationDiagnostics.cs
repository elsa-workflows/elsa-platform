namespace Elsa.PackageManifest.Generator.Core.Validation;

public enum GenerationDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public enum GenerationDiagnosticCategory
{
    General,
    ManifestValidation,
    RecommendedMetadata,
    SettingDiscovery,
    PackageInclusion,
    Infrastructure,
    InvalidInput
}

public sealed record GenerationDiagnostic(
    string Code,
    GenerationDiagnosticSeverity Severity,
    string Message,
    string? Target = null,
    string? ManifestPath = null,
    string? RuleId = null,
    GenerationDiagnosticCategory Category = GenerationDiagnosticCategory.General,
    bool CanMapValidationSeverity = false,
    bool IsFatal = false);

public sealed class GenerationDiagnostics
{
    private readonly List<GenerationDiagnostic> _items = [];

    public IReadOnlyList<GenerationDiagnostic> Items => _items;
    public bool HasErrors => _items.Any(x => x.Severity == GenerationDiagnosticSeverity.Error);

    public bool HasFatalErrors => _items.Any(x => x.IsFatal);

    public void Info(
        string code,
        string message,
        string? target = null,
        string? manifestPath = null,
        string? ruleId = null,
        GenerationDiagnosticCategory category = GenerationDiagnosticCategory.General) =>
        Add(new GenerationDiagnostic(code, GenerationDiagnosticSeverity.Info, message, target, manifestPath, ruleId, category));

    public void Warning(
        string code,
        string message,
        string? target = null,
        string? manifestPath = null,
        string? ruleId = null,
        GenerationDiagnosticCategory category = GenerationDiagnosticCategory.General,
        bool canMapValidationSeverity = false) =>
        Add(new GenerationDiagnostic(code, GenerationDiagnosticSeverity.Warning, message, target, manifestPath, ruleId, category, canMapValidationSeverity));

    public void Error(
        string code,
        string message,
        string? target = null,
        string? manifestPath = null,
        string? ruleId = null,
        GenerationDiagnosticCategory category = GenerationDiagnosticCategory.General,
        bool canMapValidationSeverity = false,
        bool isFatal = false) =>
        Add(new GenerationDiagnostic(code, GenerationDiagnosticSeverity.Error, message, target, manifestPath, ruleId, category, canMapValidationSeverity, isFatal));

    public void Fatal(
        string code,
        string message,
        string? target = null,
        string? manifestPath = null,
        string? ruleId = null,
        GenerationDiagnosticCategory category = GenerationDiagnosticCategory.Infrastructure) =>
        Add(new GenerationDiagnostic(code, GenerationDiagnosticSeverity.Error, message, target, manifestPath, ruleId, category, false, true));

    public void Verbose(string code, string message, string? target = null, string? manifestPath = null, string? ruleId = null) =>
        Info(code, message, target, manifestPath, ruleId, GenerationDiagnosticCategory.SettingDiscovery);

    public void Add(GenerationDiagnostic diagnostic) => _items.Add(diagnostic);
}
