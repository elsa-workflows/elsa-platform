using Elsa.Platform.PackageCatalog.Core.Manifests;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Core.Compatibility;
using FluentAssertions;

namespace Elsa.Platform.PackageCatalog.Core.Tests;

public sealed class ManifestIngestionServiceTests
{
    [Fact]
    public void Preserves_unknown_feature_and_setting_extension_data()
    {
        var packageVersion = new PackageVersion();
        var manifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0" },
          "displayName": "Email",
          "features": [
            {
              "id": "email",
              "typeName": "Elsa.Email.EmailFeature",
              "displayName": "Email",
              "x-feature": "future",
              "settings": [
                {
                  "name": "smtpHost",
                  "jsonType": "string",
                  "displayName": "SMTP host",
                  "x-setting": 42
                }
              ]
            }
          ]
        }
        """;

        new ManifestIngestionService().Ingest(packageVersion, manifestJson);

        packageVersion.Features.Should().ContainSingle();
        packageVersion.Features[0].ExtensionsJson.Should().Contain("x-feature");
        packageVersion.Features[0].Settings.Should().ContainSingle();
        packageVersion.Features[0].Settings[0].ExtensionsJson.Should().Contain("x-setting");
    }

    [Fact]
    public void Projects_feature_infrastructure_requirements()
    {
        var packageVersion = new PackageVersion();
        var manifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.RabbitMq", "version": "1.0.0" },
          "displayName": "RabbitMQ",
          "features": [
            {
              "id": "rabbitmq-messaging",
              "typeName": "Elsa.RabbitMq.RabbitMqFeature",
              "displayName": "RabbitMQ Messaging",
              "infrastructure": [
                {
                  "id": "message-broker",
                  "kind": "message-broker",
                  "providers": ["rabbitmq"],
                  "configurationKeys": ["RabbitMq:ConnectionString"]
                }
              ]
            }
          ]
        }
        """;

        new ManifestIngestionService().Ingest(packageVersion, manifestJson);

        packageVersion.Features.Should().ContainSingle();
        packageVersion.Features[0].InfrastructureJson.Should().Contain("message-broker");
        packageVersion.Features[0].InfrastructureJson.Should().Contain("RabbitMq:ConnectionString");
    }

    [Fact]
    public void Resolves_package_runtime_kind_defaults_and_feature_overrides()
    {
        var packageVersion = new PackageVersion();
        var manifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Mixed", "version": "1.0.0" },
          "displayName": "Mixed",
          "compatibility": { "runtimeKinds": [ "elsa.server", "acme.custom-host" ] },
          "features": [
            {
              "id": "server",
              "typeName": "Elsa.Mixed.ServerFeature",
              "displayName": "Server"
            },
            {
              "id": "studio",
              "typeName": "Elsa.Mixed.StudioFeature",
              "displayName": "Studio",
              "compatibility": { "runtimeKinds": [ "elsa.studio" ] }
            }
          ]
        }
        """;

        var ingested = new ManifestIngestionService().Ingest(packageVersion, manifestJson);
        var packageRuntimeKinds = RuntimeKindCompatibilityPolicy.ResolvePackageRuntimeKinds(ingested.Manifest);
        var inherited = RuntimeKindCompatibilityPolicy.ResolveFeatureRuntimeKinds(ingested.Manifest.Features[0], packageRuntimeKinds);
        var overridden = RuntimeKindCompatibilityPolicy.ResolveFeatureRuntimeKinds(ingested.Manifest.Features[1], packageRuntimeKinds);

        packageRuntimeKinds.Should().BeEquivalentTo("elsa.server", "acme.custom-host");
        inherited.Should().BeEquivalentTo("elsa.server", "acme.custom-host");
        overridden.Should().BeEquivalentTo("elsa.studio");
    }

    [Fact]
    public void Leaves_undeclared_runtime_kinds_empty()
    {
        var packageVersion = new PackageVersion();
        var manifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0" },
          "displayName": "Email",
          "features": [
            {
              "id": "email",
              "typeName": "Elsa.Email.EmailFeature",
              "displayName": "Email"
            }
          ]
        }
        """;

        var ingested = new ManifestIngestionService().Ingest(packageVersion, manifestJson);
        var packageRuntimeKinds = RuntimeKindCompatibilityPolicy.ResolvePackageRuntimeKinds(ingested.Manifest);
        var featureRuntimeKinds = RuntimeKindCompatibilityPolicy.ResolveFeatureRuntimeKinds(ingested.Manifest.Features[0], packageRuntimeKinds);

        packageRuntimeKinds.Should().BeEmpty();
        featureRuntimeKinds.Should().BeEmpty();
        RuntimeKindCompatibilityPolicy.IsCompatibleWith(featureRuntimeKinds, "elsa.studio").Should().BeFalse();
    }
}
