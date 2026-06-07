import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { WorkspaceContextProvider } from "@/app/WorkspaceContextProvider";
import {
  DeploymentApplicationEditPage,
  DeploymentApplicationsPage,
  DeploymentApplicationPage,
  DeploymentApplicationRevisionsPage,
  DeploymentEngineEditPage,
  DeploymentEnginePage,
  DeploymentEngineRegisterPage,
  DeploymentEnvironmentCreatePage,
  DeploymentEnvironmentEditPage,
  DeploymentEnvironmentPage,
  DeploymentRevisionDetailPage,
  DeploymentRevisionCreatePage,
  DeploymentsPage,
  NewDeploymentSetupPage
} from "@/features/deployments/DeploymentsPage";
import { DeploymentSetupPanel } from "@/features/deployments/DeploymentSetupPanel";
import type {
  DeploymentCockpit,
  WorkspaceDeploymentTier,
  WorkspaceDesiredStateRevisionDetail,
  WorkspaceDesiredStateRevisionSummary
} from "@/features/deployments/deploymentModels";
import { AuthProvider } from "@/lib/auth/AuthProvider";

describe("DeploymentsPage", () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it("renders a workspace deployment overview without the application list", async () => {
    renderDeployments();

    expect(await screen.findByRole("heading", { name: "Deployment overview" }, { timeout: 15000 })).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/applications")).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/new")).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/tiers")).toBeInTheDocument();
    expect(screen.getAllByText("4").length).toBeGreaterThan(0);
    expect(screen.getByText("Deployment posture")).toBeInTheDocument();
    expect(screen.getByText("Operational shortcuts")).toBeInTheDocument();
    expect(screen.queryByText("Workflow applications")).not.toBeInTheDocument();
    expect(screen.queryByText("Claims Operations")).not.toBeInTheDocument();
    expect(screen.getByText("Healthy engines")).toBeInTheDocument();
    expect(screen.queryByText(/password|token|secret value/i)).not.toBeInTheDocument();
  });

  it("renders application list on a dedicated route", async () => {
    renderDeployments(multipleApplicationsCockpit, "/admin/deployments/applications");

    expect(await screen.findByRole("heading", { name: "Applications" })).toBeInTheDocument();
    expect(screen.getByText("Workflow applications")).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/applications/claims-ops")).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/applications/policy-app")).toBeInTheDocument();
    expect(screen.getByText("Claims Operations")).toBeInTheDocument();
    expect(screen.getByLabelText("Sort applications")).toHaveValue("name");
    expect(within(screen.getByRole("table")).getByRole("columnheader", { name: "Application" })).toBeInTheDocument();

    await userEvent.type(screen.getByPlaceholderText("Search applications"), "Policy");

    expect(screen.getByRole("link", { name: "Policy" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Claims Operations" })).not.toBeInTheDocument();
    expect(screen.queryByText("Deployment posture")).not.toBeInTheDocument();
  });

  it("shows an empty application list state when no deployment setup exists", async () => {
    renderDeployments({
      applications: [],
      engines: [],
      comparisons: [],
      observabilityBindings: [],
      history: [],
      driftReport: [],
      assistantPlans: []
    }, "/admin/deployments/applications");

    expect(await screen.findByText("No deployment setup")).toBeInTheDocument();
    expect(screen.getByText("Create a workflow application, first environment, and first engine registration to start managing deployments.")).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/new")).toBeInTheDocument();
  });

  it("creates deployment setup from the guided setup route", async () => {
    const fetchMock = renderDeployments({
      applications: [],
      engines: [],
      comparisons: [],
      observabilityBindings: [],
      history: [],
      driftReport: [],
      assistantPlans: []
    }, "/admin/deployments/new");

    expect(await screen.findByRole("heading", { name: "New application setup" })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText("Tier")).toHaveValue("tier-production"));
    await userEvent.type(screen.getByLabelText("Application"), "Claims Operations");
    await userEvent.clear(screen.getByLabelText("Environment"));
    await userEvent.type(screen.getByLabelText("Environment"), "Prod");
    await userEvent.type(screen.getByLabelText("Engine"), "claims-prod");
    await userEvent.type(screen.getByLabelText("Base URL"), "https://workflows.example.test/elsa");
    await userEvent.type(screen.getByLabelText("Credential reference"), "kv://claims/prod/elsa-api");
    await userEvent.click(screen.getByRole("button", { name: "Create setup" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/applications`),
        expect.objectContaining({ method: "POST" })
      )
    );
    expect(requestBody(fetchMock, "POST", "/applications/app-created/environments")).toMatchObject({
      name: "Prod",
      tier: "Production",
      tierId: "tier-production"
    });
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/environments/env-created/engines`),
      expect.objectContaining({ method: "POST" })
    );
  }, 15000);

  it("renders application detail with an environment table", async () => {
    renderDeployments(multipleApplicationsCockpit, "/admin/deployments/applications/claims-ops");

    expect(await screen.findByRole("heading", { name: "Claims Operations" })).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/applications/claims-ops/revisions")).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/applications/claims-ops/environments/new")).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/applications/claims-ops/environments/claims-dev")).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/applications/claims-ops/environments/claims-prod")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Policy/i })).not.toBeInTheDocument();
  });

  it("renders application revisions across environments", async () => {
    renderDeployments(undefined, "/admin/deployments/applications/claims-ops/revisions");

    expect(await screen.findByRole("heading", { name: "Claims Operations revisions" })).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/applications/claims-ops/environments/claims-dev/revisions/new")).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/applications/claims-ops/revisions/00000000-0000-0000-0000-000000000142")).toBeInTheDocument();
    expect(screen.getByText("Payment retry workflow")).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/applications/claims-ops/environments/claims-stage")).toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText("Filter revisions by environment"), "claims-prod");

    expect(screen.getByText("Baseline production")).toBeInTheDocument();
    expect(screen.queryByText("Payment retry workflow")).not.toBeInTheDocument();
  });

  it("renders revision detail records and deployment state", async () => {
    renderDeployments(undefined, "/admin/deployments/applications/claims-ops/revisions/00000000-0000-0000-0000-000000000142");

    expect(await screen.findByRole("heading", { name: "Revision r42" })).toBeInTheDocument();
    expect(screen.getByText("Desired-state records")).toBeInTheDocument();
    expect(screen.getByText("Payment Retry")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Deploy revision" })).toBeDisabled();
    expect(screen.getByText("This revision is already deployed in Dev.")).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/applications/claims-ops/environments/claims-dev")).toBeInTheDocument();
  });

  it("renders environment detail with engine cards that link to detail pages", async () => {
    renderDeployments(undefined, "/admin/deployments/applications/claims-ops/environments/claims-dev");

    expect(await screen.findByRole("heading", { name: "Dev" })).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/applications/claims-ops/environments/claims-dev/revisions/new")).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/applications/claims-ops/revisions/00000000-0000-0000-0000-000000000142")).toBeInTheDocument();
    expect(screen.getByText("Engine registrations")).toBeInTheDocument();
    expect(linkByHref("/admin/deployments/applications/claims-ops/environments/claims-dev/engines/dev-engine")).toBeInTheDocument();
    expect(screen.queryByText("Engine details")).not.toBeInTheDocument();
    expect(screen.getByText("Promotion")).toBeInTheDocument();
    expect(screen.getByText("Observability")).toBeInTheDocument();
    expect(screen.getAllByText("Run history").length).toBeGreaterThan(0);
  });

  it("links promotion blocker to source revision creation when valid artifacts exist", async () => {
    const cockpit = {
      ...deploymentCockpitFixture,
      applications: deploymentCockpitFixture.applications.map((application) => ({
        ...application,
        environments: application.environments.map((environment) =>
          environment.id === "claims-dev"
            ? { ...environment, desiredRevision: { id: "", revision: 0, commit: "", label: "", authoredAt: "" } }
            : environment)
      }))
    };
    renderDeployments(cockpit, "/admin/deployments/applications/claims-ops/environments/claims-dev");

    expect(await screen.findByText("Source revision")).toBeInTheDocument();
    expect(screen.getByText("Dev does not have a desired-state revision yet. Create or choose a source revision before previewing promotion.")).toBeInTheDocument();
    await waitFor(() =>
      expect(linkByHref("/admin/deployments/applications/claims-ops/environments/claims-dev/revisions/new")).toBeInTheDocument()
    );
  });

  it("explains production observability validation and links to a revision with a binding", async () => {
    const cockpit = {
      ...deploymentCockpitFixture,
      comparisons: [
        {
          ...deploymentCockpitFixture.comparisons[1],
          targetEnvironmentId: "claims-prod",
          targetRevision: 40,
          validations: [
            {
              id: "deployment.tier.observability-required",
              severity: "Blocker" as const,
              scope: "Observability",
              message: "Production requires at least one observability binding."
            }
          ]
        }
      ]
    };
    renderDeployments(cockpit, "/admin/deployments/applications/claims-ops/environments/claims-prod");

    expect(await screen.findByText("Deployment blockers")).toBeInTheDocument();
    expect((await screen.findAllByText("Production requires at least one observability binding.")).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/Production promotion requires the source revision to declare where runtime telemetry will be sent/).length).toBeGreaterThan(0);
    expect(linkByHref("/admin/deployments/applications/claims-ops/environments/claims-dev/revisions/new?includeRequirement=observability-binding")).toBeInTheDocument();
  });

  it("renders not-found states for unknown hierarchy ids", async () => {
    renderDeployments(undefined, "/admin/deployments/applications/missing-app");

    expect(await screen.findByText("Application not found")).toBeInTheDocument();
  });

  it("creates an environment and first engine from the application route", async () => {
    const fetchMock = renderDeployments(applicationWithoutSetupCockpit, "/admin/deployments/applications/policy-app/environments/new");

    expect(await screen.findByRole("heading", { name: "Add environment" })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText("Tier")).toHaveValue("tier-production"));
    await userEvent.clear(screen.getByLabelText("Environment"));
    await userEvent.type(screen.getByLabelText("Environment"), "Prod");
    await userEvent.type(screen.getByLabelText("Engine"), "policy-prod-weu-01");
    await userEvent.type(screen.getByLabelText("Base URL"), "https://policy-prod.example.test/elsa");
    await userEvent.type(screen.getByLabelText("Credential reference"), "kv://policy/prod/elsa-api");
    await userEvent.click(screen.getByRole("button", { name: "Add environment" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/applications/policy-app/environments`),
        expect.objectContaining({ method: "POST" })
      )
    );
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/environments/policy-prod/engines`),
      expect.objectContaining({ method: "POST" })
    );
  }, 15000);

  it("edits application metadata through a dedicated route", async () => {
    const fetchMock = renderDeployments(undefined, "/admin/deployments/applications/claims-ops/edit");

    expect(await screen.findByRole("heading", { name: "Edit application" })).toBeInTheDocument();
    await userEvent.clear(screen.getByLabelText("Application name"));
    await userEvent.type(screen.getByLabelText("Application name"), "Claims Platform");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/applications/claims-ops`),
        expect.objectContaining({ method: "PUT" })
      )
    );
  }, 15000);

  it("edits environment metadata through a dedicated route", async () => {
    const fetchMock = renderDeployments(undefined, "/admin/deployments/applications/claims-ops/environments/claims-dev/edit");

    expect(await screen.findByRole("heading", { name: "Edit environment" })).toBeInTheDocument();
    expect(screen.getByLabelText("Tier")).toHaveValue("tier-dev");
    await userEvent.clear(screen.getByLabelText("Environment"));
    await userEvent.type(screen.getByLabelText("Environment"), "Development");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/applications/claims-ops/environments/claims-dev`),
        expect.objectContaining({ method: "PUT" })
      )
    );
  });

  it("creates a desired-state revision from a registered artifact", async () => {
    const fetchMock = renderDeployments(undefined, "/admin/deployments/applications/claims-ops/environments/claims-dev/revisions/new");

    expect(await screen.findByRole("heading", { name: "New revision" })).toBeInTheDocument();
    expect((await screen.findAllByText("Payment Retry 8")).length).toBeGreaterThan(0);
    expect(screen.getByText("No additional desired-state records are required for Dev.")).toBeInTheDocument();
    expect(screen.queryByText("Observability binding")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "local:///tmp/payment-retry-dev.zip" })).toHaveAttribute(
      "href",
      `/api/workspaces/${workspaceId}/artifacts/11111111-1111-1111-1111-111111111111/download`
    );
    await userEvent.type(screen.getByLabelText("Revision label"), "Payment retry v8");
    await userEvent.type(screen.getByLabelText("Commit"), "8f6a9c1");
    await userEvent.click(screen.getByRole("button", { name: "Create revision" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/applications/claims-ops/environments/claims-dev/revisions`),
        expect.objectContaining({ method: "POST" })
      )
    );
    expect(requestBody(fetchMock, "POST", "/environments/claims-dev/revisions")).toMatchObject({
      label: "Payment retry v8",
      commit: "8f6a9c1",
      records: [
        {
          kind: "ArtifactReference",
          name: "Payment Retry 8",
          payload: {
            artifactRecordId: "11111111-1111-1111-1111-111111111111",
            artifactId: "sha256:payment-retry-dev",
            artifactTypeId: "elsa.workflow-definition",
            contentDigest: { algorithm: "sha256", value: "dev-digest" }
          }
        }
      ]
    });
  }, 15000);

  it("creates a desired-state revision with a contextual observability binding", async () => {
    const fetchMock = renderDeployments(undefined, "/admin/deployments/applications/claims-ops/environments/claims-dev/revisions/new?includeRequirement=observability-binding");

    expect(await screen.findByRole("heading", { name: "New revision" })).toBeInTheDocument();
    expect((await screen.findAllByText("Payment Retry 8")).length).toBeGreaterThan(0);
    expect(screen.getByText("Observability binding")).toBeInTheDocument();
    expect(screen.getByText("Included from a validation action for a target environment.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Create revision" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/applications/claims-ops/environments/claims-dev/revisions`),
        expect.objectContaining({ method: "POST" })
      )
    );
    expect(requestBody(fetchMock, "POST", "/environments/claims-dev/revisions")).toMatchObject({
      records: [
        { kind: "ArtifactReference" },
        {
          kind: "ObservabilityBinding",
          name: "Traces - OpenTelemetry Collector",
          payload: {
            kind: "Traces",
            provider: "OpenTelemetry Collector",
            scope: "Dev / workflow runtime"
          }
        }
      ]
    });
  }, 15000);

  it("shows and submits required observability for production revisions", async () => {
    const fetchMock = renderDeployments(undefined, "/admin/deployments/applications/claims-ops/environments/claims-prod/revisions/new");

    expect(await screen.findByRole("heading", { name: "New revision" })).toBeInTheDocument();
    expect(await screen.findByText("Observability binding")).toBeInTheDocument();
    expect(screen.getByText("Required by Production tier.")).toBeInTheDocument();
    expect(screen.getByText("Required")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Create revision" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/applications/claims-ops/environments/claims-prod/revisions`),
        expect.objectContaining({ method: "POST" })
      )
    );
    expect(requestBody(fetchMock, "POST", "/environments/claims-prod/revisions")).toMatchObject({
      records: [
        { kind: "ArtifactReference" },
        {
          kind: "ObservabilityBinding",
          payload: {
            kind: "Traces",
            provider: "OpenTelemetry Collector",
            scope: "Prod / workflow runtime"
          }
        }
      ]
    });
  }, 15000);

  it("edits engine metadata through a dedicated route", async () => {
    const fetchMock = renderDeployments(undefined, "/admin/deployments/applications/claims-ops/environments/claims-dev/engines/dev-engine/edit");

    expect(await screen.findByRole("heading", { name: "Edit engine" })).toBeInTheDocument();
    await userEvent.clear(screen.getByLabelText("Base URL"));
    await userEvent.type(screen.getByLabelText("Base URL"), "https://dev-workflows-2.acme.example/elsa");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/engines/dev-engine`),
        expect.objectContaining({ method: "PUT" })
      )
    );
  }, 15000);

  it("registers another workflow engine for an existing environment", async () => {
    const fetchMock = renderDeployments(undefined, "/admin/deployments/applications/claims-ops/environments/claims-dev/engines/new");

    expect(await screen.findByRole("heading", { name: "Register engine" })).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText("Engine"), "claims-dev-weu-02");
    await userEvent.type(screen.getByLabelText("Base URL"), "https://claims-dev-weu-02.example/elsa");
    await userEvent.type(screen.getByLabelText("Credential reference"), "kv://acme-platform/dev/elsa-api-secondary");
    await userEvent.click(screen.getByRole("button", { name: "Register engine" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/environments/claims-dev/engines`),
        expect.objectContaining({ method: "POST" })
      )
    );
    expect(requestBody(fetchMock, "POST", "/deployments/environments/claims-dev/engines")).toMatchObject({
      name: "claims-dev-weu-02",
      baseUrl: "https://claims-dev-weu-02.example/elsa",
      credentialProvider: "Azure Key Vault",
      credentialReference: "kv://acme-platform/dev/elsa-api-secondary"
    });
  }, 15000);

  it("verifies a selected workflow engine through the live API", async () => {
    const fetchMock = renderDeployments(undefined, "/admin/deployments/applications/claims-ops/environments/claims-dev/engines/dev-engine");

    expect((await screen.findAllByRole("heading", { name: "claims-dev-weu-01" })).length).toBeGreaterThan(0);
    expect(screen.getByText("Engine details")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Verify" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/engines/dev-engine/verify`),
        expect.objectContaining({ method: "POST" })
      )
    );
    expect(await screen.findByRole("status")).toHaveTextContent("Endpoint responded successfully.");
  });

  it("runs supported engine controls from an environment detail", async () => {
    renderDeployments(undefined, "/admin/deployments/applications/claims-ops/environments/claims-stage/engines/stage-engine");

    expect((await screen.findAllByRole("heading", { name: "claims-stage-weu-01" })).length).toBeGreaterThan(0);
    expect(screen.getByText("Engine details")).toBeInTheDocument();
    expect(screen.getAllByText("claims-stage-weu-01").length).toBeGreaterThan(0);
    expect(screen.getByText("Pause Processing")).toBeInTheDocument();
    expect(screen.getAllByText("Reload Configuration").length).toBeGreaterThan(0);
    expect(screen.queryByText("Restart Shell")).not.toBeInTheDocument();

    await userEvent.click(screen.getAllByRole("button", { name: "Run" })[1]);

    expect(await screen.findByRole("status")).toHaveTextContent("Reload Configuration executed for claims-stage-weu-01.");
  }, 15000);

  it("keeps promotion validation and deployment actions on environment detail", async () => {
    renderDeployments(undefined, "/admin/deployments/applications/claims-ops/environments/claims-prod");

    expect(await screen.findByRole("heading", { name: "Prod" })).toBeInTheDocument();
    expect(screen.getAllByText("Secret references").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Payment API secret reference is missing or not verified in Prod.").length).toBeGreaterThan(0);
    expect(screen.getByText(/Promotion creates a target desired-state revision in/)).toHaveTextContent("Promotion creates a target desired-state revision in Prod from Stage.");
    expect(screen.getByLabelText("Promote from")).toHaveValue("claims-stage");
    expect(screen.queryByLabelText("Promote into")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Deploy Target Revision" })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Roll Back to r39/i })).toBeEnabled();
  });

  it("promotes from a source-only environment into an eligible target", async () => {
    const fetchMock = renderDeployments(undefined, "/admin/deployments/applications/claims-ops/environments/claims-dev");

    expect(await screen.findByRole("heading", { name: "Dev" })).toBeInTheDocument();
    expect(screen.getByText(/Promotion creates a target desired-state revision in/)).toHaveTextContent("Promotion creates a target desired-state revision in Test from Dev.");
    expect(screen.queryByLabelText("Promote from")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Promote into")).toHaveValue("claims-test");
    await userEvent.click(screen.getByRole("button", { name: "Preview promotion" }));
    expect(await screen.findByText("Live validation passed for Test.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Create Target Revision" }));
    expect(await screen.findByRole("status")).toHaveTextContent("Target revision r43 created");
    await userEvent.click(screen.getByRole("button", { name: "Deploy Target Revision" }));
    expect(await screen.findByRole("status")).toHaveTextContent("Deployment run queued");

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/promotions/preview`),
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/promotions`),
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/api/workspaces/${workspaceId}/deployments/runs`),
      expect.objectContaining({ method: "POST" })
    );
    expect(requestBody(fetchMock, "POST", "/deployments/runs")).toMatchObject({
      sourceRevisionId: "00000000-0000-0000-0000-000000000243"
    });
  }, 10000);

  it("scopes promotion selectors to the current application and resets preview when direction changes", async () => {
    renderDeployments(multipleApplicationsCockpit, "/admin/deployments/applications/claims-ops/environments/claims-test");

    expect(await screen.findByRole("heading", { name: "Test" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Promote into this environment" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Promote from this environment" })).toBeInTheDocument();
    const sourceSelector = screen.getByLabelText("Promote from") as HTMLSelectElement;
    expect(sourceSelector).toHaveValue("claims-dev");
    expect(Array.from(sourceSelector.options).map((option) => option.value)).toEqual(["claims-dev", "claims-stage"]);

    await userEvent.click(screen.getByRole("button", { name: "Preview promotion" }));
    expect(await screen.findByText("Live validation passed for Test.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Promote from this environment" }));

    const targetSelector = screen.getByLabelText("Promote into") as HTMLSelectElement;
    expect(targetSelector).toHaveValue("claims-stage");
    expect(Array.from(targetSelector.options).map((option) => option.value)).toEqual(["claims-stage", "claims-prod"]);
    expect(screen.queryByText("Live validation passed for Test.")).not.toBeInTheDocument();
    expect(screen.getByText("No comparison available")).toBeInTheDocument();
  });

  it("explains why promotion preview is unavailable when the source revision is missing", async () => {
    renderDeployments(cockpitWithMissingSourceRevision(), "/admin/deployments/applications/claims-ops/environments/claims-dev");

    expect(await screen.findByRole("heading", { name: "Dev" })).toBeInTheDocument();
    expect(screen.getByText("Promotion requirements")).toBeInTheDocument();
    expect(screen.getByText("Dev does not have a desired-state revision yet. Create or choose a source revision before previewing promotion.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Preview promotion" })).toBeDisabled();
  });

  it("keeps assistant plans immutable and scoped to environment detail", async () => {
    renderDeployments(undefined, "/admin/deployments/applications/claims-ops/environments/claims-prod");

    expect(await screen.findByRole("heading", { name: "Prod" })).toBeInTheDocument();
    expect(screen.getByText("Assistant plan plan-20260522-001 v3")).toBeInTheDocument();
    expect(screen.getByText("Proposed actions")).toBeInTheDocument();
    expect(screen.getByText("Executed actions")).toBeInTheDocument();
    expect(screen.getByText("No platform mutations executed.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve Plan" })).toBeDisabled();

    await userEvent.click(screen.getByRole("button", { name: "Reject Plan" }));
    expect(screen.getByRole("status")).toHaveTextContent("Plan marked Rejected");
  }, 15000);

  it("shows deployment run history and drift on environment detail", async () => {
    renderDeployments(undefined, "/admin/deployments/applications/claims-ops/environments/claims-prod");

    expect(await screen.findByRole("heading", { name: "Prod" })).toBeInTheDocument();
    expect(screen.getByText("Deployment blockers")).toBeInTheDocument();
    expect(screen.getAllByText("Payment API secret reference is missing or not verified in Prod.").length).toBeGreaterThan(0);
    expect(screen.getAllByText("claims-prod-weu-01 does not advertise engine.reload-configuration.").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/claims-prod-weu-01 is unreachable/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText("Run history").length).toBeGreaterThan(0);
    expect(screen.getByText("Mira Chen")).toBeInTheDocument();
    expect(screen.getByText("Latest Blocked")).toBeInTheDocument();
    expect(screen.getByText("Deploy Running")).toBeInTheDocument();
    expect(screen.getByText("Applying workflow definitions")).toBeInTheDocument();
  });

  it("keeps setup panel tier behavior independent of routed pages", async () => {
    const submit = vi.fn();
    const firstProductionTier = tier("tier-production-old", "Production", 40, ["deployment.tier.production-like"]);
    const currentProductionTier = tier("tier-production-current", "Production", 40, ["deployment.tier.production-like"]);
    const { rerender } = render(
      <DeploymentSetupPanel
        canManageSetup
        tiers={[firstProductionTier]}
        isSubmitting={false}
        onSubmit={submit}
      />
    );

    await waitFor(() => expect(screen.getByLabelText("Tier")).toHaveValue("tier-production-old"));
    rerender(
      <DeploymentSetupPanel
        canManageSetup
        tiers={[currentProductionTier]}
        isSubmitting={false}
        onSubmit={submit}
      />
    );

    await waitFor(() => expect(screen.getByLabelText("Tier")).toHaveValue("tier-production-current"));
  });
});

function renderDeployments(cockpit: DeploymentCockpit = deploymentCockpitFixture, initialEntry = "/admin/deployments") {
  const fetchMock = createDeploymentFetchMock(cockpit);
  vi.stubGlobal("fetch", fetchMock);
  render(
    <TestQueryProvider>
      <MemoryRouter initialEntries={[initialEntry]}>
        <AuthProvider>
          <WorkspaceContextProvider>
            <Routes>
              <Route path="/admin/deployments" element={<DeploymentsPage />} />
              <Route path="/admin/deployments/new" element={<NewDeploymentSetupPage />} />
              <Route path="/admin/deployments/applications" element={<DeploymentApplicationsPage />} />
              <Route path="/admin/deployments/applications/:applicationId" element={<DeploymentApplicationPage />} />
              <Route path="/admin/deployments/applications/:applicationId/edit" element={<DeploymentApplicationEditPage />} />
              <Route path="/admin/deployments/applications/:applicationId/revisions" element={<DeploymentApplicationRevisionsPage />} />
              <Route path="/admin/deployments/applications/:applicationId/revisions/:revisionId" element={<DeploymentRevisionDetailPage />} />
              <Route path="/admin/deployments/applications/:applicationId/environments/new" element={<DeploymentEnvironmentCreatePage />} />
              <Route path="/admin/deployments/applications/:applicationId/environments/:environmentId" element={<DeploymentEnvironmentPage />} />
              <Route path="/admin/deployments/applications/:applicationId/environments/:environmentId/edit" element={<DeploymentEnvironmentEditPage />} />
              <Route path="/admin/deployments/applications/:applicationId/environments/:environmentId/revisions/new" element={<DeploymentRevisionCreatePage />} />
              <Route path="/admin/deployments/applications/:applicationId/environments/:environmentId/engines/new" element={<DeploymentEngineRegisterPage />} />
              <Route path="/admin/deployments/applications/:applicationId/environments/:environmentId/engines/:engineId" element={<DeploymentEnginePage />} />
              <Route path="/admin/deployments/applications/:applicationId/environments/:environmentId/engines/:engineId/edit" element={<DeploymentEngineEditPage />} />
            </Routes>
          </WorkspaceContextProvider>
        </AuthProvider>
      </MemoryRouter>
    </TestQueryProvider>
  );
  return fetchMock;
}

function desiredStateRequirements(environment: DeploymentCockpit["applications"][number]["environments"][number]) {
  const requiresObservability = environment.tierCapabilities?.includes("deployment.observability.required") ?? false;
  return {
    environmentId: environment.id,
    environmentName: environment.name,
    tierName: environment.tierName || environment.tier,
    tierCapabilities: environment.tierCapabilities ?? [],
    requirements: requiresObservability
      ? [
          {
            id: "observability-binding",
            capabilityId: "deployment.observability.required",
            recordKind: "ObservabilityBinding",
            label: "Observability binding",
            description: "Requires at least one logs, metrics, traces, or console telemetry binding.",
            validationId: "deployment.tier.observability-required",
            required: true,
            applicability: "CurrentTier"
          }
        ]
      : []
  };
}

function createDeploymentFetchMock(cockpit: DeploymentCockpit) {
  let currentCockpit = cockpit;
  return vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = input instanceof Request ? input.url : input.toString();
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    if (url.endsWith("/api/auth/session"))
      return jsonResponse({ loginEnabled: true, authenticated: true, displayName: "Test User", email: "test@example.com", loginPath: "/api/auth/login", logoutPath: "/api/auth/logout" });
    if (url.endsWith("/api/me/organizations"))
      return jsonResponse(workspaceContextFixture());
    if (url.endsWith(`/api/workspaces/${workspaceId}/deployments/permissions`)) {
      return jsonResponse({
        permissions: [
          "deployments.read",
          "deployments.setup.manage",
          "deployments.desired-state.manage",
          "deployments.promotion.preview",
          "deployments.run.execute",
          "deployments.rollback.execute",
          "deployments.controls.execute"
        ]
      });
    }
    if (url.endsWith(`/api/workspaces/${workspaceId}/deployments/tier-capabilities`)) {
      return jsonResponse({ capabilities: tierCapabilities });
    }
    if (url.endsWith(`/api/workspaces/${workspaceId}/deployments/tiers`)) {
      return jsonResponse({ tiers: deploymentTiers });
    }
    if (url.includes(`/api/workspaces/${workspaceId}/deployments/environments/`) && url.endsWith("/desired-state-requirements")) {
      const environmentId = decodeURIComponent(url.split("/deployments/environments/")[1]?.split("/desired-state-requirements")[0] ?? "");
      const environment = currentCockpit.applications.flatMap((application) => application.environments).find((item) => item.id === environmentId);
      return environment ? jsonResponse(desiredStateRequirements(environment)) : jsonResponse({ title: "Not found" }, 404);
    }
    if (url.endsWith(`/api/workspaces/${workspaceId}/artifacts`)) {
      return jsonResponse({ items: workspaceArtifacts });
    }
    if (url.endsWith(`/api/workspaces/${workspaceId}/deployments/applications/claims-ops/revisions`)) {
      return jsonResponse({ items: revisionSummaries(currentCockpit, "claims-ops") });
    }
    if (url.includes(`/api/workspaces/${workspaceId}/deployments/revisions/`)) {
      const revisionId = decodeURIComponent(url.split("/deployments/revisions/")[1] ?? "");
      const detail = revisionDetail(currentCockpit, revisionId);
      return detail ? jsonResponse(detail) : jsonResponse({ title: "Not found" }, 404);
    }
    if (url.endsWith(`/api/workspaces/${workspaceId}/deployments/cockpit`)) {
      return jsonResponse(currentCockpit);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/applications`)) {
      return jsonResponse({ id: "app-created", workspaceId, name: "Claims Operations" }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/applications/app-created/environments`)) {
      return jsonResponse({ id: "env-created", workspaceId, applicationId: "app-created", name: "Prod" }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/environments/env-created/engines`)) {
      currentCockpit = {
        ...deploymentCockpitFixture,
        applications: [{ ...deploymentCockpitFixture.applications[0], id: "app-created", name: "Claims Operations" }]
      };
      return jsonResponse({ id: "engine-created", name: "claims-prod", environmentId: "env-created" }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/applications/policy-app/environments`)) {
      return jsonResponse({ id: "policy-prod", workspaceId, applicationId: "policy-app", name: "Prod" }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/environments/policy-prod/engines`)) {
      currentCockpit = {
        ...currentCockpit,
        applications: currentCockpit.applications.map((application) =>
          application.id === "policy-app"
            ? {
                ...application,
                environments: [
                  ...application.environments,
                  {
                    id: "policy-prod",
                    name: "Prod",
                    tier: "Production",
                    tierId: "tier-production",
                    tierName: "Production",
                    tierStatus: "Active",
                    tierCapabilities: [
                      "deployment.tier.production-like",
                      "deployment.promotion.target",
                      "deployment.confirmation.required",
                      "deployment.rollback.enabled",
                      "deployment.secret-verification.required",
                      "deployment.observability.required"
                    ],
                    health: "Healthy",
                    desiredRevision: { id: "00000000-0000-0000-0000-000000000250", revision: 1, commit: "initial", label: "Initial desired state", authoredAt: "2026-05-26T10:00:00Z" },
                    deployedRevision: null,
                    deploymentStatus: "Succeeded",
                    driftStatus: "Unknown",
                    engineIds: ["policy-prod-engine"]
                  }
                ]
              }
            : application
        ),
        engines: [
          ...currentCockpit.engines,
          engine("policy-prod-engine", "policy-prod-weu-01", "policy-prod", "Healthy", "Verified", [
            capability("engine.reload-configuration", "Reload engine configuration", "EngineApi")
          ])
        ]
      };
      return jsonResponse({ id: "policy-prod-engine", name: "policy-prod-weu-01", environmentId: "policy-prod" }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/environments/claims-dev/engines`)) {
      currentCockpit = {
        ...currentCockpit,
        applications: currentCockpit.applications.map((application) =>
          application.id === "claims-ops"
            ? {
                ...application,
                environments: application.environments.map((environment) =>
                  environment.id === "claims-dev"
                    ? { ...environment, engineIds: [...environment.engineIds, "dev-engine-secondary"] }
                    : environment
                )
              }
            : application
        ),
        engines: [
          ...currentCockpit.engines,
          engine("dev-engine-secondary", "claims-dev-weu-02", "claims-dev", "Healthy", "Verified", [
            capability("engine.reload-configuration", "Reload engine configuration", "EngineApi")
          ])
        ]
      };
      return jsonResponse({ id: "dev-engine-secondary", name: "claims-dev-weu-02", environmentId: "claims-dev" }, 201);
    }
    if (method === "PUT" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/applications/claims-ops`)) {
      currentCockpit = {
        ...currentCockpit,
        applications: currentCockpit.applications.map((application) =>
          application.id === "claims-ops" ? { ...application, name: "Claims Platform" } : application
        )
      };
      return jsonResponse({ id: "claims-ops", workspaceId, name: "Claims Platform" });
    }
    if (method === "PUT" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/applications/claims-ops/environments/claims-dev`)) {
      currentCockpit = {
        ...currentCockpit,
        applications: currentCockpit.applications.map((application) => ({
          ...application,
          environments: application.environments.map((environment) =>
            environment.id === "claims-dev" ? { ...environment, name: "Development" } : environment
          )
        }))
      };
      return jsonResponse({ id: "claims-dev", workspaceId, applicationId: "claims-ops", name: "Development" });
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/applications/claims-ops/environments/claims-dev/revisions`)) {
      currentCockpit = {
        ...currentCockpit,
        applications: currentCockpit.applications.map((application) =>
          application.id === "claims-ops"
            ? {
                ...application,
                environments: application.environments.map((environment) =>
                  environment.id === "claims-dev"
                    ? {
                        ...environment,
                        desiredRevision: {
                          id: "00000000-0000-0000-0000-000000000242",
                          revision: 43,
                          commit: "8f6a9c1",
                          label: "Payment retry v8",
                          authoredAt: "2026-05-26T11:00:00Z"
                        }
                      }
                    : environment
                )
              }
            : application
        )
      };
      return jsonResponse({
        id: "00000000-0000-0000-0000-000000000242",
        workspaceId,
        applicationId: "claims-ops",
        environmentId: "claims-dev",
        revisionNumber: 43,
        label: "Payment retry v8",
        commit: "8f6a9c1",
        contentHash: "hash-v43",
        desiredStateJson: "{}",
        authoredAt: "2026-05-26T11:00:00Z",
        createdAt: "2026-05-26T11:00:00Z",
        createdByAccountId: null
      }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/applications/claims-ops/environments/claims-prod/revisions`)) {
      return jsonResponse({
        id: "00000000-0000-0000-0000-000000000243",
        workspaceId,
        applicationId: "claims-ops",
        environmentId: "claims-prod",
        revisionNumber: 41,
        label: "Payment Retry 8",
        commit: null,
        contentHash: "hash-v41",
        desiredStateJson: "{}",
        authoredAt: "2026-05-26T11:05:00Z",
        createdAt: "2026-05-26T11:05:00Z",
        createdByAccountId: null
      }, 201);
    }
    if (method === "PUT" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/engines/dev-engine`)) {
      currentCockpit = {
        ...currentCockpit,
        engines: currentCockpit.engines.map((item) =>
          item.id === "dev-engine"
            ? { ...item, endpoint: { ...item.endpoint, baseUrl: "https://dev-workflows-2.acme.example/elsa" } }
            : item
        )
      };
      return jsonResponse({ id: "dev-engine", name: "claims-dev-weu-01", environmentId: "claims-dev" });
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/engines/dev-engine/verify`)) {
      currentCockpit = {
        ...currentCockpit,
        engines: currentCockpit.engines.map((item) =>
          item.id === "dev-engine"
            ? {
                ...item,
                health: "Healthy",
                lastHeartbeatAt: "2026-05-26T10:05:00Z",
                lastVerificationAt: "2026-05-26T10:05:00Z",
                verificationMessage: "Endpoint responded successfully.",
                credentialReference: {
                  ...item.credentialReference,
                  verificationStatus: "Verified",
                  lastVerifiedAt: "2026-05-26T10:05:00Z"
                }
              }
            : item
        )
      };
      return jsonResponse({
        engineId: "dev-engine",
        environmentId: "claims-dev",
        health: "Healthy",
        version: "Elsa 4.0.1",
        certificateStatus: "Trusted",
        credentialVerificationStatus: "Verified",
        credentialLastVerifiedAt: "2026-05-26T10:05:00Z",
        lastHeartbeatAt: "2026-05-26T10:05:00Z",
        lastVerificationAt: "2026-05-26T10:05:00Z",
        message: "Endpoint responded successfully."
      });
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/confirmations`)) {
      const body = JSON.parse(init?.body?.toString() ?? "{}") as { actionType?: string; targetId?: string };
      return jsonResponse({ id: `${body.actionType ?? "action"}-confirmation-1`, workspaceId, actionType: body.actionType, targetId: body.targetId }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/promotions/preview`)) {
      return jsonResponse({
        ...deploymentCockpitFixture.comparisons[1],
        validations: [
          { id: "secret-payment-test", severity: "Pass", scope: "Secret references", message: "Live validation passed for Test." },
          { id: "capability-test", severity: "Pass", scope: "Engine capabilities", message: "claims-test-weu-01 supports required engine operations." }
        ]
      });
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/promotions`)) {
      return jsonResponse({
        sourceRevision: {
          id: "00000000-0000-0000-0000-000000000142",
          workspaceId,
          applicationId: "claims-ops",
          environmentId: "claims-dev",
          revisionNumber: 42,
          label: "Payment retry workflow",
          commit: "8f6a9c1",
          contentHash: "source-hash",
          desiredStateJson: "{}"
        },
        targetRevision: {
          id: "00000000-0000-0000-0000-000000000243",
          workspaceId,
          applicationId: "claims-ops",
          environmentId: "claims-test",
          revisionNumber: 43,
          label: "Promoted from Dev r42",
          commit: "8f6a9c1",
          contentHash: "target-hash",
          desiredStateJson: "{}"
        },
        comparison: {
          ...deploymentCockpitFixture.comparisons[1],
          validations: [
            { id: "secret-payment-test", severity: "Pass", scope: "Secret references", message: "Live validation passed for Test." },
            { id: "capability-test", severity: "Pass", scope: "Engine capabilities", message: "claims-test-weu-01 supports required engine operations." }
          ]
        }
      }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/runs`)) {
      currentCockpit = {
        ...currentCockpit,
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
          },
          ...currentCockpit.history
        ]
      };
      return jsonResponse({
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
        confirmationId: "Deploy-confirmation-1",
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
      }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/rollbacks`)) {
      currentCockpit = {
        ...currentCockpit,
        history: [
          {
            id: "00000000-0000-0000-0000-000000000778",
            status: "Queued",
            revision: 39,
            actor: "account-1",
            environmentId: "claims-prod",
            engineId: "prod-engine",
            validationOutcome: "Warnings",
            occurredAt: "2026-05-26T10:02:00Z",
            rollbackSourceRevision: 41
          },
          ...currentCockpit.history
        ]
      };
      return jsonResponse({
        id: "00000000-0000-0000-0000-000000000778",
        workspaceId,
        applicationId: "claims-ops",
        environmentId: "claims-prod",
        engineId: "prod-engine",
        sourceRevisionId: "00000000-0000-0000-0000-000000000139",
        previousDeployedRevisionId: "00000000-0000-0000-0000-000000000141",
        rollbackSourceRunId: "00000000-0000-0000-0000-000000000410",
        status: "Queued",
        validationOutcome: "Warnings",
        confirmationId: "Rollback-confirmation-1",
        actorAccountId: "account-1",
        queuedAt: "2026-05-26T10:02:00Z",
        startedAt: null,
        completedAt: null,
        createdAt: "2026-05-26T10:02:00Z",
        workerId: null,
        workerHeartbeatAt: null,
        attemptNumber: 1,
        recoveryReason: null,
        failureMessage: null
      }, 201);
    }
    if (method === "POST" && url.endsWith(`/api/workspaces/${workspaceId}/deployments/engines/stage-engine/controls/reload-configuration/run`)) {
      return jsonResponse({
        id: "control-execution-1",
        workspaceId,
        engineId: "stage-engine",
        environmentId: "claims-stage",
        controlId: "reload-configuration",
        controlLabel: "Reload Configuration",
        boundary: "EngineApi",
        requiredCapabilityId: "engine.reload-configuration",
        confirmationId: "confirmation-1",
        actorAccountId: "account-1",
        status: "Succeeded",
        createdAt: "2026-05-26T10:00:00Z",
        message: "Reload Configuration executed for claims-stage-weu-01."
      });
    }
    return jsonResponse({ title: "Not found" }, 404);
  });
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function requestBody(fetchMock: ReturnType<typeof createDeploymentFetchMock>, method: string, urlSuffix: string) {
  const call = fetchMock.mock.calls.find(([input, init]) => {
    const url = input instanceof Request ? input.url : input.toString();
    return url.includes(urlSuffix) && init?.method === method;
  });
  expect(call).toBeDefined();
  return JSON.parse(call![1]?.body?.toString() ?? "{}") as Record<string, unknown>;
}

