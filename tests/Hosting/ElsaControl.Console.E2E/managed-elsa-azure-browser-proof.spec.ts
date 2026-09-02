import { expect, type Page, test } from "@playwright/test";

const proofEnabled = process.env.MANAGED_ELSA_AZURE_BROWSER_PROOF === "1";
const controlOrigin = (process.env.ADMIN_UI_BASE_URL ?? "").replace(/\/+$/, "");
const runtimeOrigin = (process.env.MANAGED_ELSA_PROOF_RUNTIME_ORIGIN ?? "").replace(/\/+$/, "");
const stateLifetimeSeconds = Number(process.env.MANAGED_ELSA_PROOF_STATE_LIFETIME_SECONDS ?? "");

test.use({ ignoreHTTPSErrors: false, trace: "off", screenshot: "off", video: "off" });

test.describe("managed Elsa Azure browser proof", () => {
  test.skip(!proofEnabled, "Requires an explicit Azure proof run and interactive Entra sign-in.");
  test.describe.configure({ mode: "serial" });

  test("completes the public-TLS handoff and fails closed on replay and expiry", async ({ page }) => {
    test.setTimeout(480_000);
    requirePublicHttpsOrigin(controlOrigin, "ADMIN_UI_BASE_URL");
    requirePublicHttpsOrigin(runtimeOrigin, "MANAGED_ELSA_PROOF_RUNTIME_ORIGIN");
    requireBoundedStateLifetime(stateLifetimeSeconds);

    await signInInteractively(page);

    let callbackForm: URLSearchParams | undefined;
    page.on("request", (request) => {
      if (request.method() === "POST" && request.url() === `${runtimeOrigin}/managed-elsa/handoff/callback`)
        callbackForm = new URLSearchParams(request.postData() ?? "");
    });

    await openHealthyInstance(page);
    await expect(page).toHaveURL(new RegExp(`^${escapeRegExp(runtimeOrigin)}/`), { timeout: 30_000 });
    expect(await protectedOperationStatus(page)).toBe(200);

    expect(callbackForm).toBeDefined();
    const replay = await page.request.post(`${runtimeOrigin}/managed-elsa/handoff/callback`, {
      form: Object.fromEntries(callbackForm!)
    });
    expect(replay.status()).toBe(400);

    const logoutStatus = await page.evaluate(async () =>
      fetch("/managed-elsa/logout", { method: "POST", credentials: "include" }).then((response) => response.status));
    expect(logoutStatus).toBe(204);
    expect(await protectedOperationStatus(page)).toBe(401);

    await page.goto(`${controlOrigin}/admin/runtimes`);
    await expect(page.getByRole("heading", { name: "Managed Elsa" })).toBeVisible();
    await page.route("**/api/managed-elsa/handoff/issue", async (route) => {
      await new Promise((resolve) => setTimeout(resolve, (stateLifetimeSeconds + 5) * 1_000));
      await route.continue();
    }, { times: 1 });

    const expiredCallback = page.waitForResponse((response) =>
      response.request().method() === "POST" && response.url() === `${runtimeOrigin}/managed-elsa/handoff/callback`);
    await managedInstanceRow(page).getByRole("button", { name: "Open" }).click();
    expect((await expiredCallback).status()).toBe(400);
  });
});

async function signInInteractively(page: Page) {
  await page.goto(`${controlOrigin}/admin/runtimes`);
  const managedElsaHeading = page.getByRole("heading", { name: "Managed Elsa" });
  if (await managedElsaHeading.isVisible())
    return;

  await expect(page.getByRole("heading", { name: "Sign in to Elsa Control" })).toBeVisible();
  await page.getByRole("button", { name: "Continue to sign in" }).click();
  await waitForControlReturn(page);
  await expect(managedElsaHeading).toBeVisible();
}

async function waitForControlReturn(page: Page) {
  const deadline = Date.now() + 300_000;
  while (Date.now() < deadline) {
    const current = new URL(page.url());
    if (current.origin === controlOrigin && current.pathname === "/admin/runtimes")
      return;
    await page.waitForTimeout(500);
  }

  throw new Error("Interactive sign-in did not return to Elsa Control within the bounded window.");
}

async function openHealthyInstance(page: Page) {
  const instanceRow = managedInstanceRow(page);
  await expect(instanceRow).toContainText("Healthy");
  await instanceRow.getByRole("button", { name: "Open" }).click();
}

function managedInstanceRow(page: Page) {
  return page.getByRole("row").filter({ hasText: "Managed Elsa browser proof" });
}

function protectedOperationStatus(page: Page) {
  return page.evaluate(async () =>
    fetch("/elsa/api/workflow-definitions?skip=0&take=1", { credentials: "include" }).then((response) => response.status));
}

function requirePublicHttpsOrigin(value: string, variableName: string) {
  const uri = new URL(value);
  if (uri.protocol !== "https:" || uri.username || uri.password || uri.pathname !== "/" || uri.search || uri.hash)
    throw new Error(`${variableName} must be a public HTTPS origin without credentials, query, or fragment.`);
}

function escapeRegExp(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function requireBoundedStateLifetime(value: number) {
  if (!Number.isInteger(value) || value < 5 || value > 300)
    throw new Error("MANAGED_ELSA_PROOF_STATE_LIFETIME_SECONDS must be an integer from 5 through 300.");
}
