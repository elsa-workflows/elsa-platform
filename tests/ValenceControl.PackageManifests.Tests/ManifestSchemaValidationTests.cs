using ValenceControl.PackageManifests.Validation;

namespace ValenceControl.PackageManifests.Tests;

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

        Assert.True(result.IsValid);
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

        Assert.Equal(ManifestValidationStatus.UnsupportedSchema, result.Status);
    }

    [Fact]
    public void Validate_rejects_oversized_manifest()
    {
        var json = new string(' ', ManifestValidator.MaxManifestBytes + 1);

        var result = _validator.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.RuleId == "manifest.size");
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

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.RuleId == "package.required");
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

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.RuleId == "features.invalid");
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

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_accepts_feature_categories()
    {
        var result = _validator.Validate("""
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Persistence", "version": "1.0.0" },
          "displayName": "Persistence",
          "features": [
            {
              "id": "Elsa.Persistence.EntityFrameworkCore",
              "typeName": "Elsa.Persistence.EntityFrameworkCoreFeature",
              "displayName": "Entity Framework Core",
              "categories": ["Persistence", "Data"]
            }
          ]
        }
        """);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_rejects_invalid_feature_categories()
    {
        var result = _validator.Validate("""
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Persistence", "version": "1.0.0" },
          "displayName": "Persistence",
          "features": [
            {
              "id": "Elsa.Persistence.EntityFrameworkCore",
              "typeName": "Elsa.Persistence.EntityFrameworkCoreFeature",
              "displayName": "Entity Framework Core",
              "categories": ["Persistence", "", "persistence"]
            }
          ]
        }
        """);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.RuleId == "feature.category.required");
        Assert.Contains(result.Errors, x => x.RuleId == "feature.category.duplicate");
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

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.RuleId == "infrastructure.id.required");
        Assert.Contains(result.Errors, x => x.RuleId == "infrastructure.kind.required");
    }
}