function linkByHref(href: string) {
  const link = screen.getAllByRole("link").find((item) => item.getAttribute("href") === href);
  expect(link).toBeDefined();
  return link!;
}

function workspaceContextFixture() {
  return {
    account: { id: "account-1", displayName: "Test User", email: "test@example.com" },
    organizations: [{ id: organizationId, name: "Acme Corp", role: "Owner" }],
    workspaces: [
      { id: workspaceId, name: "Acme Insurance", kind: "Shared", role: "Owner", organizationId, organizationName: "Acme Corp", organizationRole: "Owner" },
      { id: "00000000-0000-0000-0000-000000000011", name: "Acme Labs", kind: "Shared", role: "Reader", organizationId, organizationName: "Acme Corp", organizationRole: "Owner" }
    ]
  };
}

function revisionSummaries(cockpit: DeploymentCockpit, applicationId: string): WorkspaceDesiredStateRevisionSummary[] {
  const application = cockpit.applications.find((item) => item.id === applicationId);
  if (!application) return [];

  return application.environments.map((environment) => {
    const latestRun = cockpit.history.find((event) => event.environmentId === environment.id && event.revision === environment.desiredRevision.revision);
    return {
      revision: {
        id: environment.desiredRevision.id,
        workspaceId,
        applicationId: application.id,
        environmentId: environment.id,
        revisionNumber: environment.desiredRevision.revision,
        label: environment.desiredRevision.label,
        commit: environment.desiredRevision.commit || null,
        contentHash: `hash-${environment.desiredRevision.revision}`,
        desiredStateJson: JSON.stringify({
          records: [
            {
              kind: "ArtifactReference",
              name: environment.desiredRevision.label,
              payload: {
                artifactId: `artifact-${environment.id}`,
                contentDigest: { algorithm: "sha256", value: `digest-${environment.desiredRevision.revision}` }
              }
            }
          ]
        }),
        authoredAt: environment.desiredRevision.authoredAt,
        createdAt: environment.desiredRevision.authoredAt,
        createdByAccountId: null
      },
      environmentName: environment.name,
      environmentTier: environment.tier,
      environmentTierId: environment.tierId ?? null,
      environmentTierName: environment.tierName ?? null,
      isCurrentDesired: true,
      isCurrentDeployed: environment.deployedRevision === environment.desiredRevision.revision,
      latestRunStatus: latestRun?.status ?? null,
      latestRunQueuedAt: latestRun?.occurredAt ?? null
    };
  });
}

