using ValenceControl.PackageManifests.Validation;

namespace ValenceControl.PackageManifests.Tests;

public sealed class ManifestRuntimeKindCompatibilityTests
{
    private readonly ManifestValidator _validator = new();

    [Fact]
    public void Validate_accepts_package_and_feature_runtime_kinds()
    {
        var result = _validator.Validate("""
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Mixed", "version": "1.0.0" },
          "displayName": "Mixed",
          "compatibility": { "runtimeKinds": [ "elsa.server", "elsa.studio", "acme.custom-host" ] },
          "features": [
            {
              "id": "server",
              "typeName": "Elsa.Mixed.ServerFeature",
              "displayName": "Server",
              "compatibility": { "runtimeKinds": [ "elsa.server" ] }
            }
          ]
        }
        """);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("elsa server", "runtimeKind.invalid")]
    [InlineData(" elsa.server", "runtimeKind.invalid")]
    [InlineData("", "runtimeKind.invalid")]
    public void Validate_rejects_malformed_runtime_kinds(string runtimeKind, string ruleId)
    {
        var result = _validator.Validate($$"""
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0" },
          "displayName": "Email",
          "compatibility": { "runtimeKinds": [ {{JsonString(runtimeKind)}} ] }
        }
        """);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.RuleId == ruleId);
    }

    [Fact]
    public void Validate_rejects_duplicate_runtime_kinds_case_insensitively()
    {
        var result = _validator.Validate("""
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0" },
          "displayName": "Email",
          "compatibility": { "runtimeKinds": [ "elsa.server", "ELSA.SERVER" ] }
        }
        """);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.RuleId == "runtimeKind.duplicate");
    }

    private static string JsonString(string value) => System.Text.Json.JsonSerializer.Serialize(value);
}
