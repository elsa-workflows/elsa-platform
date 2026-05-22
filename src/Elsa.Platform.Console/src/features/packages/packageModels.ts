export type PackageApprovalStatus = "Pending" | "Approved" | "Rejected";
export type ValidationStatus = "NotValidated" | "Valid" | "Invalid" | "UnsupportedSchema" | "Suspicious";
export type VisibilitySeverity = "Info" | "Warning" | "Blocking";
export type ValidationFindingSeverity = "Error" | "Warning" | "Info";
export type PackageDetailsSection = "summary" | "validation" | "features" | "dependencies" | "compatibility" | "manifest" | "actions";
export type VersionAction = "approve" | "reject";

export type PackageVersionSummary = {
  version: string;
  validationStatus: ValidationStatus;
  approvalStatus: PackageApprovalStatus;
  isListed: boolean;
  suspiciousChangeDetected: boolean;
  schemaVersion?: string | null;
  versionStateToken?: string | null;
};

export type CatalogPackage = {
  packageId: string;
  approved: boolean;
  listed: boolean;
  latestVersion?: string | null;
  versions: PackageVersionSummary[];
  sourceId?: string | null;
  source?: PackageSourceSummary | null;
  approvalStatus?: PackageApprovalStatus;
  validationStatus?: ValidationStatus;
  featuresCount?: number | null;
  createdAt?: string | null;
  updatedAt?: string | null;
};

export type PackageSourceSummary = {
  id: string;
  name: string;
  url: string;
  enabled: boolean;
  status: string;
  lastSyncedAt?: string | null;
  lastSuccessfulSyncAt?: string | null;
};

export type CompatibilityMetadata = {
  targetFrameworks: string[];
  elsaVersionRange?: string | null;
  requiredCapabilities: string[];
  notes: string[];
  unsupportedCombinations: string[];
};

export type VisibilityReason = {
  code: string;
  category: string;
  severity: VisibilitySeverity;
  message: string;
  blocksPublicVisibility: boolean;
};

export type JsonBackedList<T> = T[] | string | null | undefined;

export type PackageFeatureDependency = {
  packageId?: string | null;
  versionRange?: string | null;
  featureId?: string | null;
  optional?: boolean;
  reason?: string | null;
};

export type PackageFeatureConflict = {
  packageId?: string | null;
  versionRange?: string | null;
  featureId?: string | null;
  reason?: string | null;
};

export type PackageInfrastructureRequirement = {
  id?: string | null;
  kind?: string | null;
  optional?: boolean;
  reason?: string | null;
  capabilities?: string[];
  providers?: string[];
  configurationKeys?: string[];
  extensionsJson?: string | null;
};

export type PackageFeatureSetting = {
  name: string;
  clrType?: string | null;
  jsonType: string;
  required: boolean;
  defaultValueJson?: string | null;
  displayName: string;
  description?: string | null;
  category?: string | null;
  validationJson: string;
  secret: boolean;
  restartRequired: boolean;
  environmentVariable?: string | null;
  uiJson: string;
  extensionsJson: string;
};

export type PackageFeature = {
  featureId: string;
  typeName: string;
  displayName: string;
  description?: string | null;
  category?: string | null;
  requiredCapabilities: string[];
  dependencies?: JsonBackedList<PackageFeatureDependency>;
  dependenciesJson?: string | null;
  conflicts?: JsonBackedList<PackageFeatureConflict>;
  conflictsJson?: string | null;
  infrastructure?: JsonBackedList<PackageInfrastructureRequirement>;
  infrastructureJson?: string | null;
  advanced: boolean;
  experimental: boolean;
  extensionsJson: string;
  settings: PackageFeatureSetting[];
};

export type NormalizedPackageFeature = Omit<
  PackageFeature,
  "dependencies" | "dependenciesJson" | "conflicts" | "conflictsJson" | "infrastructure" | "infrastructureJson"
> & {
  dependencies: PackageFeatureDependency[];
  conflicts: PackageFeatureConflict[];
  infrastructure: PackageInfrastructureRequirement[];
};

export type PackageManifestContent = {
  available: boolean;
  schemaVersion?: string | null;
  manifestHash: string;
  suspiciousManifestHash?: string | null;
  manifestJson: string;
};

export type PackageDetailsVersion = PackageVersionSummary & {
  manifestHash: string;
  suspiciousManifestHash?: string | null;
  versionStateToken: string;
  publishedAt?: string | null;
  indexedAt: string;
  featuresCount: number;
  settingsCount: number;
  compatibility: CompatibilityMetadata;
  visibilityReasons: VisibilityReason[];
  features: PackageFeature[];
  manifest: PackageManifestContent;
};