function revisionDetail(cockpit: DeploymentCockpit, revisionId: string): WorkspaceDesiredStateRevisionDetail | null {
  const summary = revisionSummaries(cockpit, "claims-ops").find((item) => item.revision.id === revisionId);
  if (!summary) return null;
  const runs = cockpit.history
    .filter((event) => event.environmentId === summary.revision.environmentId && event.revision === summary.revision.revisionNumber)
    .map((event) => ({
      id: event.id,
      environmentId: event.environmentId,
      engineId: event.engineId,
      status: event.status,
      validationOutcome: event.validationOutcome,
      queuedAt: event.occurredAt,
      completedAt: event.occurredAt,
      failureMessage: null
    }));

  return {
    summary,
    records: [
      {
        id: `${revisionId}:artifact`,
        kind: "ArtifactReference",
        name: "Payment Retry",
        payloadJson: JSON.stringify({
          artifactId: "payment-retry",
          contentDigest: { algorithm: "sha256", value: "stage-digest" }
        }),
        contentHash: "record-hash",
        artifactRecordId: "00000000-0000-0000-0000-000000000900",
        artifactId: "payment-retry",
        artifactTypeId: "elsa.workflow-definition",
        artifactDigest: { algorithm: "sha256", value: "stage-digest" }
      }
    ],
    runs
  };
}

