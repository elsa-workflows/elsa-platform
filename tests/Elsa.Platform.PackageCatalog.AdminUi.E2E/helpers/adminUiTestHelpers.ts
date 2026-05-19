import type { Page } from "@playwright/test";

export async function navigateAdmin(page: Page, path = "/admin/overview") {
  await page.goto(path);
}

export async function stubAdminApi(page: Page) {
  await page.route("**/api/admin/**", async (route) => {
    await route.fulfill({ status: 200, contentType: "application/json", body: "[]" });
  });
}
