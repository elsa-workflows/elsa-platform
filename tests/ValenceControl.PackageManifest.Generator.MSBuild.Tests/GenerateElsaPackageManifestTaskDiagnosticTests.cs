using System.Collections;
using ValenceControl.PackageManifest.Generator.MSBuild;
using ValenceControl.PackageManifest.Generator.Testing;
using FluentAssertions;
using Microsoft.Build.Framework;

namespace ValenceControl.PackageManifest.Generator.MSBuild.Tests;

public sealed class GenerateElsaPackageManifestTaskDiagnosticTests
{
    [Fact]
    public async Task Execute_succeeds_with_warning_diagnostics_when_fail_on_warnings_is_false()
    {
        await using var project = await BuildWarningProjectAsync();
        var buildEngine = new CapturingBuildEngine();

        var result = CreateTask(project, buildEngine, failOnWarnings: false).Execute();

        result.Should().BeTrue();
        buildEngine.Errors.Should().BeEmpty();
        buildEngine.Warnings.Should().NotBeEmpty();
        buildEngine.Errors.Should().NotContain(x => x.Message != null && x.Message.Contains("MSB4181", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Execute_fails_with_warning_diagnostics_when_fail_on_warnings_is_true()
    {
        await using var project = await BuildWarningProjectAsync();
        var buildEngine = new CapturingBuildEngine();

        var result = CreateTask(project, buildEngine, failOnWarnings: true).Execute();

        result.Should().BeFalse();
        buildEngine.Errors.Should().BeEmpty();
        buildEngine.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Execute_succeeds_with_unsupported_setting_when_fail_on_warnings_is_true()
    {
        await using var project = await BuildUnsupportedSettingProjectAsync();
        var buildEngine = new CapturingBuildEngine();

        var result = CreateTask(project, buildEngine, failOnWarnings: true).Execute();

        result.Should().BeTrue();
        buildEngine.Errors.Should().BeEmpty();
        buildEngine.Warnings.Should().BeEmpty();
        buildEngine.Messages.Should().Contain(x => x.Message != null && x.Message.Contains("EPMGEN_SETTING_TYPE_UNSUPPORTED", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_fails_infrastructure_errors_even_when_validation_severity_is_warning()
    {
        var buildEngine = new CapturingBuildEngine();
        var task = new GenerateElsaPackageManifestTask
        {
            BuildEngine = buildEngine,
            AssemblyPath = Path.Combine(Path.GetTempPath(), "missing-assembly.dll"),
            OutputPath = Path.Combine(Path.GetTempPath(), "missing-assembly", "elsa-package.json"),
            ValidationSeverity = "Warning",
            FailOnWarnings = "false"
        };

        task.Execute().Should().BeFalse();
        buildEngine.Errors.Should().NotBeEmpty();
    }

    private static GenerateElsaPackageManifestTask CreateTask(SampleProjectBuilder project, CapturingBuildEngine buildEngine, bool failOnWarnings) =>
        new()
        {
            BuildEngine = buildEngine,
            AssemblyPath = project.AssemblyPath,
            XmlDocumentationPath = project.XmlDocumentationPath,
            OutputPath = Path.Combine(project.ProjectDirectory, "obj", "elsa-package.json"),
            PackageId = "Sample.Elsa.Package",
            Version = "1.2.3",
            Description = "Sample package.",
            TargetFramework = "net10.0",
            ValidationSeverity = "Warning",
            FailOnWarnings = failOnWarnings.ToString()
        };

    private static async Task<SampleProjectBuilder> BuildWarningProjectAsync()
    {
        var project = new SampleProjectBuilder().WithSource("""
using CShells.Features;

namespace Sample.Features;

[ShellFeature("Warnings")]
public sealed class WarningFeature : IShellFeature
{
    public string? Value { get; set; }
}
""");
        var build = await project.BuildAsync();
        build.ExitCode.Should().Be(0, build.CombinedOutput);
        return project;
    }

    private static async Task<SampleProjectBuilder> BuildUnsupportedSettingProjectAsync()
    {
        var project = new SampleProjectBuilder().WithSource("""
#nullable enable
using System;
using CShells.Features;

namespace Sample.Features;

[ShellFeature("Identity", Description = "Identity feature.")]
public sealed class IdentityFeature : IShellFeature
{
    public string Name { get; set; } = "";

    public Type? ApiKeyProviderType { get; set; }
}
""");
        var build = await project.BuildAsync();
        build.ExitCode.Should().Be(0, build.CombinedOutput);
        return project;
    }

    private sealed class CapturingBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public List<BuildWarningEventArgs> Warnings { get; } = [];
        public List<BuildMessageEventArgs> Messages { get; } = [];

        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "";

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            IDictionary globalProperties,
            IDictionary targetOutputs) => true;

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);
        public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e);
        public void LogCustomEvent(CustomBuildEventArgs e) { }
    }
}