function TestQueryProvider({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false }
    }
  });

  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

const organizationId = "00000000-0000-0000-0000-000000000001";
const workspaceId = "00000000-0000-0000-0000-000000000010";
const tierCapabilities = [
  capabilityDefinition("deployment.tier.development-like", "Development-like", "Classification"),
  capabilityDefinition("deployment.tier.test-like", "Test-like", "Classification"),
  capabilityDefinition("deployment.tier.preproduction-like", "Pre-production-like", "Classification"),
  capabilityDefinition("deployment.tier.production-like", "Production-like", "Classification"),
  capabilityDefinition("deployment.promotion.source", "Promotion source", "Promotion"),
  capabilityDefinition("deployment.promotion.target", "Promotion target", "Promotion"),
  capabilityDefinition("deployment.confirmation.required", "Confirmation required", "Safeguards"),
  capabilityDefinition("deployment.rollback.enabled", "Rollback enabled", "Rollback"),
  capabilityDefinition("deployment.secret-verification.required", "Secret verification required", "Validation"),
  capabilityDefinition("deployment.observability.required", "Observability required", "Observability")
];
const deploymentTiers = [
  tier("tier-dev", "Dev", 10, ["deployment.tier.development-like", "deployment.promotion.source"]),
  tier("tier-test", "Test", 20, ["deployment.tier.test-like", "deployment.promotion.source", "deployment.promotion.target"]),
  tier("tier-stage", "Stage", 30, ["deployment.tier.preproduction-like", "deployment.promotion.source", "deployment.promotion.target", "deployment.secret-verification.required"]),
  tier("tier-production", "Production", 40, [
    "deployment.tier.production-like",
    "deployment.promotion.target",
    "deployment.confirmation.required",
    "deployment.rollback.enabled",
    "deployment.secret-verification.required",
    "deployment.observability.required"
  ]),
  tier("tier-uat", "UAT", 35, ["deployment.tier.preproduction-like", "deployment.promotion.target"]),
  tier("tier-legacy-prod", "Legacy Production", 50, ["deployment.tier.production-like"], "Archived")
];
const workspaceArtifacts = [
  {
    id: "11111111-1111-1111-1111-111111111111",
    workspaceId,
    artifactId: "sha256:payment-retry-dev",
    layoutVersion: "platform.elsa.io/deployment-artifact/v1alpha1",
    contentDigest: { algorithm: "sha256", value: "dev-digest" },
    format: "Zip",
    referenceProvider: "local",
    reference: "local:///tmp/payment-retry-dev.zip",
    manifest: { name: "Payment Retry", version: "8", environment: "Dev" },
    resources: [],
    checksumStatus: "Verified",
    inspectionStatus: "Valid",
    diagnostics: [],
    registeredAt: "2026-05-26T10:00:00Z",
    registeredByAccountId: "account-1",
    lastInspectedAt: "2026-05-26T10:00:00Z",
    createdAt: "2026-05-26T10:00:00Z",
    updatedAt: "2026-05-26T10:00:00Z",
    envelopeVersion: "platform.elsa.io/artifact-envelope/v1alpha1",
    artifactTypeId: "elsa.workflow-definition",
    artifactSchemaVersion: "1.0",
    manifestDigest: { algorithm: "sha256", value: "manifest-digest" },
    payloadReference: null,
    producer: null,
    displayMetadata: {
      name: "Payment Retry",
      version: "8",
      description: "Payment retry workflow",
      labels: { workflow: "payment-retry" },
      annotations: {},
      source: null
    },
    compatibilityHints: null
  }
] as const;

