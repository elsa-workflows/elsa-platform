import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 20_000,
      refetchOnWindowFocus: false,
      retry: 1
    },
    mutations: {
      retry: 0
    }
  }
});

export const queryKeys = {
  authSession: ["auth-session"] as const,
  application: ["application"] as const,
  sources: ["sources"] as const,
  packages: ["packages"] as const,
  packageDetails: (packageId: string) => ["packages", packageId] as const,
  packageValidation: (packageId: string, version: string) => ["packages", packageId, "versions", version, "validation"] as const,
  packageManifest: (packageId: string, version: string) => ["packages", packageId, "versions", version, "manifest"] as const,
  syncRuns: ["sync-runs"] as const,
  syncRun: (runId: string) => ["sync-runs", runId] as const,
  deploymentCockpit: (workspaceId: string) => ["deployments", workspaceId, "cockpit"] as const,
  deploymentPermissions: (workspaceId: string) => ["deployments", workspaceId, "permissions"] as const,
  deploymentTiers: (workspaceId: string) => ["deployments", workspaceId, "tiers"] as const,
  deploymentTierCapabilities: (workspaceId: string) => ["deployments", workspaceId, "tier-capabilities"] as const,
  artifacts: (workspaceId: string) => ["artifacts", workspaceId] as const,
  artifactDetails: (workspaceId: string, artifactId: string) => ["artifacts", workspaceId, artifactId] as const,
  overview: ["overview"] as const,
  workspaceContext: ["workspace-context"] as const,
  runtimeBuilderCatalog: (workspaceId: string) => ["runtime-builder", workspaceId, "catalog"] as const,
  runtimeConfigurations: (workspaceId: string) => ["runtime-builder", workspaceId, "configurations"] as const
};

export function QueryProvider({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}
