import { execFile } from "node:child_process";
import path from "node:path";
import { promisify } from "node:util";
import { fileURLToPath } from "node:url";
import { expect, type Page, test } from "@playwright/test";

const execFileAsync = promisify(execFile);
const proofEnabled = process.env.MANAGED_ELSA_BROWSER_PROOF === "1";
const keycloakUsername = process.env.MANAGED_ELSA_PROOF_USERNAME ?? "ada";
const keycloakPassword = process.env.MANAGED_ELSA_PROOF_PASSWORD ?? "password";
const runtimeOrigin = (process.env.MANAGED_ELSA_PROOF_RUNTIME_ORIGIN ?? "https://runtime.localhost:7444")
  .replace(/\/+$/, "");
const fixtureDatabase = process.env.MANAGED_ELSA_PROOF_DATABASE;
const repositoryRoot = fileURLToPath(new URL("../../..", import.meta.url));
const fixtureProject = process.env.MANAGED_ELSA_PROOF_FIXTURE_PROJECT ??
  path.join(repositoryRoot, "src/Hosting/ElsaControl.ManagedBrowserProof/ElsaControl.ManagedBrowserProof.csproj");

test.use({ ignoreHTTPSErrors: true, trace: "off" });

test.describe("managed Elsa browser proof", () => {
  test.skip(!proofEnabled, "Requires the isolated managed-Elsa proof fixture.");
  test.describe.configure({ mode: "serial" });

  test.beforeAll(() => {
    if (!fixtureDatabase)
      throw new Error("MANAGED_ELSA_PROOF_DATABASE is required for the live browser proof.");
  });

  test.afterEach(async () => {
    if (fixtureDatabase)
      await runFixture("restore");
  });

  test("opens a healthy instance, rejects replay, performs an authorized operation, and revokes the runtime session", async ({ page }) => {
    test.setTimeout(90_000);
    await signInToControl(page);

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
  });

  test("renders a verified but unavailable instance without an open action", async ({ page }) => {
    await runFixture("unavailable");
    await signInToControl(page);

    await expect(page.getByRole("heading", { name: "Managed Elsa" })).toBeVisible();
    const instanceRow = managedInstanceRow(page);
    await expect(instanceRow).toContainText("Unavailable");
    await expect(instanceRow.getByRole("button", { name: "Open" })).toHaveCount(0);
  });

  test("fails safely when organization membership is revoked before issuance", async ({ page }) => {
    await signInToControl(page);
    await expect(managedInstanceRow(page)).toContainText("Healthy");

    await page.route("**/api/managed-elsa/handoff/issue", async (route) => {
      await runFixture("revoke");
      await route.continue();
    }, { times: 1 });
    await managedInstanceRow(page).getByRole("button", { name: "Open" }).click();

    await expect(page.getByRole("alert")).toContainText(
      "This managed instance is no longer available to your account.",
      { timeout: 30_000 });
    await expect(page).toHaveURL(/\/admin\/runtimes$/);
  });

  test("rejects an expired browser-bound handoff state", async ({ page }) => {
    test.setTimeout(120_000);
    await signInToControl(page);
    await page.route("**/api/managed-elsa/handoff/issue", async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 65_000));
      await route.continue();
    }, { times: 1 });

    const callbackResponse = page.waitForResponse((response) =>
      response.request().method() === "POST" && response.url() === `${runtimeOrigin}/managed-elsa/handoff/callback`);
    await managedInstanceRow(page).getByRole("button", { name: "Open" }).click();

    expect((await callbackResponse).status()).toBe(400);
  });
});

async function signInToControl(page: Page) {
  await page.goto("/admin/runtimes");
  await expect(page.getByRole("heading", { name: "Sign in to Elsa Control" })).toBeVisible();
  await page.getByRole("button", { name: "Continue to sign in" }).click();
  await page.getByLabel("Username or email").fill(keycloakUsername);
  await page.getByRole("textbox", { name: "Password" }).fill(keycloakPassword);
  await page.getByRole("button", { name: "Sign In" }).click();
  await expect(page).toHaveURL(/\/admin\/runtimes$/);
}

async function openHealthyInstance(page: Page) {
  await expect(page.getByRole("heading", { name: "Managed Elsa" })).toBeVisible();
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

async function runFixture(command: "restore" | "revoke" | "unavailable") {
  if (!fixtureDatabase)
    throw new Error("MANAGED_ELSA_PROOF_DATABASE is required for the live browser proof.");

  await execFileAsync("dotnet", [
    "run",
    "--no-build",
    "--project",
    fixtureProject,
    "--",
    command,
    fixtureDatabase,
    runtimeOrigin
  ], { cwd: repositoryRoot });
}

function escapeRegExp(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