export type PackageDetails = Omit<CatalogPackage, "versions"> & {
  source?: PackageSourceSummary | null;
  createdAt?: string | null;
  updatedAt?: string | null;
  versions: PackageDetailsVersion[];
};

export type ValidationFinding = {
  severity: ValidationFindingSeverity;
  code?: string | null;
  message: string;
  path?: string | null;
  blocksPublicVisibility: boolean;
  validatedAt: string;
  validatorVersion?: string | null;
};

export type ValidationFindingsResponse = {
  packageId: string;
  version: string;
  findings: ValidationFinding[];
};

export type PackageManifestResponse = PackageManifestContent & {
  packageId: string;
  version: string;
};

export type PackageVersionActionRequest = {
  reason?: string;
  expectedStateToken?: string;
};

export type PackageFilter = "All" | "Pending" | "Approved" | "Rejected" | "Invalid" | "Suspicious" | "Unlisted";
export type PackageSort = "packageId" | "latestVersion" | "approvalStatus" | "validationStatus" | "updatedAt";

export type SelectablePackageVersion = {
  packageId: string;
  version: string;
  expectedStateToken?: string;
};

const approvalOrder: PackageApprovalStatus[] = ["Pending", "Rejected", "Approved"];
const validationOrder: ValidationStatus[] = ["Suspicious", "Invalid", "UnsupportedSchema", "NotValidated", "Valid"];
const packageDetailsSections: PackageDetailsSection[] = ["summary", "validation", "features", "dependencies", "compatibility", "manifest", "actions"];

export function latestVersion(packageItem: CatalogPackage) {
  return packageItem.latestVersion ?? packageItem.versions[0]?.version ?? null;
}

export function latestVersionSummary(packageItem: CatalogPackage) {
  const latest = latestVersion(packageItem);
  return packageItem.versions.find((version) => version.version === latest) ?? packageItem.versions[0] ?? null;
}

export function packageApprovalStatus(packageItem: CatalogPackage): PackageApprovalStatus {
  if (packageItem.approvalStatus) return packageItem.approvalStatus;
  const statuses = packageItem.versions.map((version) => version.approvalStatus);
  return approvalOrder.find((status) => statuses.includes(status)) ?? (packageItem.approved ? "Approved" : "Pending");
}

export function packageValidationStatus(packageItem: CatalogPackage): ValidationStatus {
  if (packageItem.validationStatus) return packageItem.validationStatus;
  const statuses = packageItem.versions.map((version) => version.validationStatus);
  return validationOrder.find((status) => statuses.includes(status)) ?? "NotValidated";
}

export function isPackageListed(packageItem: CatalogPackage) {
  return packageItem.listed && (latestVersionSummary(packageItem)?.isListed ?? true);
}

export function hasSuspiciousChange(packageItem: CatalogPackage) {
  return latestVersionSummary(packageItem)?.suspiciousChangeDetected ?? false;
}

export function selectableLatestVersion(packageItem: CatalogPackage): SelectablePackageVersion | null {
  const latest = latestVersionSummary(packageItem);
  return latest ? { packageId: packageItem.packageId, version: latest.version, expectedStateToken: latest.versionStateToken ?? undefined } : null;
}

export function selectionKey(item: SelectablePackageVersion) {
  return `${item.packageId}@${item.version}`;
}

export function selectedPackageDetailsVersion(packageDetails: PackageDetails, routeVersion?: string | null) {
  if (packageDetails.versions.length === 0) return null;
  if (routeVersion) {
    const explicit = packageDetails.versions.find((version) => version.version === routeVersion);
    if (explicit) return explicit;
  }
  const latest = packageDetails.latestVersion
    ? packageDetails.versions.find((version) => version.version === packageDetails.latestVersion)
    : null;
  return latest ?? [...packageDetails.versions].sort((left, right) => Date.parse(right.indexedAt) - Date.parse(left.indexedAt))[0] ?? null;
}

export function parsePackageDetailsSection(value?: string | null): PackageDetailsSection {
  return packageDetailsSections.includes(value as PackageDetailsSection) ? (value as PackageDetailsSection) : "summary";
}

export function visibilityReasonGroups(reasons: VisibilityReason[]) {
  return reasons.reduce<Record<string, VisibilityReason[]>>((groups, reason) => {
    const category = reason.category || "Other";
    groups[category] = [...(groups[category] ?? []), reason];
    return groups;
  }, {});
}

