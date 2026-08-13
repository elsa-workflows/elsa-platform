export type PatternPreview = {
  packageId: string;
  included: boolean;
};

export function previewPatterns(includePatterns: string[], excludePatterns: string[], packageIds: string[]): PatternPreview[] {
  return packageIds.map((packageId) => ({
    packageId,
    included: isPackageIncluded(packageId, includePatterns, excludePatterns)
  }));
}

export function isPackageIncluded(packageId: string, includePatterns: string[], excludePatterns: string[]) {
  if (excludePatterns.some((pattern) => matchesGlob(packageId, pattern))) return false;
  return includePatterns.some((pattern) => matchesGlob(packageId, pattern));
}

function matchesGlob(value: string, pattern: string) {
  const trimmed = pattern.trim();
  if (!trimmed) return false;
  if (trimmed.endsWith(".") && !trimmed.includes("*") && !trimmed.includes("?")) {
    return value.toLowerCase().startsWith(trimmed.toLowerCase());
  }

  const escaped = trimmed
    .replace(/[.+^${}()|[\]\\]/g, "\\$&")
    .replace(/\*/g, ".*")
    .replace(/\?/g, ".");
  return new RegExp(`^${escaped}$`, "i").test(value);
}
