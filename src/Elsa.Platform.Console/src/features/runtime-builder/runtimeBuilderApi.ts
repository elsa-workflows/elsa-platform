import { apiRequest } from "@/lib/api/httpClient";
import type {
  BuilderBundleResponse,
  BuilderCatalog,
  BuilderPlanResponse,
  RuntimeBuilderIntent,
  RuntimeConfiguration,
  RuntimeConfigurationRequest
} from "@/features/runtime-builder/runtimeBuilderModels";

export function getBuilderCatalog(workspaceId: string) {
  return apiRequest<BuilderCatalog>(`/api/workspaces/${encodeURIComponent(workspaceId)}/builder/catalog`);
}

export function planRuntime(workspaceId: string, intent: RuntimeBuilderIntent) {
  return apiRequest<BuilderPlanResponse>(`/api/workspaces/${encodeURIComponent(workspaceId)}/builder/plan`, {
    method: "POST",
    body: JSON.stringify({ intent })
  });
}

export function generateBundle(workspaceId: string, intent: RuntimeBuilderIntent) {
  return apiRequest<BuilderBundleResponse>(`/api/workspaces/${encodeURIComponent(workspaceId)}/builder/bundle`, {
    method: "POST",
    body: JSON.stringify(toBundleRequest(intent))
  });
}

export function listRuntimeConfigurations(workspaceId: string) {
  return apiRequest<RuntimeConfiguration[]>(`/api/workspaces/${encodeURIComponent(workspaceId)}/runtime-configurations`);
}

export function createRuntimeConfiguration(workspaceId: string, request: RuntimeConfigurationRequest) {
  return apiRequest<RuntimeConfiguration>(`/api/workspaces/${encodeURIComponent(workspaceId)}/runtime-configurations`, {
    method: "POST",
    body: JSON.stringify(request)
  });
}

function toBundleRequest(intent: RuntimeBuilderIntent) {
  return {
    image: intent.image,
    packages: intent.packages,
    packageSources: intent.packageSources,
    infrastructure: intent.infrastructure,
    localPackages: intent.localPackages,
    target: intent.target
  };
}