const deploymentCockpitFixture: DeploymentCockpit = {
  applications: [
    {
      id: "claims-ops",
      name: "Claims Operations",
      workspaceName: "Acme Insurance",
      environments: [
        {
          id: "claims-dev",
          name: "Dev",
          tier: "Dev",
          tierId: "tier-dev",
          tierName: "Dev",
          tierStatus: "Active",
          tierCapabilities: ["deployment.tier.development-like", "deployment.promotion.source"],
          health: "Healthy",
          desiredRevision: { id: "00000000-0000-0000-0000-000000000142", revision: 42, commit: "8f6a9c1", label: "Payment retry workflow", authoredAt: "2026-05-21T08:30:00Z" },
          deployedRevision: 42,
          deploymentStatus: "Succeeded",
          driftStatus: "InSync",
          engineIds: ["dev-engine"]
        },
        {
          id: "claims-test",
          name: "Test",
          tier: "Test",
          tierId: "tier-test",
          tierName: "Test",
          tierStatus: "Active",
          tierCapabilities: ["deployment.tier.test-like", "deployment.promotion.source", "deployment.promotion.target"],
          health: "Healthy",
          desiredRevision: { id: "00000000-0000-0000-0000-000000000139", revision: 39, commit: "79d1b07", label: "Fraud review tuning", authoredAt: "2026-05-20T13:20:00Z" },
          deployedRevision: 39,
          deploymentStatus: "Succeeded",
          driftStatus: "InSync",
          engineIds: ["test-engine"]
        },
        {
          id: "claims-stage",
          name: "Stage",
          tier: "Stage",
          tierId: "tier-stage",
          tierName: "Stage",
          tierStatus: "Active",
          tierCapabilities: ["deployment.tier.preproduction-like", "deployment.promotion.source", "deployment.promotion.target", "deployment.secret-verification.required"],
          health: "Degraded",
          desiredRevision: { id: "00000000-0000-0000-0000-000000000141", revision: 41, commit: "c174f2a", label: "Policy document sync", authoredAt: "2026-05-21T06:10:00Z" },
          deployedRevision: 40,
          deploymentStatus: "Running",
          driftStatus: "DriftDetected",
          engineIds: ["stage-engine"]
        },
        {
          id: "claims-prod",
          name: "Prod",
          tier: "Production",
          tierId: "tier-production",
          tierName: "Production",
          tierStatus: "Active",
          tierCapabilities: [
            "deployment.tier.production-like",
            "deployment.promotion.target",
            "deployment.confirmation.required",
            "deployment.rollback.enabled",
            "deployment.secret-verification.required",
            "deployment.observability.required"
          ],
          health: "Unreachable",
          desiredRevision: { id: "00000000-0000-0000-0000-000000000140", revision: 40, commit: "11ec9d4", label: "Baseline production", authoredAt: "2026-05-19T15:45:00Z" },
          deployedRevision: 40,
          deploymentStatus: "Blocked",
          driftStatus: "Unknown",
          engineIds: ["prod-engine"]
        }
      ]
    }
  ],
  engines: [
    engine("dev-engine", "claims-dev-weu-01", "claims-dev", "Healthy", "Verified", [
      capability("workflow.pause-processing", "Pause workflow processing", "Workflow"),
      capability("engine.reload-configuration", "Reload engine configuration", "EngineApi")
    ]),
    engine("test-engine", "claims-test-weu-01", "claims-test", "Healthy", "Verified", [
      capability("workflow.pause-processing", "Pause workflow processing", "Workflow"),
      capability("engine.reload-configuration", "Reload engine configuration", "EngineApi")
    ]),
    engine("stage-engine", "claims-stage-weu-01", "claims-stage", "Degraded", "Verified", [
      capability("workflow.pause-processing", "Pause workflow processing", "Workflow"),
      capability("engine.reload-configuration", "Reload engine configuration", "EngineApi")
    ]),
    engine("prod-engine", "claims-prod-weu-01", "claims-prod", "Unreachable", "Missing", [
      capability("workflow.pause-processing", "Pause workflow processing", "Workflow")
    ])
  ],
  comparisons: [
    {
      sourceEnvironmentId: "claims-stage",
      targetEnvironmentId: "claims-prod",
      sourceRevisionId: "00000000-0000-0000-0000-000000000141",
      sourceRevision: 41,
      targetRevision: 40,
      rollbackRevisionId: "00000000-0000-0000-0000-000000000139",
      rollbackRevision: 39,
      diff: [
        { id: "workflow-payment-retry", category: "Workflows", name: "Payment Retry", sourceValue: "v7 with idempotent retry", targetValue: "v6", impact: "Changed" },
        { id: "secret-payment-api", category: "SecretReferences", name: "Payment API", sourceValue: "kv://acme-platform/prod/payment-api:v3", targetValue: "Missing reference", impact: "Changed" }
      ],
      validations: [
        { id: "secret-payment-api", severity: "Blocker", scope: "Secret references", message: "Payment API secret reference is missing or not verified in Prod." },
        { id: "capability-reload", severity: "Blocker", scope: "Engine capabilities", message: "claims-prod-weu-01 does not advertise engine.reload-configuration." }
      ],
      artifacts: [
        artifactComparison("Payment Retry", "sha256:payment-retry-stage", "stage-digest", "Changed", {
          displayName: "Payment Retry",
          version: "7"
        })
      ]
    },
    {
      sourceEnvironmentId: "claims-dev",
      targetEnvironmentId: "claims-test",
      sourceRevisionId: "00000000-0000-0000-0000-000000000142",
      sourceRevision: 42,
      targetRevision: 39,
      rollbackRevisionId: "00000000-0000-0000-0000-000000000138",
      rollbackRevision: 38,
      diff: [
        { id: "workflow-payment-retry-test", category: "Workflows", name: "Payment Retry", sourceValue: "v8", targetValue: "v6", impact: "Changed" },
        { id: "secret-payment-test", category: "SecretReferences", name: "Payment API", sourceValue: "kv://acme-platform/test/payment-api:v3", targetValue: "kv://acme-platform/test/payment-api:v2", impact: "Changed" }
      ],
      validations: [
        { id: "secret-payment-test", severity: "Pass", scope: "Secret references", message: "Required secret references are verified for Test." },
        { id: "capability-test", severity: "Pass", scope: "Engine capabilities", message: "claims-test-weu-01 supports required engine operations." }
      ],
      artifacts: [
        artifactComparison("Payment Retry", "sha256:payment-retry-dev", "dev-digest", "Changed", {
          displayName: "Payment Retry",
          version: "8"
        })
      ]
    }
  ],
  observabilityBindings: [
    { id: "prod-logs", kind: "Logs", provider: "Azure Monitor", status: "Connected", scope: "claims-prod / rev 40", correlatedRevision: 40, sample: "143 structured events in the last 30 minutes" }
  ],
  history: [
    {
      id: "00000000-0000-0000-0000-000000000410",
      status: "Blocked",
      revision: 41,
      actor: "Mira Chen",
      environmentId: "claims-prod",
      engineId: "prod-engine",
      validationOutcome: "Blocked",
      occurredAt: "2026-05-22T08:05:00Z",
      rollbackSourceRevision: null,
      commands: [
        {
          id: "command-1",
          workspaceId,
          runId: "00000000-0000-0000-0000-000000000410",
          environmentId: "claims-prod",
          engineId: "prod-engine",
          action: "Deploy",
          status: "Running",
          artifact: commandArtifact("sha256:payment-retry-stage", "stage-digest"),
          workerId: "runtime-worker-1",
          claimedAt: "2026-05-22T08:06:00Z",
          leaseExpiresAt: "2026-05-22T08:11:00Z",
          heartbeatAt: "2026-05-22T08:07:00Z",
          attemptNumber: 1,
          percentComplete: 75,
          progressMessage: "Applying workflow definitions",
          observedArtifactDigest: null,
          runtimeReference: "elsa://workflows/payment-retry",
          diagnostics: [],
          createdAt: "2026-05-22T08:05:00Z",
          updatedAt: "2026-05-22T08:07:00Z",
          completedAt: null
        }
      ]
    }
  ],
  driftReport: [
    { id: "drift-shell", environmentId: "claims-stage", engineId: "stage-engine", area: "Shell concurrency", desired: "16 workers", observed: "12 workers", action: "Review" }
  ],
  assistantPlans: [
    {
      id: "plan-20260522-001",
      version: 3,
      status: "Proposed",
      workspaceName: "Acme Insurance",
      targetEnvironmentId: "claims-prod",
      targetEngineId: "prod-engine",
      summary: "Promote revision 41 from Stage to Prod after validating secrets, reachability, and engine reload capability.",
      proposedActions: [
        "Verify Prod secret references and provider access",
        "Run desired-state diff from Stage revision 41 to Prod revision 40",
        "Apply revision 41 to claims-prod-weu-01 as one deployment",
        "Keep rollback path to revision 39 available"
      ],
      executedActions: [],
      validations: [
        { id: "assistant-scope", severity: "Pass", scope: "Workspace authorization", message: "Plan is scoped to Acme Insurance only." },
        { id: "assistant-secret", severity: "Blocker", scope: "Secret references", message: "Payment API reference must verify before approval can execute." }
      ],
      rollbackPath: "Redeploy revision 39 to claims-prod-weu-01 if revision 41 fails after validation clears.",
      allOrNothing: true,
      createdAt: "2026-05-22T08:02:00Z"
    }
  ]
};

