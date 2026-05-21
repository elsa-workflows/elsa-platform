export type WorkspaceRole = "Owner" | "Admin" | "Member" | "Viewer" | string;
export type WorkspaceKind = "Personal" | "Team" | string;

export type AccountContext = {
  id: string;
  displayName?: string | null;
  email?: string | null;
};

export type WorkspaceContext = {
  id: string;
  name: string;
  kind: WorkspaceKind;
  role: WorkspaceRole;
};

export type MeWorkspacesResponse = {
  account: AccountContext;
  workspaces: WorkspaceContext[];
};

export type RuntimeImageEnvironmentVariable = {
  name: string;
  displayName: string;
  description: string;
  required: boolean;
  secret: boolean;
  defaultValue?: string | null;
  group: string;
  advanced: boolean;
};

export type RuntimeImage = {
  slug: string;
  displayName: string;
  description: string;
  image: string;
  availableTags: string[];
  defaultTag: string;
  defaultPort: number;
  hostPort: number;
  containerName: string;
  licenseTier: string;
  stability: string;
  capabilities: string[];
  envVars: RuntimeImageEnvironmentVariable[];
  deploymentHints: {
    supportsDockerCompose: boolean;
    supportsKubernetes: boolean;
    requiresCompanionServer: boolean;
    needsSharedNetwork: boolean;
    companionImageSlug?: string | null;
  };
  docs: {
    dockerHubUrl?: string | null;
    containerPaths: string[];
    showPerShellAdmin: boolean;
    showNuplane: boolean;
  };
};

export type PublicPackageSource = {
  id: string;
  name: string;
  url: string;
};

export type PublicPackageFeatureSetting = {
  name: string;
  jsonType: string;
  required: boolean;
  defaultValue?: unknown;
  displayName: string;
  description?: string | null;
  category?: string | null;
  secret: boolean;
  restartRequired: boolean;
  environmentVariable?: string | null;
};

export type PublicPackageInfrastructureRequirement = {
  id: string;
  kind: string;
  optional: boolean;
  reason?: string | null;
  capabilities: string[];
  providers: string[];
  configurationKeys: string[];
};

export type PublicPackageFeature = {
  featureId: string;
  typeName: string;
  displayName: string;
  description?: string | null;
  category?: string | null;
  requiredCapabilities: string[];
  infrastructure: PublicPackageInfrastructureRequirement[];
  advanced: boolean;
  experimental: boolean;
  settings: PublicPackageFeatureSetting[];
};

export type PublicPackageVersion = {
  packageId: string;
  version: string;
  source: PublicPackageSource;
  schemaVersion?: string | null;
  publishedAt?: string | null;
  features: PublicPackageFeature[];
};

export type BuilderPackage = {
  packageId: string;
  displayName: string;
  source: PublicPackageSource;
  latestVersion?: string | null;
  versions: PublicPackageVersion[];
};

export type InfrastructureProvider = {
  id: string;
  displayName: string;
  kind: string;
  strategy: string;
  provider: string;
  capabilities: string[];
  outputs: string[];
};

export type BuilderCatalog = {
  images: RuntimeImage[];
  packages: BuilderPackage[];
  infrastructureProviders: InfrastructureProvider[];
};

export type RuntimeImageSelection = {
  slug: string;
  tag?: string | null;
  hostPort?: number | null;
  envOverrides?: Record<string, string> | null;
};

export type BundlePackageSelection = {
  sourceId: string;
  packageId: string;
  version: string;
  selectedFeatures?: string[] | null;
  settings?: Record<string, Record<string, unknown>> | null;
};

export type PackageSourceSelection = {
  sourceId: string;
  name?: string | null;
  url?: string | null;
  kind?: string | null;
};

export type InfrastructureSelection = {
  kind: string;
  providerId: string;
  strategy: string;
  settings?: Record<string, unknown> | null;
};

export type LocalPackagesOptions = {
  enabled: boolean;
  directoryPath?: string | null;
};

export type RuntimeBuilderIntent = {
  image: RuntimeImageSelection;
  packages: BundlePackageSelection[];
  packageSources: PackageSourceSelection[];
  infrastructure: InfrastructureSelection[];
  localPackages?: LocalPackagesOptions | null;
  target?: string | null;
};

export type BuilderFinding = {
  level: string;
  code: string;
  message: string;
  scope?: string | null;
};

export type BuilderPlanResponse = {
  resolved: RuntimeBuilderIntent;
  autoAdded: {
    packages: BundlePackageSelection[];
    features: string[];
    infrastructure: InfrastructureSelection[];
  };
  findings: BuilderFinding[];
};

export type BuilderBundleFile = {
  path: string;
  language: string;
  contentType: string;
  required: boolean;
  contents: string;
};

export type BuilderBundleResponse = {
  bundleId: string;
  files: BuilderBundleFile[];
  findings: BuilderFinding[];
};

export type RuntimeConfiguration = {
  id: string;
  workspaceId: string;
  name: string;
  description?: string | null;
  intent: RuntimeBuilderIntent;
  createdAt: string;
  updatedAt: string;
};

export type RuntimeConfigurationRequest = {
  name: string;
  description?: string | null;
  intent: RuntimeBuilderIntent;
};

export type SelectedRuntimePackage = {
  sourceId: string;
  packageId: string;
  version: string;
  selectedFeatures: string[];
};

export type DeploymentTarget = "docker-compose" | "kubernetes-helm" | "azure-container-apps";

export const deploymentTargets: Array<{ value: DeploymentTarget; label: string }> = [
  { value: "docker-compose", label: "Docker Compose" },
  { value: "kubernetes-helm", label: "Kubernetes Helm" },
  { value: "azure-container-apps", label: "Azure Container Apps" }
];

export function packageSelectionKey(sourceId: string, packageId: string) {
  return `${sourceId}:${packageId.toLowerCase()}`;
}

export function selectedPackageKey(selection: SelectedRuntimePackage) {
  return packageSelectionKey(selection.sourceId, selection.packageId);
}

export function latestPackageVersion(packageItem: BuilderPackage) {
  return packageItem.versions.find((version) => version.version === packageItem.latestVersion) ?? packageItem.versions[0] ?? null;
}

export function findSelectedVersion(packageItem: BuilderPackage, version: string) {
  return packageItem.versions.find((item) => item.version === version) ?? latestPackageVersion(packageItem);
}

export function defaultSelectedFeatures(version: PublicPackageVersion) {
  return version.features
    .filter((feature) => !feature.advanced && !feature.experimental)
    .map((feature) => feature.featureId)
    .sort((left, right) => left.localeCompare(right, undefined, { sensitivity: "base" }));
}

export function normalizeFindingLevel(level: string) {
  return level.toLowerCase();
}
