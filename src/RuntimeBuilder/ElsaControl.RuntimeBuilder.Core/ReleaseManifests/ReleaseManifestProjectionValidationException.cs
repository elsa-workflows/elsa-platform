namespace ElsaControl.RuntimeBuilder.Core.ReleaseManifests;

/// <summary>
/// Indicates that a previously admitted manifest is structurally incomplete for
/// projection. This is intentionally distinct from unexpected programmer failures so
/// callers can fail closed without masking defects.
/// </summary>
public sealed class ReleaseManifestProjectionValidationException(string message) : InvalidOperationException(message);
