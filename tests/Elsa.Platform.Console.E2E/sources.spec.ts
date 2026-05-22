import { expect, test } from "@playwright/test";

test.describe("source operations", () => {
  test("create, edit, pattern-test, sync, toggle, and soft-delete source workflow", async ({ page }) => {
    let source: Record<string, unknown> | null = null;
    const sourceBody = {
      id: "source-1",
      name: "Elsa Official",
      type: "NuGetFeed",
      url: "https://api.nuget.org/v3/index.json",
      enabled: true,
      includePatterns: ["Elsa.*"],
      excludePatterns: ["*.Tests"],
      approvalPolicy: "Manual",
      versionDiscoveryPolicy: "AllVersions",
      status: "Healthy",
      lastSyncedAt: null,
      lastSuccessfulSyncAt: null,
      lastSyncError: null,
      packageCount: 0,
      softDeletedAt: null,
      pollingInterval: "PT30M",
      createdAt: "2026-05-15T08:00:00Z",
      updatedAt: "2026-05-15T08:00:00Z"
    };

    await page.route("**/api/admin/sources", async (route) => {
      if (route.request().method() === "GET") {
        await route.fulfill({ json: source ? [source] : [] });
        return;
      }
      source = sourceBody;
      await route.fulfill({ json: source });
    });
    await page.route("**/api/admin/sources/source-1", async (route) => {
      if (route.request().method() === "PUT") {
        const body = route.request().postDataJSON();
        source = { ...sourceBody, ...body, id: "source-1", type: "NuGetFeed", status: "Healthy", packageCount: 0 };
        await route.fulfill({ json: source });
        return;
      }
      if (route.request().method() === "DELETE") {
        source = null;
        await route.fulfill({ status: 204 });
        return;
      }
      await route.fulfill({ json: source ?? sourceBody });
    });
    await page.route("**/api/admin/sync/sources/source-1", async (route) => {
      source = { ...(source ?? sourceBody), lastSuccessfulSyncAt: "2026-05-15T08:00:00Z" };
      await route.fulfill({ json: { id: "sync-1", status: "Completed" } });
    });

    await page.goto("/admin/sources/new");
    await page.getByLabel("Name").fill("Elsa Official");
    await page.getByLabel("Feed URL").fill("https://api.nuget.org/v3/index.json");
    await page.getByLabel("Version Discovery").selectOption("LatestPreview");
    await page.getByLabel("Exclude Patterns").fill("*.Tests");
    await expect(page.getByText("Elsa.Tests")).toBeVisible();
    await page.getByRole("button", { name: "Save Source" }).click();
    await expect(page.getByRole("link", { name: "Elsa Official" })).toBeVisible();
    await page.getByRole("link", { name: "Edit" }).click();
    await page.getByLabel("Name").fill("Elsa Internal");
    await page.getByRole("button", { name: "Save Source" }).click();
    await expect(page.getByRole("link", { name: "Elsa Internal" })).toBeVisible();
    await page.getByRole("button", { name: "Sync" }).click();
    await page.getByRole("button", { name: "Disable" }).click();
    await expect(page.getByText("No")).toBeVisible();
    await page.getByRole("button", { name: "Enable" }).click();
    await expect(page.getByText("Yes")).toBeVisible();
    await page.getByRole("button", { name: "Delete" }).click();
    await page.getByRole("button", { name: "Delete" }).last().click();
    await expect(page.getByText("No package sources")).toBeVisible();
  });
});
