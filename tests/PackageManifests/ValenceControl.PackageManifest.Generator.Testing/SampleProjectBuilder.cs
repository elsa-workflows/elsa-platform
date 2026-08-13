using System.Diagnostics;

namespace ValenceControl.PackageManifest.Generator.Testing;

public sealed class SampleProjectBuilder : IAsyncDisposable
{
    private readonly List<string> _sources = [];
    private bool _useExternalCShellsReference;
    private bool _useExternalCShellsPackageReference;
    private bool _useLocalGeneratorPackage;
    private readonly Dictionary<string, string> _properties = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TargetFramework"] = "net10.0",
        ["PackageId"] = "Sample.Elsa.Package",
        ["Version"] = "1.2.3",
        ["Description"] = "Sample package for generator tests.",
        ["GenerateDocumentationFile"] = "true"
    };

    public SampleProjectBuilder()
    {
        ProjectDirectory = Path.Combine(Path.GetTempPath(), "elsa-manifest-generator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ProjectDirectory);
    }

    public string ProjectDirectory { get; }

    public string ProjectFile => Path.Combine(ProjectDirectory, "Sample.Elsa.Package.csproj");

    public string CShellsProjectFile => Path.Combine(ProjectDirectory, "CShells.Abstractions", "CShells.Abstractions.csproj");

    // The CShells package source lives outside the sample project directory so its sources are not
    // globbed into the sample assembly; only the produced package is consumed via a local feed.
    private string CShellsPackageSourceDirectory => ProjectDirectory + "-cshells";

    private string CShellsFeedDirectory => Path.Combine(ProjectDirectory, ".cshells-feed");

    public string TargetFramework => _properties.TryGetValue("TargetFramework", out var targetFramework)
        ? targetFramework
        : _properties["TargetFrameworks"].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];

    public string PackagePath => Path.Combine(ProjectDirectory, "bin", "Debug", $"{_properties["PackageId"]}.{_properties["Version"]}.nupkg");

    public string ReleasePackagePath => PackagePathForConfiguration("Release");

    public string AssemblyPath => Path.Combine(ProjectDirectory, "bin", "Debug", TargetFramework, "Sample.Elsa.Package.dll");

    public string XmlDocumentationPath => Path.ChangeExtension(AssemblyPath, ".xml");

    public string ManifestPath => ManifestPathForConfiguration("Debug", TargetFramework);

    public SampleProjectBuilder WithProperty(string name, string value)
    {
        if (string.Equals(name, "TargetFrameworks", StringComparison.OrdinalIgnoreCase))
            _properties.Remove("TargetFramework");
        else if (string.Equals(name, "TargetFramework", StringComparison.OrdinalIgnoreCase))
            _properties.Remove("TargetFrameworks");

        _properties[name] = value;
        return this;
    }

    public SampleProjectBuilder WithTargetFrameworks(params string[] targetFrameworks) =>
        WithProperty("TargetFrameworks", string.Join(';', targetFrameworks));

    public SampleProjectBuilder WithExternalCShellsReference()
    {
        _useExternalCShellsReference = true;
        return this;
    }

    /// <summary>
    /// References CShells.Abstractions as a NuGet <c>PackageReference</c> (built into a local feed) rather than a
    /// project reference. This reproduces real consumer packages where the referenced assembly is not copied to the
    /// output directory and is only resolvable through the restore graph.
    /// </summary>
    public SampleProjectBuilder WithExternalCShellsPackageReference()
    {
        _useExternalCShellsPackageReference = true;
        return this;
    }

    public SampleProjectBuilder WithLocalGeneratorPackage()
    {
        _useLocalGeneratorPackage = true;
        return this;
    }

    public SampleProjectBuilder WithSource(string source)
    {
        _sources.Add(source);
        return this;
    }

    public string AssemblyPathForConfiguration(string configuration, string targetFramework) =>
        Path.Combine(ProjectDirectory, "bin", configuration, targetFramework, "Sample.Elsa.Package.dll");

    public string ManifestPathForConfiguration(string configuration, string targetFramework) =>
        Path.Combine(ProjectDirectory, "obj", configuration, targetFramework, "elsa-package.json");

    public string PackagePathForConfiguration(string configuration) =>
        Path.Combine(ProjectDirectory, "bin", configuration, $"{_properties["PackageId"]}.{_properties["Version"]}.nupkg");

    public async Task<CommandResult> BuildAsync(string configuration = "Debug", CancellationToken cancellationToken = default)
    {
        await PrepareAsync(cancellationToken);
        return await RunDotNetAsync(["build", ProjectFile, "--nologo", "--configuration", configuration], cancellationToken);
    }

    public async Task<CommandResult> PackAsync(string configuration = "Debug", CancellationToken cancellationToken = default)
    {
        await PrepareAsync(cancellationToken);
        return await RunDotNetAsync(["pack", ProjectFile, "--nologo", "--configuration", configuration, "--no-build"], cancellationToken);
    }

    public async Task<CommandResult> PackWithBuildAsync(string configuration = "Debug", CancellationToken cancellationToken = default)
    {
        await PrepareAsync(cancellationToken);
        return await RunDotNetAsync(["pack", ProjectFile, "--nologo", "--configuration", configuration], cancellationToken);
    }

    private async Task PrepareAsync(CancellationToken cancellationToken)
    {
        WriteProject();
        await EnsureExternalCShellsPackageAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(ProjectDirectory))
            Directory.Delete(ProjectDirectory, true);
        if (Directory.Exists(CShellsPackageSourceDirectory))
            Directory.Delete(CShellsPackageSourceDirectory, true);
        return ValueTask.CompletedTask;
    }

    private void WriteProject()
    {
        if (_useLocalGeneratorPackage)
            WriteLocalGeneratorPackage();

        if (_useExternalCShellsReference)
            WriteCShellsProject();

        File.WriteAllText(ProjectFile, RenderProject());
        if (!_useExternalCShellsReference && !_useExternalCShellsPackageReference)
            File.WriteAllText(Path.Combine(ProjectDirectory, "CShells.cs"), CShellsFeatureFixtures.AbstractionsSource);

        File.WriteAllText(Path.Combine(ProjectDirectory, "ManifestHints.cs"), CShellsFeatureFixtures.ManifestHintsSource);

        for (var i = 0; i < _sources.Count; i++)
            File.WriteAllText(Path.Combine(ProjectDirectory, $"Feature{i}.cs"), _sources[i]);
    }

    private string RenderProject()
    {
        var properties = string.Join(Environment.NewLine, _properties.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"    <{x.Key}>{x.Value}</{x.Key}>"));
        var propsImport = _useLocalGeneratorPackage
            ? """  <Import Project=".generator/build/ValenceControl.PackageManifest.Generator.props" />"""
            : "";
        var targetsImport = _useLocalGeneratorPackage
            ? """  <Import Project=".generator/build/ValenceControl.PackageManifest.Generator.targets" />"""
            : "";
        var projectReferences = _useExternalCShellsReference
            ? """

  <ItemGroup>
    <ProjectReference Include="CShells.Abstractions/CShells.Abstractions.csproj" />
  </ItemGroup>
"""
            : "";

        var packageReferences = _useExternalCShellsPackageReference
            ? """

  <ItemGroup>
    <PackageReference Include="CShells.Abstractions" Version="1.0.0" />
  </ItemGroup>
"""
            : "";

        return $"""
<Project Sdk="Microsoft.NET.Sdk">
{propsImport}
  <PropertyGroup>
{properties}
  </PropertyGroup>
{projectReferences}{packageReferences}
{targetsImport}
</Project>
""";
    }

    private async Task EnsureExternalCShellsPackageAsync(CancellationToken cancellationToken)
    {
        if (!_useExternalCShellsPackageReference)
            return;

        Directory.CreateDirectory(CShellsPackageSourceDirectory);
        File.WriteAllText(Path.Combine(CShellsPackageSourceDirectory, "CShells.Abstractions.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>CShells.Abstractions</AssemblyName>
    <PackageId>CShells.Abstractions</PackageId>
    <Version>1.0.0</Version>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
""");
        File.WriteAllText(Path.Combine(CShellsPackageSourceDirectory, "CShells.cs"), CShellsFeatureFixtures.AbstractionsSource);

        Directory.CreateDirectory(CShellsFeedDirectory);
        var pack = await RunDotNetAsync(["pack", Path.Combine(CShellsPackageSourceDirectory, "CShells.Abstractions.csproj"), "--nologo", "--output", CShellsFeedDirectory], cancellationToken);
        if (pack.ExitCode != 0)
            throw new InvalidOperationException($"Failed to build the CShells.Abstractions test package.{Environment.NewLine}{pack.CombinedOutput}");

        // Restrict the sample project to the local feed for CShells.Abstractions so the machine-wide package source
        // mapping does not shadow it, while still allowing framework/reference packages to restore from nuget.org.
        File.WriteAllText(Path.Combine(ProjectDirectory, "nuget.config"), """
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value=".cshells-feed" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="local">
      <package pattern="CShells.Abstractions" />
    </packageSource>
    <packageSource key="nuget">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
""");
    }

    private void WriteCShellsProject()
    {
        var directory = Path.GetDirectoryName(CShellsProjectFile)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(CShellsProjectFile, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>CShells.Abstractions</AssemblyName>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
""");
        File.WriteAllText(Path.Combine(directory, "CShells.cs"), CShellsFeatureFixtures.AbstractionsSource);
    }

    private void WriteLocalGeneratorPackage()
    {
        var packageDirectory = Path.Combine(ProjectDirectory, ".generator");
        var buildDirectory = Path.Combine(packageDirectory, "build");
        var tasksDirectory = Path.Combine(packageDirectory, "tasks");
        Directory.CreateDirectory(buildDirectory);
        Directory.CreateDirectory(tasksDirectory);

        File.Copy(FindRepositoryFile("src/ValenceControl.PackageManifest.Generator/build/ValenceControl.PackageManifest.Generator.props"), Path.Combine(buildDirectory, "ValenceControl.PackageManifest.Generator.props"), true);
        File.Copy(FindRepositoryFile("src/ValenceControl.PackageManifest.Generator/build/ValenceControl.PackageManifest.Generator.targets"), Path.Combine(buildDirectory, "ValenceControl.PackageManifest.Generator.targets"), true);

        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.*", SearchOption.TopDirectoryOnly)
                     .Where(IsGeneratorTaskAsset))
        {
            File.Copy(file, Path.Combine(tasksDirectory, Path.GetFileName(file)), true);
        }
    }

    private static bool IsGeneratorTaskAsset(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) &&
               Path.GetFileName(path).StartsWith("ValenceControl.PackageManifest.Generator.", StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }

    private static async Task<CommandResult> RunDotNetAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        return new CommandResult(process.ExitCode, output, error);
    }
}

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string CombinedOutput => StandardOutput + StandardError;
}
