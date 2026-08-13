import { describe, expect, it } from "vitest";
import {
  compatibilityMatchesSearch,
  featureMatchesCategory,
  featureMatchesSearch,
  hasSuspiciousChange,
  isStaleVersionAction,
  normalizeFeature,
  parsePackageDetailsSection,
  selectedPackageDetailsVersion,
  validationFindingMatchesSearch,
  visibilityReasonGroups,
  visibilityReasonMatchesSearch,
  type CatalogPackage,
  type PackageDetails
} from "@/features/packages/packageModels";

const packageItem: CatalogPackage = {
  packageId: "Elsa.Test",
  approved: true,
  listed: true,
  latestVersion: "2.0.0",
  versions: [
    {
      version: "1.0.0",
      approvalStatus: "Approved",
      validationStatus: "Valid",
      isListed: true,
      suspiciousChangeDetected: true,
      schemaVersion: "1.0"
    },
    {
      version: "2.0.0",
      approvalStatus: "Approved",
      validationStatus: "Valid",
      isListed: true,
      suspiciousChangeDetected: false,
      schemaVersion: "1.0"
    }
  ]
};

describe("packageModels", () => {
  it("reports suspicious changes for the latest version only", () => {
    expect(hasSuspiciousChange(packageItem)).toBe(false);
    expect(hasSuspiciousChange({ ...packageItem, latestVersion: "1.0.0" })).toBe(true);
  });

  it("selects the latest indexed version when the route and latest package version are unavailable", () => {
    expect(selectedPackageDetailsVersion(detailsItem, "missing")?.version).toBe("2.0.0");
  });

  it("preserves canonical package casing from details payloads", () => {
    expect(detailsItem.packageId).toBe("Elsa.Test");
  });

  it("groups visibility reasons by category", () => {
    expect(Object.keys(visibilityReasonGroups(detailsItem.versions[0].visibilityReasons))).toEqual(["TrustDecision", "Validation"]);
  });

  it("falls back unknown route sections to summary", () => {
    expect(parsePackageDetailsSection("manifest")).toBe("manifest");
    expect(parsePackageDetailsSection("bogus")).toBe("summary");
  });

  it("filters compatibility metadata by framework, capability, note, and unsupported combination text", () => {
    expect(compatibilityMatchesSearch(detailsItem.versions[0].compatibility, "net10")).toBe(true);
    expect(compatibilityMatchesSearch(detailsItem.versions[0].compatibility, "persistence")).toBe(true);
    expect(compatibilityMatchesSearch(detailsItem.versions[0].compatibility, "requires")).toBe(true);
    expect(compatibilityMatchesSearch(detailsItem.versions[0].compatibility, "postgres only")).toBe(true);
    expect(compatibilityMatchesSearch(detailsItem.versions[0].compatibility, "sqlite")).toBe(false);
  });

  it("detects stale version actions by comparing expected and current state tokens", () => {
    expect(isStaleVersionAction({ packageId: "Elsa.Test", version: "2.0.0", expectedStateToken: "old" }, detailsItem.versions[0])).toBe(true);
    expect(isStaleVersionAction({ packageId: "Elsa.Test", version: "2.0.0", expectedStateToken: "state-2" }, detailsItem.versions[0])).toBe(false);
  });

  it("normalizes JSON-backed feature dependencies", () => {
    const [dependency] = normalizeFeature(detailsItem.versions[0].features[0]).dependencies;
    expect(dependency.packageId).toBe("Elsa.Core");
  });

  it("normalizes feature categories from categories and falls back to the legacy category", () => {
    const feature = normalizeFeature({
      ...detailsItem.versions[0].features[0],
      category: "Legacy",
      categories: ["Persistence", "Data", "Persistence", ""]
    });
    const legacyFeature = normalizeFeature({
      ...detailsItem.versions[0].features[0],
      category: "Legacy",
      categories: null
    });

    expect(feature.categories).toEqual(["Persistence", "Data"]);
    expect(feature.category).toBe("Persistence");
    expect(legacyFeature.categories).toEqual(["Legacy"]);
  });

  it("matches feature search and category filters against normalized categories", () => {
    const categorizedFeature = normalizeFeature({
      ...detailsItem.versions[0].features[0],
      category: "Legacy",
      categories: ["Persistence", "Data"]
    });
    const uncategorizedFeature = normalizeFeature({
      ...detailsItem.versions[0].features[0],
      category: null,
      categories: []
    });

    expect(featureMatchesSearch(categorizedFeature, "data")).toBe(true);
    expect(featureMatchesCategory(categorizedFeature, "Data")).toBe(true);
    expect(featureMatchesCategory(categorizedFeature, ["Data", "Uncategorized"])).toBe(true);
    expect(featureMatchesCategory(categorizedFeature, "Uncategorized")).toBe(false);
    expect(featureMatchesCategory(uncategorizedFeature, ["Data"])).toBe(false);
    expect(featureMatchesCategory(uncategorizedFeature, "Uncategorized")).toBe(true);
    expect(featureMatchesCategory(uncategorizedFeature, "All")).toBe(true);
    expect(featureMatchesCategory(uncategorizedFeature, [])).toBe(true);
  });

  it("ignores invalid entries in JSON-backed feature lists", () => {
    const feature = normalizeFeature({
      ...detailsItem.versions[0].features[0],
      dependencies: [null, { packageId: "Elsa.Core" }, "invalid", 42] as never,
      conflictsJson: "[null,{\"featureId\":\"http\"},\"invalid\"]",
      infrastructureJson: "[{\"kind\":\"database\"},false]"
    });

    expect(feature.dependencies).toEqual([{ packageId: "Elsa.Core" }]);
    expect(feature.conflicts).toEqual([{ featureId: "http" }]);
    expect(feature.infrastructure).toEqual([{ kind: "database" }]);
  });

  it("filters validation findings and visibility reasons by diagnostic text", () => {
    const finding = {
      severity: "Error" as const,
      code: "RequiredFieldMissing",
      message: "Feature description is required.",
      path: "$.features[0].description",
      blocksPublicVisibility: true,
      validatedAt: "2026-05-15T08:12:00Z"
    };

    expect(validationFindingMatchesSearch(finding, "requiredfield")).toBe(true);
    expect(validationFindingMatchesSearch(finding, "blocking")).toBe(true);
    expect(validationFindingMatchesSearch(finding, "other")).toBe(false);
    expect(visibilityReasonMatchesSearch(detailsItem.versions[0].visibilityReasons[0], "trustdecision")).toBe(true);
  });
});

