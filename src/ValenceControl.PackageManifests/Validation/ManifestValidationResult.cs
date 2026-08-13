namespace ValenceControl.PackageManifests.Validation;

public enum ManifestValidationStatus
{
    Valid,
    Invalid,
    UnsupportedSchema
}

public enum ManifestValidationSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ManifestValidationFinding(
    string Path,
    string RuleId,
    string Message,
    ManifestValidationSeverity Severity);

public sealed record ManifestValidationResult(
    ManifestValidationStatus Status,
    IReadOnlyList<ManifestValidationFinding> Errors,
    IReadOnlyList<ManifestValidationFinding> Warnings)
{
    public bool IsValid => Status == ManifestValidationStatus.Valid && Errors.Count == 0;

    public static ManifestValidationResult Valid(IReadOnlyList<ManifestValidationFinding>? warnings = null) =>
        new(ManifestValidationStatus.Valid, [], warnings ?? []);

    public static ManifestValidationResult Invalid(params ManifestValidationFinding[] errors) =>
        new(ManifestValidationStatus.Invalid, errors, []);
}