const applicationWithoutSetupCockpit: DeploymentCockpit = {
  applications: [
    {
      id: "policy-app",
      name: "Policy",
      workspaceName: "Acme Insurance",
      environments: []
    }
  ],
  engines: [],
  comparisons: [],
  observabilityBindings: [],
  history: [],
  driftReport: [],
  assistantPlans: []
};

function cockpitWithMissingSourceRevision(): DeploymentCockpit {
  return {
    ...deploymentCockpitFixture,
    applications: deploymentCockpitFixture.applications.map((application) => application.id !== "claims-ops"
      ? application
      : {
        ...application,
        environments: application.environments.map((environment) => environment.id === "claims-dev"
          ? {
            ...environment,
            desiredRevision: {
              ...environment.desiredRevision,
              id: "",
              revision: 0,
              commit: "",
              label: "No desired revision"
            }
          }
          : environment)
      })
  };
}

const multipleApplicationsCockpit: DeploymentCockpit = {
  ...deploymentCockpitFixture,
  applications: [
    ...deploymentCockpitFixture.applications,
    {
      id: "policy-app",
      name: "Policy",
      workspaceName: "Acme Insurance",
      environments: [
        {
          id: "policy-prod",
          name: "Prod",
          tier: "Production",
          tierId: "tier-production",
          tierName: "Production",
          tierStatus: "Active",
          tierCapabilities: ["deployment.tier.production-like", "deployment.promotion.target"],
          health: "Healthy",
          desiredRevision: { id: "00000000-0000-0000-0000-000000000250", revision: 1, commit: "initial", label: "Initial desired state", authoredAt: "2026-05-26T10:00:00Z" },
          deployedRevision: 1,
          deploymentStatus: "Succeeded",
          driftStatus: "InSync",
          engineIds: ["policy-prod-engine"]
        }
      ]
    }
  ],
  engines: [
    ...deploymentCockpitFixture.engines,
    engine("policy-prod-engine", "policy-prod-weu-01", "policy-prod", "Healthy", "Verified", [
      capability("engine.reload-configuration", "Reload engine configuration", "EngineApi")
    ])
  ]
};

