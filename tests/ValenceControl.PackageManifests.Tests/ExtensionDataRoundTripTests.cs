using System.Text.Json;
using ValenceControl.PackageManifests;
using FluentAssertions;

namespace ValenceControl.PackageManifests.Tests;

public sealed class ExtensionDataRoundTripTests
{
    [Fact]
    public void Unknown_fields_round_trip_at_package_feature_and_setting_levels()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0", "x-package": true },
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
          ],
          "x-root": { "enabled": true }
        }
        """;

        var manifest = JsonSerializer.Deserialize<ElsaPackageManifest>(json, ManifestJsonSerializerOptions.Default)!;
        var roundTrip = JsonSerializer.Deserialize<ElsaPackageManifest>(JsonSerializer.Serialize(manifest, ManifestJsonSerializerOptions.Default), ManifestJsonSerializerOptions.Default)!;

        roundTrip.ExtensionData.Should().ContainKey("x-root");
        roundTrip.Package.ExtensionData.Should().ContainKey("x-package");
        roundTrip.Features[0].ExtensionData.Should().ContainKey("x-feature");
        roundTrip.Features[0].Settings[0].ExtensionData.Should().ContainKey("x-setting");
    }
}