const detailsItem: PackageDetails = {
  packageId: "Elsa.Test",
  approved: false,
  listed: true,
  latestVersion: null,
  source: {
    id: "source-1",
    name: "Elsa Official",
    url: "https://api.nuget.org/v3/index.json",
    enabled: true,
    status: "Healthy"
  },
  versions: [
    {
      version: "2.0.0",
      approvalStatus: "Pending",
      validationStatus: "Invalid",
      isListed: true,
      suspiciousChangeDetected: false,
      schemaVersion: "1.0",
      manifestHash: "sha256:2",
      suspiciousManifestHash: null,
      versionStateToken: "state-2",
      indexedAt: "2026-05-15T08:00:00Z",
      featuresCount: 1,
      settingsCount: 1,
      compatibility: {
        targetFrameworks: ["net10.0"],
        elsaVersionRange: "[4.0.0,5.0.0)",
        requiredCapabilities: ["persistence"],
        notes: ["Requires durable storage."],
        unsupportedCombinations: ["Postgres only"]
      },
      visibilityReasons: [
        {
          code: "VersionPendingApproval",
          category: "TrustDecision",
          severity: "Blocking",
          message: "This package version is pending approval.",
          blocksPublicVisibility: true
        },
        {
          code: "ValidationNotValid",
          category: "Validation",
          severity: "Blocking",
          message: "Validation status is Invalid.",
          blocksPublicVisibility: true
        }
      ],
      features: [
        {
          featureId: "postgresql",
          typeName: "Elsa.Persistence.PostgreSql.PostgreSqlFeature",
          displayName: "PostgreSQL Persistence",
          category: "Persistence",
          requiredCapabilities: ["persistence"],
          dependenciesJson: "[{\"packageId\":\"Elsa.Core\",\"versionRange\":\"[4.0.0,5.0.0)\"}]",
          conflictsJson: "[]",
          infrastructureJson: "[]",
          advanced: false,
          experimental: false,
          extensionsJson: "{}",
          settings: [
            {
              name: "connectionString",
              clrType: "System.String",
              jsonType: "string",
              required: true,
              defaultValueJson: null,
              displayName: "Connection string",
              validationJson: "{}",
              secret: true,
              restartRequired: true,
              uiJson: "{}",
              extensionsJson: "{}"
            }
          ]
        }
      ],
      manifest: {
        available: true,
        schemaVersion: "1.0",
        manifestHash: "sha256:2",
        suspiciousManifestHash: null,
        manifestJson: "{}"
      }
    },
    {
      version: "1.0.0",
      approvalStatus: "Approved",
      validationStatus: "Valid",
      isListed: true,
      suspiciousChangeDetected: false,
      schemaVersion: "1.0",
      manifestHash: "sha256:1",
      suspiciousManifestHash: null,
      versionStateToken: "state-1",
      indexedAt: "2026-05-14T08:00:00Z",
      featuresCount: 0,
      settingsCount: 0,
      compatibility: {
        targetFrameworks: [],
        elsaVersionRange: null,
        requiredCapabilities: [],
        notes: [],
        unsupportedCombinations: []
      },
      visibilityReasons: [],
      features: [],
      manifest: {
        available: true,
        schemaVersion: "1.0",
        manifestHash: "sha256:1",
        suspiciousManifestHash: null,
        manifestJson: "{}"
      }
    }
  ]
};