function engine(
  id: string,
  name: string,
  environmentId: string,
  health: DeploymentCockpit["engines"][number]["health"],
  verificationStatus: DeploymentCockpit["engines"][number]["credentialReference"]["verificationStatus"],
  capabilities: DeploymentCockpit["engines"][number]["capabilities"]
): DeploymentCockpit["engines"][number] {
  return {
    id,
    name,
    environmentId,
    endpoint: {
      baseUrl: `https://${name}.example/elsa`,
      region: "West Europe",
      version: "Elsa 4.0.1",
      certificateStatus: "Trusted"
    },
    credentialReference: {
      provider: "Azure Key Vault",
      reference: `kv://acme-platform/${environmentId.replace("claims-", "")}/elsa-api`,
      verificationStatus,
      lastVerifiedAt: verificationStatus === "Verified" ? "2026-05-22T07:50:00Z" : null
    },
    health,
    lastHeartbeatAt: health === "Unreachable" ? null : "2026-05-22T08:16:30Z",
    lastVerificationAt: health === "Unreachable" ? null : "2026-05-22T08:12:30Z",
    verificationMessage: health === "Unreachable" ? "Engine has not been verified." : "Endpoint responded successfully.",
    capabilities,
    controls: [
      { id: "pause-processing", label: "Pause Processing", boundary: "Workflow", capabilityId: "workflow.pause-processing", description: "Stops new workflow dispatch without touching host infrastructure." },
      { id: "reload-configuration", label: "Reload Configuration", boundary: "EngineApi", capabilityId: "engine.reload-configuration", description: "Reloads engine API configuration from desired state." },
      { id: "restart-shell", label: "Restart Shell", boundary: "Shell", capabilityId: "shell.restart", description: "Hidden until the shell restart capability is advertised." }
    ],
    hostingProvider: null
  };
}

