import { expect, test } from "@playwright/test";

test.describe("deployment cockpit workflow", () => {
  test("setup, preview, queue deploy, inspect history, and run a supported control", async ({ page }) => {
    const workspaceId = "00000000-0000-0000-0000-000000000010";
    let cockpit = emptyCockpit();

    await page.route("**/api/auth/session", async (route) => {
      await route.fulfill({
        json: {
          loginEnabled: true,
          authenticated: true,
          displayName: "Test User",
          email: "test@example.com",
          loginPath: "/api/auth/login",
          logoutPath: "/api/auth/logout"
        }
      });
    });
    await page.route("**/api/me/workspaces", async (route) => {
      await route.fulfill({
        json: {
          account: { id: "account-1", displayName: "Test User", email: "test@example.com" },
          workspaces: [{ id: workspaceId, name: "Acme Insurance", kind: "Personal", role: "Owner" }]
        }
      });
    });
    await page.route(`**/api/workspaces/${workspaceId}/deployments/permissions`, async (route) => {
      await route.fulfill({
        json: {
          permissions: [
            "deployments.read",
            "deployments.setup.manage",
            "deployments.promotion.preview",
            "deployments.run.execute",
            "deployments.rollback.execute",
            "deployments.controls.execute"
          ]
        }
      });
    });
    await page.route(`**/api/workspaces/${workspaceId}/deployments/cockpit`, async (route) => {
      await route.fulfill({ json: cockpit });
    });
    await page.route(`**/api/workspaces/${workspaceId}/deployments/applications`, async (route) => {
      await route.fulfill({ status: 201, json: { id: "claims-ops", workspaceId, name: "Claims Operations" } });
    });
    await page.route(`**/api/workspaces/${workspaceId}/deployments/applications/claims-ops/environments`, async (route) => {
      await route.fulfill({ status: 201, json: { id: "claims-test", workspaceId, applicationId: "claims-ops", name: "Test" } });
    });
    await page.route(`**/api/workspaces/${workspaceId}/deployments/environments/claims-test/engines`, async (route) => {
      cockpit = populatedCockpit();
      await route.fulfill({ status: 201, json: cockpit.engines[0] });
    });
    await page.route(`**/api/workspaces/${workspaceId}/deployments/promotions/preview`, async (route) => {
      await route.fulfill({ json: cockpit.comparisons[0] });
    });
    await page.route(`**/api/workspaces/${workspaceId}/deployments/confirmations`, async (route) => {
      const body = route.request().postDataJSON() as { actionType: string; targetId: string };
      await route.fulfill({ status: 201, json: { id: `${body.actionType}-confirmation`, workspaceId, ...body } });
    });
    await page.route(`**/api/workspaces/${workspaceId}/deployments/runs`, async (route) => {
      cockpit = {
        ...cockpit,
        history: [
          {
            id: "00000000-0000-0000-0000-000000000777",
            status: "Queued",
            revision: 42,
            actor: "account-1",
            environmentId: "claims-test",
            engineId: "test-engine",
            validationOutcome: "Passed",
            occurredAt: "2026-05-26T10:01:00Z",
            rollbackSourceRevision: null
          }
        ]
      };
      await route.fulfill({
        status: 201,
        json: {
          id: "00000000-0000-0000-0000-000000000777",
          workspaceId,
          applicationId: "claims-ops",
          environmentId: "claims-test",
          engineId: "test-engine",
          sourceRevisionId: "00000000-0000-0000-0000-000000000142",
          previousDeployedRevisionId: null,
          rollbackSourceRunId: null,
          status: "Queued",
          validationOutcome: "Passed",
          confirmationId: "Deploy-confirmation",
          actorAccountId: "account-1",
          queuedAt: "2026-05-26T10:01:00Z",
          startedAt: null,
          completedAt: null,
          createdAt: "2026-05-26T10:01:00Z",
          workerId: null,
          workerHeartbeatAt: null,
          attemptNumber: 1,
          recoveryReason: null,
          failureMessage: null
        }
      });
    });
    await page.route(`**/api/workspaces/${workspaceId}/deployments/engines/test-engine/controls/reload-configuration/run`, async (route) => {
      await route.fulfill({
        json: {
          id: "control-execution-1",
          workspaceId,
          engineId: "test-engine",
          environmentId: "claims-test",
          controlId: "reload-configuration",
          controlLabel: "Reload Configuration",
          boundary: "EngineApi",
          requiredCapabilityId: "engine.reload-configuration",
          confirmationId: "RuntimeControl-confirmation",
          actorAccountId: "account-1",
          status: "Succeeded",
          createdAt: "2026-05-26T10:03:00Z",
          message: "Reload Configuration executed for claims-test-weu-01."
        }
      });
    });

    await page.goto("/admin/deployments");
    await expect(page.getByText("No deployment setup")).toBeVisible();
    await page.getByLabel("Application").fill("Claims Operations");
    await page.getByLabel("Environment").fill("Test");
    await page.getByLabel("Engine").fill("claims-test-weu-01");
    await page.getByLabel("Base URL").fill("https://claims-test.example/elsa");
    await page.getByLabel("Credential reference").fill("kv://claims/test/elsa-api");
    await page.getByRole("button", { name: "Create setup" }).click();
    await expect(page.getByRole("heading", { name: "Deployments" })).toBeVisible();

    await page.getByRole("button", { name: "Promotion Diff" }).click();
    await page.getByRole("button", { name: "Refresh Preview" }).click();
    await expect(page.getByText("Live validation passed for Test.")).toBeVisible();
    await page.getByRole("button", { name: "Deploy Revision" }).click();
    await expect(page.getByRole("status")).toContainText("Deployment run queued");

    await page.getByRole("button", { name: "Observability" }).click();
    await expect(page.getByText("Latest Queued")).toBeVisible();
    await expect(page.getByText("No drift metadata has been recorded")).toBeVisible();

    await page.getByRole("button", { name: "Engine Registration" }).click();
    await page.getByRole("button", { name: "Run" }).click();
    await expect(page.getByRole("status")).toContainText("Reload Configuration executed for claims-test-weu-01.");
  });
});

