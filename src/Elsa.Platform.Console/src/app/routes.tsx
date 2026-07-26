import { createBrowserRouter, Link, Navigate } from "react-router-dom";
import { AppShell } from "@/app/AppShell";
import { OverviewPage } from "@/app/OverviewPage";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { Button, EmptyState, buttonClassName } from "@/components/ui";
import {
  DeploymentApplicationEditPage,
  DeploymentApplicationsPage,
  DeploymentApplicationPage,
  DeploymentApplicationRevisionsPage,
  DeploymentCredentialReferenceCreatePage,
  DeploymentCredentialReferenceEditPage,
  DeploymentCredentialReferencePage,
  DeploymentCredentialStoreCreatePage,
  DeploymentCredentialStoreEditPage,
  DeploymentEnginePage,
  DeploymentEngineEditPage,
  DeploymentEngineRegisterPage,
  DeploymentCredentialsPage,
  DeploymentEnvironmentCreatePage,
  DeploymentEnvironmentEditPage,
  DeploymentEnvironmentPage,
  DeploymentRevisionDetailPage,
  DeploymentRevisionCreatePage,
  DeploymentsPage,
  NewDeploymentSetupPage
} from "@/features/deployments/DeploymentsPage";
import { DeploymentTierCreatePage, DeploymentTierEditPage, DeploymentTiersPage } from "@/features/deployments/DeploymentTiersPage";
import { ArtifactCreatePage, ArtifactDetailsPage, ArtifactsPage } from "@/features/artifacts/ArtifactsPage";
import { RequireCustomerAuth, useAuth } from "@/lib/auth/AuthProvider";
import { NewSourcePage, EditSourcePage } from "@/features/sources/SourceFormPage";
import { SourceDetailsPage } from "@/features/sources/SourceDetailsPage";
import { SourcesPage } from "@/features/sources/SourcesPage";
import { PackageDetailsPage } from "@/features/packages/PackageDetailsPage";
import { PackagesPage } from "@/features/packages/PackagesPage";
import { EditRuntimeBuilderPage, NewRuntimeBuilderPage, RuntimeBuilderPage } from "@/features/runtime-builder/RuntimeBuilderPage";
import { SyncRunDetailsPage } from "@/features/sync-runs/SyncRunDetailsPage";
import { SyncRunsPage } from "@/features/sync-runs/SyncRunsPage";
import { WeaverSessionPage } from "@/features/weaver/WeaverSessionPage";
import { ConsoleLogsPage } from "@/features/console/ConsoleLogsPage";
import { HealingComponentsPage, HealingConfigurationPage } from "@/features/healing/HealingConfigurationPage";
import { HealingIncidentPage } from "@/features/healing/HealingIncidentPage";
import { HealingIncidentsPage } from "@/features/healing/HealingIncidentsPage";
import { HealingAuditPage, HealingOverviewPage } from "@/features/healing/HealingOverviewPage";

function PlaceholderPage({ title }: { title: string }) {
  return (
    <section className="space-y-2">
      <h1 className="font-display text-xl font-semibold">{title}</h1>
      <p className="text-sm text-muted-foreground">This operational view is ready for feature implementation.</p>
    </section>
  );
}

export function AdminLoginPage() {
  const auth = useAuth();

  if (auth.isLoading)
    return <RequestStateView state="loading" title="Checking customer session" />;

  if (auth.session?.authenticated) {
    return (
      <EmptyState
        title="You are already signed in"
        description="Your customer session is active. Continue to the platform overview or use the account menu when you need to sign out."
        action={<Link to="/admin/overview" className={buttonClassName()}>Open overview</Link>}
      />
    );
  }

  if (!auth.session?.loginEnabled) {
    return (
      <RequestStateView
        state="unauthorized"
        title="Customer login is not configured"
        description="Workspace features require a configured platform identity provider."
      />
    );
  }

  return (
    <EmptyState
      title="Sign in to Elsa Platform"
      description="Use your configured platform identity provider to continue to the console."
      action={<Button type="button" onClick={() => auth.signIn("/admin/overview")}>Continue to sign in</Button>}
    />
  );
}

export function ConsoleNotFoundPage() {
  return (
    <EmptyState
      title="Console page not found"
      description="The requested console page does not exist or is no longer available."
      action={<Link to="/admin/overview" className={buttonClassName()}>Open overview</Link>}
    />
  );
}

