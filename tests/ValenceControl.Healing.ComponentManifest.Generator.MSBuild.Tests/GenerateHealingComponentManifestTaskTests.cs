using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using ValenceControl.Healing.ComponentManifest;
using ValenceControl.Healing.ComponentManifest.Generator.MSBuild;
using Microsoft.Build.Framework;

namespace ValenceControl.Healing.ComponentManifest.Generator.MSBuild.Tests;

public sealed class GenerateHealingComponentManifestTaskTests
{
    [Fact]
    public void Execute_reads_resolved_assets_hashes_artifacts_and_emits_dependency_graph_without_local_paths()
    {
        using var fixture = AssetsFixture.Create();
        var engine = new CapturingBuildEngine();
        var task = fixture.CreateTask(engine);

        var result = task.Execute();

        Assert.True(result, string.Join(Environment.NewLine, engine.Errors.Select(x => x.Message)));
        var json = File.ReadAllText(fixture.OutputPath);
        var manifest = ComponentManifestSerializer.Deserialize(json);
        Assert.Contains(manifest.Components, x => x.Key == "application:Acme.WorkflowHost:2.4.1");
        Assert.DoesNotContain(manifest.Components, x => x.Key == "nuget:Build.Tools:9.9.9");
        var alpha = manifest.Components.Single(x => x.Key == "nuget:Acme.Alpha:1.2.3");
        Assert.True(alpha.DirectDependency);
        Assert.Equal("https://github.com/acme/alpha", alpha.RepositoryUrl);
        Assert.Equal(new string('b', 40), alpha.RepositoryCommit);
        Assert.Equal(["nuget:Acme.Beta:4.5.6"], alpha.Dependencies);
        Assert.Single(alpha.Assemblies, x => x.RelativePath == "lib/net10.0/Acme.Alpha.dll");
        Assert.Matches("^sha256:[0-9a-f]{64}$", alpha.ContentHash);
        Assert.Matches("^sha256:[0-9a-f]{64}$", alpha.Assemblies[0].ContentHash);
        Assert.DoesNotContain(fixture.Root, json);
        Assert.DoesNotContain(".nuget/packages", json);
        Assert.DoesNotContain("token-value", json);
    }