function emptyCockpit() {
  return {
    applications: [],
    engines: [],
    comparisons: [],
    observabilityBindings: [],
    history: [],
    driftReport: [],
    assistantPlans: []
  };
}

function populatedCockpit() {
  return {
    applications: [
      {
        id: "claims-ops",
        name: "Claims Operations",
        workspaceName: "Acme Insurance",
        environments: [
          {
            id: "claims-test",
            name: "Test",
            tier: "Test",
            health: "Healthy",
            desiredRevision: {
              id: "00000000-0000-0000-0000-000000000142",
              revision: 42,
              commit: "8f6a9c1",
              label: "Payment retry workflow",
              authoredAt: "2026-05-21T08:30:00Z"
            },
            deployedRevision: 39,
            deploymentStatus: "Succeeded",
            driftStatus: "InSync",
            engineIds: ["test-engine"]
          }
        ]
      }
    ],
    engines: [
      {
        id: "test-engine",
        name: "claims-test-weu-01",
        environmentId: "claims-test",
        endpoint: {
          baseUrl: "https://claims-test.example/elsa",
          region: "West Europe",
          version: "Elsa 4.0.1",
          certificateStatus: "Trusted"
        },
        credentialReference: {
          provider: "Azure Key Vault",
          reference: "kv://claims/test/elsa-api",
          verificationStatus: "Verified",
          lastVerifiedAt: "2026-05-22T07:50:00Z"
        },
        health: "Healthy",
        lastHeartbeatAt: "2026-05-22T08:16:30Z",
        lastVerificationAt: "2026-05-22T08:12:30Z",
        verificationMessage: "Endpoint responded successfully.",
        capabilities: [{ id: "engine.reload-configuration", label: "Reload engine configuration", boundary: "EngineApi" }],
        controls: [
          {
            id: "reload-configuration",
            label: "Reload Configuration",
            boundary: "EngineApi",
            capabilityId: "engine.reload-configuration",
            description: "Reloads engine API configuration from desired state."
          }
        ],
        hostingProvider: null
      }
    ],
    comparisons: [
      {
        sourceEnvironmentId: "claims-test",
        targetEnvironmentId: "claims-test",
        sourceRevisionId: "00000000-0000-0000-0000-000000000142",
        sourceRevision: 42,
        targetRevision: 39,
        diff: [
          {
            id: "workflow-payment-retry",
            category: "Workflows",
            name: "Payment Retry",
            sourceValue: "v8",
            targetValue: "v6",
            impact: "Changed"
          }
        ],
        validations: [{ id: "secret-payment-test", severity: "Pass", scope: "Secret references", message: "Live validation passed for Test." }],
        rollbackRevision: 39,
        rollbackRevisionId: "00000000-0000-0000-0000-000000000139"
      }
    ],
    observabilityBindings: [
      {
        id: "test-logs",
        kind: "Logs",
        provider: "Azure Monitor",
        status: "Connected",
        scope: "claims-test / rev 42",
        correlatedRevision: 42,
        sample: "12 structured events in the last 30 minutes"
      }
    ],
    history: [],
    driftReport: [],
    assistantPlans: []
  };
}
