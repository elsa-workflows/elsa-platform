import { expect, test } from "@playwright/test";

const packageDetails = {
  packageId: "Elsa.Persistence.PostgreSql",
  source: {
    id: "source-1",
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
      versionStateToken: "state-102",
      publishedAt: "2026-05-15T06:30:00Z",
      indexedAt: "2026-05-15T08:10:00Z",
      featuresCount: 2,
      settingsCount: 3,
      compatibility: { targetFrameworks: ["net10.0"], elsaVersionRange: "[4.0.0,5.0.0)", requiredCapabilities: [], notes: [], unsupportedCombinations: [] },
      visibilityReasons: [{ code: "VersionPendingApproval", category: "TrustDecision", severity: "Blocking", message: "This package version is pending approval.", blocksPublicVisibility: true }],
      features: [],
      manifest: { available: true, schemaVersion: "1.0", manifestHash: "sha256:postgres-102", suspiciousManifestHash: null, manifestJson: "" }
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
      versionStateToken: "state-101",
      publishedAt: "2026-05-14T06:30:00Z",
      indexedAt: "2026-05-14T08:10:00Z",
      featuresCount: 1,
      settingsCount: 1,
      compatibility: { targetFrameworks: ["net10.0"], elsaVersionRange: "[4.0.0,5.0.0)", requiredCapabilities: [], notes: [], unsupportedCombinations: [] },
      visibilityReasons: [{ code: "PackagePendingApproval", category: "TrustDecision", severity: "Blocking", message: "This package is pending approval.", blocksPublicVisibility: true }],
      features: [],
      manifest: { available: true, schemaVersion: "1.0", manifestHash: "sha256:postgres-101", suspiciousManifestHash: null, manifestJson: "" }
    }
  ]
};

test.describe("package details", () => {
  test("supports package, version, and section routes", async ({ page }) => {
    await page.route("**/api/admin/packages/Elsa.Persistence.PostgreSql", async (route) => {
      await route.fulfill({ json: packageDetails });
    });
    await page.route("**/api/admin/packages/Elsa.Persistence.PostgreSql/versions/*/validation", async (route) => {
      await route.fulfill({ json: { packageId: packageDetails.packageId, version: "1.0.2", findings: [] } });
    });
    await page.route("**/api/admin/packages/Elsa.Persistence.PostgreSql/versions/*/manifest", async (route) => {
      const version = decodeURIComponent(route.request().url().split("/versions/")[1]?.split("/")[0] ?? "");
      const packageVersion = packageDetails.versions.find((item) => item.version === version) ?? packageDetails.versions[0];
      await route.fulfill({ json: { ...packageVersion.manifest, manifestJson: JSON.stringify({ manifest: packageVersion.version }) } });
    });

    await page.goto("/admin/packages/Elsa.Persistence.PostgreSql");
    await expect(page.getByRole("heading", { name: "Elsa.Persistence.PostgreSql" })).toBeVisible();
    await expect(page.getByText("VersionPendingApproval")).toBeVisible();

    await page.goto("/admin/packages/Elsa.Persistence.PostgreSql/versions/1.0.1");
    await expect(page.getByText("1.0.1").first()).toBeVisible();

    await page.goto("/admin/packages/Elsa.Persistence.PostgreSql/versions/1.0.2/manifest");
    await expect(page.getByText("manifest", { exact: true })).toBeVisible();
  });
});