export const router = createBrowserRouter([
  {
    path: "/",
    element: <Navigate to="/admin/overview" replace />
  },
  {
    path: "/admin",
    element: <AppShell />,
    errorElement: <RequestStateView state="unexpected" title="The console could not load." />,
    children: [
      { index: true, element: <Navigate to="/admin/overview" replace /> },
      { path: "login", element: <AdminLoginPage /> },
      { path: "overview", element: <OverviewPage /> },
      { path: "sources", element: <SourcesPage /> },
      { path: "sources/new", element: <NewSourcePage /> },
      { path: "sources/:sourceId", element: <SourceDetailsPage /> },
      { path: "sources/:sourceId/edit", element: <EditSourcePage /> },
      { path: "packages", element: <PackagesPage /> },
      { path: "packages/:packageId", element: <PackageDetailsPage /> },
      { path: "packages/:packageId/versions/:version", element: <PackageDetailsPage /> },
      { path: "packages/:packageId/versions/:version/:section", element: <PackageDetailsPage /> },
      { path: "sync-runs", element: <SyncRunsPage /> },
      { path: "sync-runs/:runId", element: <SyncRunDetailsPage /> },
      { path: "deployments", element: <RequireCustomerAuth><DeploymentsPage /></RequireCustomerAuth> },
      { path: "deployments/new", element: <RequireCustomerAuth><NewDeploymentSetupPage /></RequireCustomerAuth> },
      { path: "deployments/credentials", element: <RequireCustomerAuth><DeploymentCredentialsPage /></RequireCustomerAuth> },
      { path: "deployments/credentials/stores/new", element: <RequireCustomerAuth><DeploymentCredentialStoreCreatePage /></RequireCustomerAuth> },
      { path: "deployments/credentials/stores/:secretStoreId/edit", element: <RequireCustomerAuth><DeploymentCredentialStoreEditPage /></RequireCustomerAuth> },
      { path: "deployments/credentials/references/new", element: <RequireCustomerAuth><DeploymentCredentialReferenceCreatePage /></RequireCustomerAuth> },
      { path: "deployments/credentials/references/:credentialReferenceId", element: <RequireCustomerAuth><DeploymentCredentialReferencePage /></RequireCustomerAuth> },
      { path: "deployments/credentials/references/:credentialReferenceId/edit", element: <RequireCustomerAuth><DeploymentCredentialReferenceEditPage /></RequireCustomerAuth> },
      { path: "deployments/applications", element: <RequireCustomerAuth><DeploymentApplicationsPage /></RequireCustomerAuth> },
      { path: "deployments/applications/:applicationId", element: <RequireCustomerAuth><DeploymentApplicationPage /></RequireCustomerAuth> },
      { path: "deployments/applications/:applicationId/edit", element: <RequireCustomerAuth><DeploymentApplicationEditPage /></RequireCustomerAuth> },
      { path: "deployments/applications/:applicationId/revisions", element: <RequireCustomerAuth><DeploymentApplicationRevisionsPage /></RequireCustomerAuth> },
      { path: "deployments/applications/:applicationId/revisions/:revisionId", element: <RequireCustomerAuth><DeploymentRevisionDetailPage /></RequireCustomerAuth> },
      { path: "deployments/applications/:applicationId/environments/new", element: <RequireCustomerAuth><DeploymentEnvironmentCreatePage /></RequireCustomerAuth> },
      { path: "deployments/applications/:applicationId/environments/:environmentId", element: <RequireCustomerAuth><DeploymentEnvironmentPage /></RequireCustomerAuth> },
      { path: "deployments/applications/:applicationId/environments/:environmentId/edit", element: <RequireCustomerAuth><DeploymentEnvironmentEditPage /></RequireCustomerAuth> },
      { path: "deployments/applications/:applicationId/environments/:environmentId/revisions/new", element: <RequireCustomerAuth><DeploymentRevisionCreatePage /></RequireCustomerAuth> },
      { path: "deployments/applications/:applicationId/environments/:environmentId/engines/new", element: <RequireCustomerAuth><DeploymentEngineRegisterPage /></RequireCustomerAuth> },
      { path: "deployments/applications/:applicationId/environments/:environmentId/engines/:engineId", element: <RequireCustomerAuth><DeploymentEnginePage /></RequireCustomerAuth> },
      { path: "deployments/applications/:applicationId/environments/:environmentId/engines/:engineId/edit", element: <RequireCustomerAuth><DeploymentEngineEditPage /></RequireCustomerAuth> },
      { path: "deployments/tiers", element: <RequireCustomerAuth><DeploymentTiersPage /></RequireCustomerAuth> },
      { path: "deployments/tiers/new", element: <RequireCustomerAuth><DeploymentTierCreatePage /></RequireCustomerAuth> },
      { path: "deployments/tiers/:tierId/edit", element: <RequireCustomerAuth><DeploymentTierEditPage /></RequireCustomerAuth> },
      { path: "artifacts", element: <RequireCustomerAuth><ArtifactsPage /></RequireCustomerAuth> },
      { path: "artifacts/new", element: <RequireCustomerAuth><ArtifactCreatePage /></RequireCustomerAuth> },
      { path: "artifacts/:artifactId", element: <RequireCustomerAuth><ArtifactDetailsPage /></RequireCustomerAuth> },
      { path: "runtime-builder", element: <RequireCustomerAuth><RuntimeBuilderPage /></RequireCustomerAuth> },
      { path: "runtime-builder/new", element: <RequireCustomerAuth><NewRuntimeBuilderPage /></RequireCustomerAuth> },
      { path: "runtime-builder/:configurationId/edit", element: <RequireCustomerAuth><EditRuntimeBuilderPage /></RequireCustomerAuth> },
      { path: "console", element: <RequireCustomerAuth><ConsoleLogsPage /></RequireCustomerAuth> },
      { path: "healing", element: <RequireCustomerAuth><HealingOverviewPage /></RequireCustomerAuth> },
      { path: "healing/audit", element: <RequireCustomerAuth><HealingAuditPage /></RequireCustomerAuth> },
      { path: "healing/incidents", element: <RequireCustomerAuth><HealingIncidentsPage /></RequireCustomerAuth> },
      { path: "healing/incidents/:incidentId", element: <RequireCustomerAuth><HealingIncidentPage /></RequireCustomerAuth> },
      { path: "healing/applications/:applicationId/configuration", element: <RequireCustomerAuth><HealingConfigurationPage /></RequireCustomerAuth> },
      { path: "healing/applications/:applicationId/components", element: <RequireCustomerAuth><HealingComponentsPage /></RequireCustomerAuth> },
      { path: "targets", element: <PlaceholderPage title="Targets" /> },
      { path: "runtimes", element: <PlaceholderPage title="Managed Runtimes" /> },
      { path: "operations", element: <PlaceholderPage title="Runtime Operations" /> },
      { path: "weaver/sessions/:sessionId", element: <RequireCustomerAuth><WeaverSessionPage /></RequireCustomerAuth> },
      { path: "audit", element: <PlaceholderPage title="Audit" /> },
      { path: "*", element: <ConsoleNotFoundPage /> }
    ]
  }
]);