function artifactComparison(
  name: string,
  artifactId: string,
  digest: string,
  impact: DeploymentCockpit["comparisons"][number]["artifacts"][number]["impact"],
  metadata: Record<string, string>
): DeploymentCockpit["comparisons"][number]["artifacts"][number] {
  return {
    name,
    source: {
      artifactRecordId: "00000000-0000-0000-0000-000000000900",
      artifactId,
      artifactTypeId: "elsa.workflow-definition",
      contentDigest: { algorithm: "sha256", value: digest },
      metadata,
      configuration: { environment: "stage" },
      compatibilityHints: ["workflow-definition.apply"]
    },
    target: {
      artifactRecordId: "00000000-0000-0000-0000-000000000899",
      artifactId: "sha256:payment-retry-prod",
      artifactTypeId: "elsa.workflow-definition",
      contentDigest: { algorithm: "sha256", value: "prod-digest" },
      metadata: { displayName: "Payment Retry", version: "6" },
      configuration: { environment: "prod" },
      compatibilityHints: ["workflow-definition.apply"]
    },
    impact,
    runtimeCompatibility: [{ id: `${artifactId}-runtime`, severity: "Pass", scope: "Runtime compatibility", message: "Workflow runtime capability is present." }]
  };
}

function commandArtifact(artifactId: string, digest: string): NonNullable<DeploymentCockpit["history"][number]["commands"]>[number]["artifact"] {
  return {
    artifactRecordId: "00000000-0000-0000-0000-000000000900",
    artifactId,
    artifactTypeId: "elsa.workflow-definition",
    contentDigest: { algorithm: "sha256", value: digest }
  };
}

function capability(
  id: string,
  label: string,
  boundary: DeploymentCockpit["engines"][number]["capabilities"][number]["boundary"]
) {
  return { id, label, boundary };
}

function capabilityDefinition(id: string, label: string, category: string) {
  return {
    id,
    label,
    description: label,
    category,
    isDeprecated: false
  };
}

function tier(id: string, name: string, sortOrder: number, capabilities: string[], status: WorkspaceDeploymentTier["status"] = "Active"): WorkspaceDeploymentTier {
  return {
    id,
    workspaceId,
    name,
    description: null,
    sortOrder,
    isDefault: name === "Dev" || name === "Test" || name === "Stage" || name === "Production",
    status,
    capabilities,
    environmentCount: 0,
    createdAt: "2026-05-28T10:00:00Z",
    updatedAt: "2026-05-28T10:00:00Z",
    createdByAccountId: null,
    updatedByAccountId: null,
    archivedAt: status === "Archived" ? "2026-05-28T10:00:00Z" : null,
    archivedByAccountId: null
  };
}
