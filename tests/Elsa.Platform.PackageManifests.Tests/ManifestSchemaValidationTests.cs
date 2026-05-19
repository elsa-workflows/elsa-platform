using Elsa.Platform.PackageManifests.Validation;
using FluentAssertions;

namespace Elsa.Platform.PackageManifests.Tests;

public sealed class ManifestSchemaValidationTests
{
    private readonly ManifestValidator _validator = new();

    [Fact]
    public void Validate_accepts_minimal_supported_manifest()
    {
        var result = _validator.Validate("""
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0" },
          "displayName": "Email"
        }
        """);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_rejects_unsupported_schema()
    {
        var result = _validator.Validate("""
        {
          "schemaVersion": "99.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0" },
          "displayName": "Email"
        }
        """);

        result.Status.Should().Be(ManifestValidationStatus.UnsupportedSchema);
    }

    [Fact]
    public void Validate_rejects_oversized_manifest()
    {
        var json = new string(' ', ManifestValidator.MaxManifestBytes + 1);

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.RuleId == "manifest.size");
    }

    [Fact]
    public void Validate_rejects_null_package_without_throwing()
    {
        var result = _validator.Validate("""
        {
          "schemaVersion": "1.0",
          "package": null,
          "displayName": "Email"
        }
        """);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.RuleId == "package.required");
    }

    [Fact]
    public void Validate_rejects_null_features_without_throwing()
    {
        var result = _validator.Validate("""
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0" },
          "displayName": "Email",
          "features": null
        }
        """);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.RuleId == "features.invalid");
    }

    [Fact]
    public void Validate_accepts_feature_infrastructure_requirements()
    {
        var result = _validator.Validate("""
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.RabbitMq", "version": "1.0.0" },
          "displayName": "RabbitMQ",
          "features": [
            {
              "id": "Elsa.RabbitMq.Messaging",
              "typeName": "Elsa.RabbitMq.RabbitMqFeature",
              "displayName": "RabbitMQ Messaging",
              "infrastructure": [
                {
                  "id": "message-broker",
                  "kind": "message-broker",
                  "providers": ["rabbitmq", "azure-service-bus"],
                  "configurationKeys": ["RabbitMq:ConnectionString"]
                }
              ]
            }
          ]
        }
        """);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_rejects_infrastructure_requirements_without_id_or_kind()
    {
        var result = _validator.Validate("""
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.RabbitMq", "version": "1.0.0" },
          "displayName": "RabbitMQ",
          "features": [
            {
              "id": "Elsa.RabbitMq.Messaging",
              "typeName": "Elsa.RabbitMq.RabbitMqFeature",
              "displayName": "RabbitMQ Messaging",
              "infrastructure": [
                { "id": "", "kind": "" }
              ]
            }
          ]
        }
        """);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.RuleId == "infrastructure.id.required");
        result.Errors.Should().Contain(x => x.RuleId == "infrastructure.kind.required");
    }
}
