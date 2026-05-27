export const sourceFixture = {
  id: "source-1",
  name: "Elsa Official",
  type: "NuGetFeed",
  url: "https://api.nuget.org/v3/index.json",
  enabled: true,
  includePatterns: ["Elsa."],
  excludePatterns: ["*.Tests"],
  approvalPolicy: "Manual",
  versionDiscoveryPolicy: "AllVersions",
  status: "Healthy",
  isSyncing: false,
  lastSuccessfulSyncAt: "2026-05-15T08:00:00Z",
  lastSyncedAt: "2026-05-15T08:00:00Z",
  lastSyncError: null,
  packageCount: 12,
  createdAt: "2026-05-15T07:00:00Z",
  updatedAt: "2026-05-15T08:00:00Z"
};

export const packageFixture = {
  packageId: "Elsa.Persistence.PostgreSql",
  sourceId: "00000000-0000-0000-0000-000000000001",
  approved: false,
  listed: true,
  latestVersion: "1.0.2",
  approvalStatus: "Pending",
  validationStatus: "Valid",
  featuresCount: 3,
  updatedAt: "2026-05-15T08:15:00Z",
  versions: [
    {
      version: "1.0.2",
      approvalStatus: "Pending",
      validationStatus: "Valid",
      isListed: true,
      suspiciousChangeDetected: false,
      schemaVersion: "1.0",
      versionStateToken: "state-102-pending-valid"
    }
  ]
};

export const packageDetailsFixture = {
  packageId: "Elsa.Persistence.PostgreSql",
  source: {
    id: "00000000-0000-0000-0000-000000000001",
    name: "Elsa Official",
    url: "https://api.nuget.org/v3/index.json",
    enabled: true,
    status: "Healthy",
    lastSyncedAt: "2026-05-15T08:00:00Z",
    lastSuccessfulSyncAt: "2026-05-15T08:00:00Z"
  },
  approved: false,
  listed: true,
  latestVersion: "1.0.2",
  createdAt: "2026-05-15T07:00:00Z",
  updatedAt: "2026-05-15T08:15:00Z",
  versions: [
    {
      version: "1.0.2",
      approvalStatus: "Pending",
      validationStatus: "Valid",
      isListed: true,
      suspiciousChangeDetected: false,
      schemaVersion: "1.0",
      manifestHash: "sha256:postgres-102",
      suspiciousManifestHash: null,
      versionStateToken: "state-102-pending-valid",
      publishedAt: "2026-05-15T06:30:00Z",
      indexedAt: "2026-05-15T08:10:00Z",
      featuresCount: 2,
      settingsCount: 3,
      compatibility: {
        targetFrameworks: ["net10.0"],
        elsaVersionRange: "[4.0.0,5.0.0)",
        requiredCapabilities: ["persistence"],
        notes: ["Requires a PostgreSQL provider configured at runtime."],
        unsupportedCombinations: []
      },
      visibilityReasons: [
        {
          code: "VersionPendingApproval",
          category: "TrustDecision",
          severity: "Blocking",
          message: "This package version is pending approval.",
          blocksPublicVisibility: true
        }
      ],
      features: [
        {
          featureId: "postgresql",
          typeName: "Elsa.Persistence.PostgreSql.PostgreSqlFeature",
          displayName: "PostgreSQL Persistence",
          description: "Stores workflow state in PostgreSQL.",
          category: "Persistence",
          requiredCapabilities: ["persistence"],
          dependencies: [{ packageId: "Elsa.Core", versionRange: "[4.0.0,5.0.0)", featureId: null, optional: false, reason: "Core runtime required." }],
          conflicts: [],
          infrastructure: [{ id: "postgresql", kind: "Database", optional: false, reason: "Stores workflow state.", capabilities: ["relational"], providers: ["PostgreSQL"], configurationKeys: ["connectionString"], extensionsJson: "{}" }],
          advanced: false,
          experimental: false,
          extensionsJson: "{}",
          settings: [
            {
              name: "connectionString",
              displayName: "Connection string",
              description: "Database connection string.",
              category: "Connection",
              jsonType: "string",
              clrType: "System.String",
              required: true,
              defaultValueJson: null,
              validationJson: "{\"minLength\":1}",
              secret: true,
              restartRequired: true,
              environmentVariable: "ELSA_POSTGRESQL_CONNECTION_STRING",
              uiJson: "{}",
              extensionsJson: "{}"
            }
          ]
        }
      ],
      manifest: {
        available: true,
        schemaVersion: "1.0",
        manifestHash: "sha256:postgres-102",
        suspiciousManifestHash: null,
        manifestJson: JSON.stringify({ id: "Elsa.Persistence.PostgreSql", version: "1.0.2", features: [{ id: "postgresql" }] }, null, 2)
      }
    },
    {
      version: "1.0.1",
      approvalStatus: "Approved",
      validationStatus: "Valid",
      isListed: true,
      suspiciousChangeDetected: false,
      schemaVersion: "1.0",
      manifestHash: "sha256:postgres-101",
      suspiciousManifestHash: null,
      versionStateToken: "state-101-approved-valid",
      publishedAt: "2026-05-14T06:30:00Z",
      indexedAt: "2026-05-14T08:10:00Z",
      featuresCount: 1,
      settingsCount: 1,
      compatibility: {
        targetFrameworks: ["net10.0"],
        elsaVersionRange: "[4.0.0,5.0.0)",
        requiredCapabilities: ["persistence"],
        notes: [],
        unsupportedCombinations: []
      },
      visibilityReasons: [
        {
          code: "PackagePendingApproval",
          category: "TrustDecision",
          severity: "Blocking",
          message: "This package is pending approval.",
          blocksPublicVisibility: true
        }
      ],
      features: [],
      manifest: {
        available: true,
        schemaVersion: "1.0",
        manifestHash: "sha256:postgres-101",
        suspiciousManifestHash: null,
        manifestJson: "{\"id\":\"Elsa.Persistence.PostgreSql\",\"version\":\"1.0.1\"}"
      }
    }
  ]
};

export const packageWithoutVersionsFixture = {
  ...packageDetailsFixture,
  packageId: "Elsa.Empty",
  latestVersion: null,
  versions: []
};

export const validationFindingsFixture = {
  packageId: "Elsa.Persistence.PostgreSql",
  version: "1.0.2",
  findings: [
    {
      severity: "Warning",
      code: "RecommendedDescription",
      message: "Feature description is recommended.",
      path: "$.features[0].description",
      blocksPublicVisibility: false,
      validatedAt: "2026-05-15T08:12:00Z",
      validatorVersion: "1.0.0"
    }
  ]
};

export const syncRunFixture = {
  id: "sync-123",
  trigger: "Scheduled",
  status: "CompletedWithErrors",
  startedAt: "2026-05-15T08:00:00Z",
  completedAt: "2026-05-15T08:02:14Z",
  error: null,
  summaryCountersJson: JSON.stringify({ scanned: 52, indexed: 4, failed: 1 }),
  itemCount: 1,
  sources: [{ id: "source-1", name: "Elsa Official" }],
  items: [
    {
      id: "item-1",
      sourceId: "source-1",
      packageId: "Elsa.Persistence.PostgreSql",
      version: "1.0.2",
      status: "Failed",
      message: null,
      error: "Package download failed.",
      startedAt: "2026-05-15T08:00:05Z",
      completedAt: "2026-05-15T08:00:07Z"
    }
  ]
};