    [Fact]
    public void Execute_fails_closed_when_an_asset_path_escapes_its_package_root()
    {
        using var fixture = AssetsFixture.Create(escapingAsset: true);
        var engine = new CapturingBuildEngine();

        var result = fixture.CreateTask(engine).Execute();

        Assert.False(result);
        Assert.Contains(engine.Errors, x => x.Message != null && x.Message.Contains("unsafe", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(fixture.OutputPath));
    }

    [Fact]
    public void Execute_hashes_safe_extracted_package_contents_when_the_cache_does_not_retain_archives()
    {
        using var fixture = AssetsFixture.Create(omitPackageArchives: true);
        var engine = new CapturingBuildEngine();

        var result = fixture.CreateTask(engine).Execute();

        Assert.True(result, string.Join(Environment.NewLine, engine.Errors.Select(x => x.Message)));
        var manifest = ComponentManifestSerializer.Deserialize(File.ReadAllText(fixture.OutputPath));
        Assert.Matches("^sha256:[0-9a-f]{64}$", manifest.Components.Single(x => x.Key == "nuget:Acme.Alpha:1.2.3").ContentHash);
    }

    [Fact]
    public void Execute_package_content_hash_is_stable_whether_or_not_the_cache_retains_archives()
    {
        using var retainedArchive = AssetsFixture.Create();
        using var extractedOnly = AssetsFixture.Create(omitPackageArchives: true);
        Assert.True(retainedArchive.CreateTask(new CapturingBuildEngine()).Execute());
        Assert.True(extractedOnly.CreateTask(new CapturingBuildEngine()).Execute());

        var retainedManifest = ComponentManifestSerializer.Deserialize(File.ReadAllText(retainedArchive.OutputPath));
        var extractedManifest = ComponentManifestSerializer.Deserialize(File.ReadAllText(extractedOnly.OutputPath));

        Assert.Equal(
            extractedManifest.Components.Single(x => x.Key == "nuget:Acme.Alpha:1.2.3").ContentHash,
            retainedManifest.Components.Single(x => x.Key == "nuget:Acme.Alpha:1.2.3").ContentHash);
    }

    [Fact]
    public void Execute_rejects_an_assembly_symlink_that_resolves_outside_the_package_root()
    {
        using var fixture = AssetsFixture.Create();
        fixture.ReplaceAlphaAssemblyWithEscapingSymlink();
        var engine = new CapturingBuildEngine();

        var result = fixture.CreateTask(engine).Execute();

        Assert.False(result);
        Assert.Contains(engine.Errors, x => x.Message != null && x.Message.Contains("unsafe", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(fixture.OutputPath));
    }

    [Fact]
    public void Execute_rejects_malicious_repository_commit_metadata_from_a_nuspec()
    {
        using var fixture = AssetsFixture.Create(repositoryCommit: "$(HOME)/token?secret=value");
        var engine = new CapturingBuildEngine();

        var result = fixture.CreateTask(engine).Execute();

        Assert.False(result);
        Assert.Contains(engine.Errors, x => x.Message != null && x.Message.Contains("manifest.repository-commit.invalid", StringComparison.Ordinal));
        Assert.False(File.Exists(fixture.OutputPath));
    }

    [Fact]
    public void Execute_does_not_fall_back_to_an_arbitrary_runtime_target()
    {
        using var fixture = AssetsFixture.Create(assetsTarget: "net10.0/win-x64");
        var engine = new CapturingBuildEngine();

        var result = fixture.CreateTask(engine).Execute();

        Assert.False(result);
        Assert.Contains(engine.Errors, x => x.Message != null && x.Message.Contains("linux-x64", StringComparison.Ordinal));
        Assert.False(File.Exists(fixture.OutputPath));
    }

    [Fact]
    public void Package_project_declares_publishable_build_assets_and_task_runtime()
    {
        var projectRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(projectRoot, "src", "ValenceControl.Healing.ComponentManifest.Generator.MSBuild", "ValenceControl.Healing.ComponentManifest.Generator.MSBuild.csproj");
        var targetPath = Path.Combine(projectRoot, "src", "ValenceControl.Healing.ComponentManifest.Generator.MSBuild", "build", "ValenceControl.Healing.ComponentManifest.Generator.MSBuild.targets");
        var project = File.ReadAllText(projectPath);
        var targets = File.ReadAllText(targetPath);

        Assert.Contains("PackagePath=\"tasks/\"", project);
        Assert.Contains("PackagePath=\"build/\"", project);
        Assert.Contains("PackagePath=\"buildTransitive/\"", project);
        Assert.Contains("GenerateHealingComponentManifestTask", targets);
        Assert.Contains("ProjectAssetsFile=\"$(ProjectAssetsFile)\"", targets);
        Assert.Contains("DependsOnTargets=\"ResolvePackageAssets\"", targets);
        Assert.Contains("Delete Files=\"$(ValenceControlHealingComponentManifestOutputPath)\"", targets);
        Assert.Contains("AfterTargets=\"Clean\"", targets);
    }

    [Fact]
    public void Packed_package_consumer_clean_removes_a_manifest_generated_by_an_earlier_build()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), $"healing-manifest-consumer-{Guid.NewGuid():N}");
        var feed = Path.Combine(root, "feed");
        var consumer = Path.Combine(root, "consumer");
        var packages = Path.Combine(root, "packages");
        Directory.CreateDirectory(feed);
        Directory.CreateDirectory(consumer);
        try
        {
            var generatorProject = Path.Combine(repositoryRoot, "src", "ValenceControl.Healing.ComponentManifest.Generator.MSBuild", "ValenceControl.Healing.ComponentManifest.Generator.MSBuild.csproj");
            RunDotNet(repositoryRoot, "pack", generatorProject, "-c", "Release", "--no-build", "--no-restore", "-o", feed);
            File.WriteAllText(Path.Combine(consumer, "Consumer.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="ValenceControl.Healing.ComponentManifest.Generator.MSBuild" Version="0.0.1" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """);
            File.WriteAllText(Path.Combine(consumer, "Program.cs"), "Console.WriteLine(\"consumer\");");
            File.WriteAllText(Path.Combine(consumer, "NuGet.Config"), $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{{feed}}" />
              </packageSources>
            </configuration>
            """);

            RunDotNet(consumer, "restore", "Consumer.csproj", "--configfile", "NuGet.Config", "--packages", packages, "--no-cache", "--force");
            RunDotNet(consumer, "build", "Consumer.csproj", "-c", "Release", "--no-restore",
                "-p:ValenceControlHealingSourceRevision=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "-p:ValenceControlHealingManifestCreatedAt=2026-07-16T00:00:00Z");
            var manifest = Path.Combine(consumer, "bin", "Release", "net10.0", "valence-control-healing-component-manifest.json");
            Assert.True(File.Exists(manifest));

            RunDotNet(consumer, "clean", "Consumer.csproj", "-c", "Release");

            Assert.False(File.Exists(manifest));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ValenceControl.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static void RunDotNet(string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = standardOutput.GetAwaiter().GetResult() + standardError.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, output);
    }

    private sealed class AssetsFixture : IDisposable
    {
        private AssetsFixture(string root, string projectDirectory, string packageRoot, string outputPath, string assetsPath, string applicationPath)
        {
            Root = root;
            ProjectDirectory = projectDirectory;
            PackageRoot = packageRoot;
            OutputPath = outputPath;
            AssetsPath = assetsPath;
            ApplicationPath = applicationPath;
        }

        public string Root { get; }
        public string ProjectDirectory { get; }
        public string PackageRoot { get; }
        public string OutputPath { get; }
        public string AssetsPath { get; }
        public string ApplicationPath { get; }

        public static AssetsFixture Create(
            bool escapingAsset = false,
            bool omitPackageArchives = false,
            string assetsTarget = "net10.0",
            string? repositoryCommit = null)
        {
            var root = Path.Combine(Path.GetTempPath(), $"healing-manifest-{Guid.NewGuid():N}");
            var project = Path.Combine(root, "src", "Acme.WorkflowHost");
            var packageRoot = Path.Combine(root, ".nuget", "packages");
            var output = Path.Combine(project, "obj", "valence-control-healing-component-manifest.json");
            var application = Path.Combine(project, "bin", "Acme.WorkflowHost.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(application)!);
            File.Copy(typeof(GenerateHealingComponentManifestTaskTests).Assembly.Location, application);

            CreatePackage(packageRoot, "acme.alpha", "1.2.3", "Acme.Alpha.dll", omitPackageArchives ? null : "alpha-nupkg", repositoryCommit);
            CreatePackage(packageRoot, "acme.beta", "4.5.6", "Acme.Beta.dll", omitPackageArchives ? null : "beta-nupkg");
            CreatePackageArtifact(packageRoot, "build.tools", "9.9.9", "tools-nupkg");

            var alphaAsset = escapingAsset ? "../outside.dll" : "lib/net10.0/Acme.Alpha.dll";
            var assetsPath = Path.Combine(project, "obj", "project.assets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(assetsPath)!);
            var assets = $$"""
            {
              "version": 3,
              "targets": {
                "{{assetsTarget}}": {
                  "Acme.Alpha/1.2.3": {
                    "type": "package",
                    "dependencies": { "Acme.Beta": "4.5.6" },
                    "compile": { "{{alphaAsset}}": {} }
                  },
                  "Acme.Beta/4.5.6": {
                    "type": "package",
                    "compile": { "lib/net10.0/Acme.Beta.dll": {} }
                  },
                  "Build.Tools/9.9.9": {
                    "type": "package",
                    "build": { "build/Build.Tools.targets": {} }
                  }
                }
              },
              "libraries": {
                "Acme.Alpha/1.2.3": { "type": "package", "path": "acme.alpha/1.2.3" },
                "Acme.Beta/4.5.6": { "type": "package", "path": "acme.beta/4.5.6" },
                "Build.Tools/9.9.9": { "type": "package", "path": "build.tools/9.9.9" }
              },
              "project": {
                "frameworks": {
                  "net10.0": {
                    "dependencies": {
                      "Acme.Alpha": { "target": "Package", "version": "[1.2.3, )" },
                      "Build.Tools": { "target": "Package", "version": "[9.9.9, )", "suppressParent": "All" }
                    }
                  }
                },
                "restore": { "packagesPath": "{{JsonEncodedText.Encode(packageRoot)}}", "configFilePaths": ["/Users/alice/.nuget/NuGet.Config?token=token-value"] }
              },
              "packageFolders": { "{{JsonEncodedText.Encode(packageRoot + Path.DirectorySeparatorChar)}}": {} }
            }
            """;
            File.WriteAllText(assetsPath, assets);
            return new AssetsFixture(root, project, packageRoot, output, assetsPath, application);
        }

        public GenerateHealingComponentManifestTask CreateTask(IBuildEngine engine) => new()
        {
            BuildEngine = engine,
            ProjectAssetsFile = AssetsPath,
            ProjectDirectory = ProjectDirectory,
            ApplicationAssemblyPath = ApplicationPath,
            OutputPath = OutputPath,
            ApplicationName = "Acme.WorkflowHost",
            ApplicationVersion = "2.4.1",
            TargetFramework = "net10.0",
            RuntimeIdentifier = "linux-x64",
            SourceRevision = new string('a', 40),
            RepositoryUrl = "https://github.com/acme/workflow-host.git",
            BuildId = "build-42",
            CreatedAt = "2026-07-16T00:00:00Z",
            RequireSourceRevision = true
        };

        public void ReplaceAlphaAssemblyWithEscapingSymlink()
        {
            var assembly = Path.Combine(PackageRoot, "acme.alpha", "1.2.3", "lib", "net10.0", "Acme.Alpha.dll");
            var outside = Path.Combine(Root, "outside-alpha.dll");
            File.Copy(typeof(GenerateHealingComponentManifestTaskTests).Assembly.Location, outside);
            File.Delete(assembly);
            File.CreateSymbolicLink(assembly, outside);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private static void CreatePackage(
            string packageRoot,
            string id,
            string version,
            string assemblyName,
            string? packageContent,
            string? repositoryCommit = null)
        {
            var directory = Path.Combine(packageRoot, id, version);
            var library = Path.Combine(directory, "lib", "net10.0", assemblyName);
            Directory.CreateDirectory(Path.GetDirectoryName(library)!);
            File.Copy(typeof(GenerateHealingComponentManifestTaskTests).Assembly.Location, library);
            var repository = id == "acme.alpha"
                ? $"<repository type=\"git\" url=\"https://github.com/acme/alpha.git\" commit=\"{repositoryCommit ?? new string('b', 40)}\" />"
                : "";
            File.WriteAllText(Path.Combine(directory, $"{id}.nuspec"), $"<package><metadata><id>{id}</id><version>{version}</version>{repository}</metadata></package>");
            if (packageContent is not null)
                File.WriteAllText(Path.Combine(directory, $"{id}.{version}.nupkg"), packageContent);
        }

        private static void CreatePackageArtifact(string packageRoot, string id, string version, string packageContent)
        {
            var directory = Path.Combine(packageRoot, id, version);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"{id}.{version}.nupkg"), packageContent);
        }
    }

    private sealed class CapturingBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "";
        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogCustomEvent(CustomBuildEventArgs e) { }
    }
}
