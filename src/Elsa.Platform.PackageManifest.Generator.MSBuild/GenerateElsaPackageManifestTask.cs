using Elsa.Platform.PackageManifest.Generator.Core.Generation;
using Elsa.Platform.PackageManifest.Generator.Core.Validation;
using Elsa.Platform.PackageManifest.Generator.MSBuild.Packaging;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Elsa.Platform.PackageManifest.Generator.MSBuild;

public sealed class GenerateElsaPackageManifestTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string AssemblyPath { get; set; } = "";
    public string? XmlDocumentationPath { get; set; }
    [Required]
    public string OutputPath { get; set; } = "";
    public string? OverrideFile { get; set; }
    public string? PackageId { get; set; }
    public string? Version { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Authors { get; set; }
    public string? RepositoryUrl { get; set; }
    public string? PackageProjectUrl { get; set; }
    public string? PackageTags { get; set; }
    public string? PackageLicenseExpression { get; set; }
    public string? PackageReadmeFile { get; set; }
    public string? TargetFramework { get; set; }
    public string? TargetFrameworks { get; set; }
    public string? ValidationSeverity { get; set; }
    public string? Strict { get; set; }
    public string? FailOnWarnings { get; set; }
    public string? AllowTargetFrameworkDifferences { get; set; }
    public string? DiagnosticsVerbosity { get; set; }
    public string? AdditionalFeatureInterfaceTypes { get; set; }
    public string[] ReferenceAssemblyPaths { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            var options = MsBuildGeneratorOptionsMapper.MapOptions(
                OutputPath,
                OverrideFile,
                ValidationSeverity,
                Strict,
                FailOnWarnings,
                AllowTargetFrameworkDifferences,
                DiagnosticsVerbosity,
                AdditionalFeatureInterfaceTypes);

            var metadata = MsBuildGeneratorOptionsMapper.MapPackageMetadata(
                string.IsNullOrWhiteSpace(PackageId) ? Path.GetFileNameWithoutExtension(AssemblyPath) : PackageId,
                string.IsNullOrWhiteSpace(Version) ? "1.0.0" : Version,
                Title,
                Description,
                Authors,
                RepositoryUrl,
                PackageProjectUrl,
                PackageTags,
                PackageLicenseExpression,
                PackageReadmeFile,
                TargetFramework,
                TargetFrameworks);

            var assemblyInput = new AssemblyInspectionInput(
                AssemblyPath,
                XmlDocumentationPath,
                TargetFramework,
                ReferenceAssemblyPaths,
                true);

            var diagnostics = new GenerationDiagnostics();
            var generator = new ManifestGenerator();
            generator.Generate(options, metadata, assemblyInput, diagnostics);

            var policy = new ValidationSeverityPolicy(options.ValidationSeverity, options.FailOnWarnings);
            var mappedDiagnostics = diagnostics.Items.Select(policy.MapLoggedSeverity).ToArray();
            foreach (var diagnostic in mappedDiagnostics)
                LogDiagnostic(diagnostic);

            return !policy.ShouldFail(diagnostics) && !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
    }

    private void LogDiagnostic(GenerationDiagnostic diagnostic)
    {
        var message = GenerationDiagnosticFormatter.Format(diagnostic);
        switch (diagnostic.Severity)
        {
            case GenerationDiagnosticSeverity.Error:
                Log.LogError(message);
                break;
            case GenerationDiagnosticSeverity.Warning:
                Log.LogWarning(message);
                break;
            default:
                Log.LogMessage(MessageImportance.Low, message);
                break;
        }
    }
}