export function versionStateChanged(loaded?: string | null, current?: string | null) {
  return Boolean(loaded && current && loaded !== current);
}

export function isStaleVersionAction(action: SelectablePackageVersion, version?: PackageDetailsVersion | null) {
  return versionStateChanged(action.expectedStateToken, version?.versionStateToken);
}

export function compatibilityMatchesSearch(compatibility: CompatibilityMetadata, term: string) {
  const normalizedTerm = normalizeSearchTerm(term);
  if (!normalizedTerm) return true;
  return [
    ...compatibility.targetFrameworks,
    compatibility.elsaVersionRange ?? "",
    ...compatibility.requiredCapabilities,
    ...compatibility.notes,
    ...compatibility.unsupportedCombinations
  ].some((value) => normalizeSearchTerm(value).includes(normalizedTerm));
}

export function normalizeFeature(feature: PackageFeature): NormalizedPackageFeature {
  return {
    featureId: feature.featureId,
    typeName: feature.typeName,
    displayName: feature.displayName,
    description: feature.description,
    category: feature.category,
    requiredCapabilities: feature.requiredCapabilities,
    advanced: feature.advanced,
    experimental: feature.experimental,
    extensionsJson: feature.extensionsJson,
    settings: feature.settings,
    dependencies: normalizeJsonBackedList<PackageFeatureDependency>(feature.dependencies ?? feature.dependenciesJson),
    conflicts: normalizeJsonBackedList<PackageFeatureConflict>(feature.conflicts ?? feature.conflictsJson),
    infrastructure: normalizeJsonBackedList<PackageInfrastructureRequirement>(feature.infrastructure ?? feature.infrastructureJson)
  };
}

export function featureMatchesSearch(feature: NormalizedPackageFeature, term: string) {
  const normalizedTerm = normalizeSearchTerm(term);
  if (!normalizedTerm) return true;
  return [
    feature.featureId,
    feature.typeName,
    feature.displayName,
    feature.description ?? "",
    feature.category ?? "",
    ...feature.requiredCapabilities,
    ...feature.settings.flatMap((setting) => [
      setting.name,
      setting.displayName,
      setting.description ?? "",
      setting.category ?? "",
      setting.environmentVariable ?? "",
      setting.jsonType
    ]),
    ...feature.dependencies.flatMap((dependency) => [dependency.packageId ?? "", dependency.versionRange ?? "", dependency.featureId ?? "", dependency.reason ?? ""]),
    ...feature.conflicts.flatMap((conflict) => [conflict.packageId ?? "", conflict.versionRange ?? "", conflict.featureId ?? "", conflict.reason ?? ""]),
    ...feature.infrastructure.flatMap((requirement) => [
      requirement.id ?? "",
      requirement.kind ?? "",
      requirement.reason ?? "",
      ...(requirement.capabilities ?? []),
      ...(requirement.providers ?? []),
      ...(requirement.configurationKeys ?? [])
    ])
  ].some(
    (value) => normalizeSearchTerm(value).includes(normalizedTerm)
  );
}

export function validationFindingMatchesSearch(finding: ValidationFinding, term: string) {
  const normalizedTerm = normalizeSearchTerm(term);
  if (!normalizedTerm) return true;
  return [finding.severity, finding.code ?? "", finding.message, finding.path ?? "", finding.blocksPublicVisibility ? "blocking" : "nonblocking"].some(
    (value) => normalizeSearchTerm(value).includes(normalizedTerm)
  );
}

export function visibilityReasonMatchesSearch(reason: VisibilityReason, term: string) {
  const normalizedTerm = normalizeSearchTerm(term);
  if (!normalizedTerm) return true;
  return [reason.severity, reason.code, reason.category, reason.message, reason.blocksPublicVisibility ? "blocking" : "does-not-block"].some((value) =>
    normalizeSearchTerm(value).includes(normalizedTerm)
  );
}

function normalizeJsonBackedList<T>(value: JsonBackedList<T>): T[] {
  if (Array.isArray(value)) return value.filter(isJsonObject) as T[];
  if (!value) return [];
  try {
    const parsed = JSON.parse(value) as unknown;
    return Array.isArray(parsed) ? (parsed.filter(isJsonObject) as T[]) : [];
  } catch {
    return [];
  }
}

function isJsonObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function normalizeSearchTerm(value: string) {
  return value.trim().toLowerCase();
}
