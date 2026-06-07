import { createBrowserRouter, Navigate } from "react-router-dom";
import { AppShell } from "@/app/AppShell";
import { OverviewPage } from "@/app/OverviewPage";
import { RequestStateView } from "@/components/states/RequestStateViews";
import {
  DeploymentApplicationEditPage,
  DeploymentApplicationsPage,
  DeploymentApplicationPage,
  DeploymentEnginePage,
  DeploymentEngineEditPage,
  DeploymentEngineRegisterPage,
  DeploymentEnvironmentCreatePage,
  DeploymentEnvironmentEditPage,
  DeploymentEnvironmentPage,
  DeploymentRevisionCreatePage,
  DeploymentsPage,
  NewDeploymentSetupPage
} from "@/features/deployments/DeploymentsPage";
import { DeploymentTierCreatePage, DeploymentTierEditPage, DeploymentTiersPage } from "@/features/deployments/DeploymentTiersPage";
import { ArtifactCreatePage, ArtifactDetailsPage, ArtifactsPage } from "@/features/artifacts/ArtifactsPage";
import { RequireCustomerAuth } from "@/lib/auth/AuthProvider";
import { NewSourcePage, EditSourcePage } from "@/features/sources/SourceFormPage";
import { SourceDetailsPage } from "@/features/sources/SourceDetailsPage";
import { SourcesPage } from "@/features/sources/SourcesPage";
import { PackageDetailsPage } from "@/features/packages/PackageDetailsPage";
import { PackagesPage } from "@/features/packages/PackagesPage";
import { EditRuntimeBuilderPage, NewRuntimeBuilderPage, RuntimeBuilderPage } from "@/features/runtime-builder/RuntimeBuilderPage";
import { SyncRunDetailsPage } from "@/features/sync-runs/SyncRunDetailsPage";
import { SyncRunsPage } from "@/features/sync-runs/SyncRunsPage";
import { WeaverSessionPage } from "@/features/weaver/WeaverSessionPage";

function PlaceholderPage({ title }: { title: string }) {
  return (
    <section className="space-y-2">
      <h1 className="font-display text-xl font-semibold">{title}</h1>
      <p className="text-sm text-muted-foreground">This operational view is ready for feature implementation.</p>
    </section>
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
      { path: "deployments/applications", element: <RequireCustomerAuth><DeploymentApplicationsPage /></RequireCustomerAuth> },
      { path: "deployments/applications/:applicationId", element: <RequireCustomerAuth><DeploymentApplicationPage /></RequireCustomerAuth> },
      { path: "deployments/applications/:applicationId/edit", element: <RequireCustomerAuth><DeploymentApplicationEditPage /></RequireCustomerAuth> },
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
      { path: "targets", element: <PlaceholderPage title="Targets" /> },
      { path: "runtimes", element: <PlaceholderPage title="Managed Runtimes" /> },
      { path: "operations", element: <PlaceholderPage title="Runtime Operations" /> },
      { path: "weaver/sessions/:sessionId", element: <RequireCustomerAuth><WeaverSessionPage /></RequireCustomerAuth> },
      { path: "audit", element: <PlaceholderPage title="Audit" /> }
    ]
  }
]);
