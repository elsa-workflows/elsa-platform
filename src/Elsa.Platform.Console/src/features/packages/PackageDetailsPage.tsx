import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Check, RefreshCw, Search, X } from "lucide-react";
import type { Dispatch, SetStateAction } from "react";
import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { Badge, Button, EmptyState, Input, SecondaryButton } from "@/components/ui";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { approvePackageVersion, getPackageDetails, getPackageManifest, getPackageValidation, rejectPackageVersion } from "@/features/packages/packageApi";
import {
  compatibilityMatchesSearch,
  featureMatchesCategory,
  featureMatchesSearch,
  normalizeFeature,
  type PackageFeatureConflict,
  type PackageFeatureDependency,
  type PackageInfrastructureRequirement,
  parsePackageDetailsSection,
  selectedPackageDetailsVersion,
  validationFindingMatchesSearch,
  visibilityReasonGroups
} from "@/features/packages/packageModels";
import { ApiError } from "@/lib/api/httpClient";
import { formatDateTime, formatJson } from "@/lib/formatters";
import { queryKeys } from "@/lib/query/queryClient";
import { sourceStatusTone, statusToneClass } from "@/lib/status/statusBadges";
import { cn } from "@/lib/utils";

export function PackageDetailsPage() {
  const { packageId = "", version, section } = useParams();
  const [inspectionSearch, setInspectionSearch] = useState("");
  const [validationSearch, setValidationSearch] = useState("");
  const [manifestSearch, setManifestSearch] = useState("");
  const [selectedFeatureCategories, setSelectedFeatureCategories] = useState<string[]>([]);
  const [rejectionReason, setRejectionReason] = useState("");
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const [reviewedTokens, setReviewedTokens] = useState<Record<string, string>>({});
  const queryClient = useQueryClient();
  const activeSection = parsePackageDetailsSection(section);
  const packageDetails = useQuery({
    queryKey: queryKeys.packageDetails(packageId),
    queryFn: () => getPackageDetails(packageId),
    enabled: Boolean(packageId),
    refetchInterval: 30_000
  });
  const details = packageDetails.data;
  const selectedVersion = details ? selectedPackageDetailsVersion(details, version) : null;
  const explicitVersionMissing = Boolean(details && version && !details.versions.some((item) => item.version === version));
  const reasonGroups = selectedVersion ? visibilityReasonGroups(selectedVersion.visibilityReasons) : {};
  const validation = useQuery({
    queryKey: queryKeys.packageValidation(details?.packageId ?? packageId, selectedVersion?.version ?? ""),
    queryFn: () => getPackageValidation(details!.packageId, selectedVersion!.version),
    enabled: Boolean(details && selectedVersion)
  });
  const manifest = useQuery({
    queryKey: queryKeys.packageManifest(details?.packageId ?? packageId, selectedVersion?.version ?? ""),
    queryFn: () => getPackageManifest(details!.packageId, selectedVersion!.version),
    enabled: Boolean(details && selectedVersion)
  });
  const normalizedFeatures = useMemo(() => selectedVersion?.features.map(normalizeFeature) ?? [], [selectedVersion]);
  const featureCategoryOptions = useMemo(() => {
    const counts = new Map<string, number>();
    normalizedFeatures.forEach((feature) => {
      const categories = feature.categories.length > 0 ? feature.categories : ["Uncategorized"];
      categories.forEach((category) => counts.set(category, (counts.get(category) ?? 0) + 1));
    });
    return [...counts.entries()]
      .map(([category, count]) => ({ category, count }))
      .sort((left, right) => left.category.localeCompare(right.category));
  }, [normalizedFeatures]);
  const featureCategoryLabels = useMemo(() => featureCategoryOptions.map((item) => item.category), [featureCategoryOptions]);
  const selectedFeatureCategorySet = useMemo(() => new Set(selectedFeatureCategories), [selectedFeatureCategories]);
  const allFeatureCategoriesSelected = selectedFeatureCategories.length === 0;
  const visibleFeatures = useMemo(
    () => normalizedFeatures.filter((feature) => featureMatchesSearch(feature, inspectionSearch) && featureMatchesCategory(feature, selectedFeatureCategories)),
    [inspectionSearch, normalizedFeatures, selectedFeatureCategories]
  );
  const compatibilityVisible = selectedVersion ? compatibilityMatchesSearch(selectedVersion.compatibility, inspectionSearch) : false;
  const visibleValidationFindings = useMemo(
    () => (validation.data?.findings ?? []).filter((finding) => validationFindingMatchesSearch(finding, validationSearch)),
    [validation.data?.findings, validationSearch]
  );
  const manifestContent = manifest.data ?? selectedVersion?.manifest;
  const formattedManifest = formatJson(manifestContent?.manifestJson);
  const manifestVisible = !manifestSearch.trim() || formattedManifest.value.toLowerCase().includes(manifestSearch.trim().toLowerCase());
  const listingLabel = selectedVersion ? (selectedVersion.isListed && details?.listed ? "Listed" : "Unlisted") : details?.listed ? "Listed" : "No versions";
  const selectedReviewTokenKey = selectedVersion ? reviewTokenKey(details?.packageId ?? packageId, selectedVersion.version) : null;
  const reviewedStateToken = selectedReviewTokenKey ? reviewedTokens[selectedReviewTokenKey] : undefined;
  const actionTokenStale = Boolean(selectedVersion && reviewedStateToken && reviewedStateToken !== selectedVersion.versionStateToken);
  const selectedAction = selectedVersion && !actionTokenStale
    ? {
        packageId: details?.packageId ?? packageId,
        version: selectedVersion.version,
        expectedStateToken: reviewedStateToken ?? selectedVersion.versionStateToken
      }
    : null;
  const approveVersion = useMutation({
    mutationFn: () => approvePackageVersion(selectedAction!, "Reviewed from package details."),
    onSuccess: () => handleActionSuccess("Version approved."),
    onError: (error) => setActionMessage(actionErrorMessage(error))
  });
  const rejectVersion = useMutation({
    mutationFn: () => rejectPackageVersion(selectedAction!, rejectionReason),
    onSuccess: () => {
      setRejectionReason("");
      handleActionSuccess("Version rejected.");
    },
    onError: (error) => setActionMessage(actionErrorMessage(error))
  });

  useEffect(() => {
    setActionMessage(null);
    setRejectionReason("");
  }, [selectedVersion?.version]);

  useEffect(() => {
    setSelectedFeatureCategories((current) => {
      const next = current.filter((category) => featureCategoryLabels.includes(category));
      return next.length === current.length ? current : next;
    });
  }, [featureCategoryLabels]);

  useEffect(() => {
    if (!selectedVersion) return;
    const tokenKey = reviewTokenKey(details?.packageId ?? packageId, selectedVersion.version);
    setReviewedTokens((current) =>
      current[tokenKey] ? current : { ...current, [tokenKey]: selectedVersion.versionStateToken }
    );
  }, [details?.packageId, packageId, selectedVersion]);

  useEffect(() => {
    if (!activeSection || activeSection === "summary") return;
    document.getElementById(sectionElementId(activeSection))?.scrollIntoView?.({ block: "start" });
  }, [activeSection, selectedVersion?.version]);

  function handleActionSuccess(message: string) {
    setActionMessage(message);
    if (selectedReviewTokenKey) {
      setReviewedTokens((current) => {
        const next = { ...current };
        delete next[selectedReviewTokenKey];
        return next;
      });
    }
    void queryClient.invalidateQueries({ queryKey: queryKeys.packageDetails(packageId) });
    void queryClient.invalidateQueries({ queryKey: queryKeys.packageDetails(details!.packageId) });
    void queryClient.invalidateQueries({ queryKey: queryKeys.packages });
  }

  function refreshPackageDetails() {
    if (selectedReviewTokenKey) {
      setReviewedTokens((current) => {
        const next = { ...current };
        delete next[selectedReviewTokenKey];
        return next;
      });
    }
    void packageDetails.refetch();
    if (selectedVersion) {
      void validation.refetch();
      void manifest.refetch();
    }
  }

  if (packageDetails.isLoading) return <RequestStateView state="loading" title="Loading package details" />;

  if (packageDetails.isError && !packageDetails.data) {
    const state = requestState(packageDetails.error);
    return <RequestStateView state={state} title={state === "not-found" ? "Package not found" : undefined} />;
  }

  if (!details) return <RequestStateView state="unexpected" title="Package details could not load" />;

  if (packageDetails.isRefetchError && isAccessError(packageDetails.error)) {
    return <RequestStateView state="unauthorized" title="Access problem" />;
  }

  return (
    <section className="space-y-5">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div className="space-y-2">
          <Link to="/admin/packages" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground">
            <ArrowLeft className="h-4 w-4" />
            Packages
          </Link>
          <div>
            <h1 className="text-2xl font-semibold">{details.packageId}</h1>
            <p className="mt-1 text-sm text-muted-foreground">
              {selectedVersion ? `Version ${selectedVersion.version}` : "No indexed versions"} · Updated {formatDateTime(details.updatedAt)}
            </p>
          </div>
        </div>
        <SecondaryButton onClick={refreshPackageDetails} title="Refresh package details">
          <RefreshCw className="h-4 w-4" />
          Refresh
        </SecondaryButton>
      </div>

      {packageDetails.isRefetchError ? <RequestStateView state="stale" title="Showing last loaded package details" /> : null}

      {details.versions.length === 0 ? (
        <EmptyState title="No indexed versions" description="This package exists in the catalog, but no package versions have been indexed yet." />
      ) : null}

      {explicitVersionMissing ? (
        <RequestStateView
          state="stale"
          title="Version not available"
          description="The requested version is not indexed for this package. Showing the latest available version instead."
        />
      ) : null}

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="space-y-4">
          <section id={sectionElementId("summary")} className="rounded-ui border border-border bg-surface p-4">
            <div className="flex flex-wrap items-center gap-2">
              <Badge className={statusToneClass(sourceStatusTone(selectedVersion?.approvalStatus ?? (details.approved ? "Approved" : "Pending")))}>
                {selectedVersion?.approvalStatus ?? (details.approved ? "Approved" : "Pending")}
              </Badge>
              <Badge className={statusToneClass(sourceStatusTone(selectedVersion?.validationStatus ?? "NotValidated"))}>
                {selectedVersion?.validationStatus ?? "NotValidated"}
              </Badge>
              <Badge className={statusToneClass(sourceStatusTone(listingLabel))}>
                {listingLabel}
              </Badge>
              {selectedVersion?.suspiciousChangeDetected ? (
                <Badge className={statusToneClass(sourceStatusTone("Suspicious"))}>Suspicious</Badge>
              ) : null}
            </div>

            <dl className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <DetailItem label="Latest version" value={details.latestVersion ?? "None"} />
              <DetailItem label="Selected version" value={selectedVersion?.version ?? "None"} />
              <DetailItem label="Features" value={selectedVersion?.featuresCount.toString() ?? "0"} />
              <DetailItem label="Settings" value={selectedVersion?.settingsCount.toString() ?? "0"} />
              <DetailItem label="Published" value={formatDateTime(selectedVersion?.publishedAt)} />
              <DetailItem label="Indexed" value={formatDateTime(selectedVersion?.indexedAt)} />
              <DetailItem label="Schema" value={selectedVersion?.schemaVersion ?? "Unknown"} />
              <DetailItem label="Section" value={activeSection} />
            </dl>
          </section>

          <section className="rounded-ui border border-border bg-surface p-4">
            <h2 className="text-base font-medium">Visibility</h2>
            {selectedVersion && Object.keys(reasonGroups).length > 0 ? (
              <div className="mt-3 space-y-3">
                {Object.entries(reasonGroups).map(([category, reasons]) => (
                  <div key={category}>
                    <h3 className="text-sm font-medium">{category}</h3>
                    <ul className="mt-2 space-y-2">
                      {reasons.map((reason) => (
                        <li key={`${reason.category}-${reason.code}`} className="rounded-ui border border-border bg-background p-3 text-sm">
                          <div className="flex flex-wrap items-center gap-2">
                            <Badge className={statusToneClass(sourceStatusTone(reason.severity))}>{reason.severity}</Badge>
                            <span className="font-medium">{reason.code}</span>
                          </div>
                          <p className="mt-1 text-muted-foreground">{reason.message}</p>
                        </li>
                      ))}
                    </ul>
                  </div>
                ))}
              </div>
            ) : (
              <p className="mt-2 text-sm text-muted-foreground">No version visibility reasons are available.</p>
            )}
          </section>

          <section id={sectionElementId("validation")} className="rounded-ui border border-border bg-surface p-4">
            <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
              <h2 className="text-base font-medium">Validation Findings</h2>
              <label className="relative block md:w-72">
                <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
                <Input value={validationSearch} onChange={(event) => setValidationSearch(event.target.value)} className="pl-9" placeholder="Filter validation findings" aria-label="Filter validation findings" />
              </label>
            </div>
            {validation.isError ? (
              <RequestStateView
                state="stale"
                title="Validation findings could not load"
                description="Package summary data is still available. Refresh to retry validation diagnostics."
              />
            ) : validation.isLoading ? (
              <p className="mt-2 text-sm text-muted-foreground">Loading validation findings</p>
            ) : (validation.data?.findings.length ?? 0) === 0 ? (
              <p className="mt-2 text-sm text-muted-foreground">No validation findings for this version.</p>
            ) : visibleValidationFindings.length === 0 ? (
              <p className="mt-2 text-sm text-muted-foreground">No validation findings match the search.</p>
            ) : (
              <ul className="mt-3 space-y-2">
                {visibleValidationFindings.map((finding, index) => (
                  <li key={`${finding.severity}-${finding.code ?? index}-${finding.path ?? ""}`} className="rounded-ui border border-border bg-background p-3 text-sm">
                    <div className="flex flex-wrap items-center gap-2">
                      <Badge className={statusToneClass(sourceStatusTone(finding.severity))}>{finding.severity}</Badge>
                      {finding.blocksPublicVisibility ? <Badge className={statusToneClass(sourceStatusTone("Blocking"))}>Blocks public visibility</Badge> : null}
                      {finding.code ? <span className="font-medium">{finding.code}</span> : null}
                      {finding.path ? <span className="text-muted-foreground">{finding.path}</span> : null}
                    </div>
                    <p className="mt-1 text-muted-foreground">{finding.message}</p>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section id={sectionElementId("features")} className="rounded-ui border border-border bg-surface p-4">
            <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
              <h2 className="text-base font-medium">Features, Dependencies, and Compatibility</h2>
              <label className="relative block md:w-72">
                <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
                <Input value={inspectionSearch} onChange={(event) => setInspectionSearch(event.target.value)} className="pl-9" placeholder="Filter package surface" aria-label="Filter package surface" />
              </label>
            </div>

            <div className="mt-3 grid gap-4 lg:grid-cols-[14rem_minmax(0,1fr)]">
              <aside className="lg:border-r lg:border-border lg:pr-4" aria-label="Feature categories">
                <div className="flex items-center justify-between gap-3">
                  <h3 className="text-sm font-medium">Categories</h3>
                  {selectedFeatureCategories.length > 0 ? (
                    <button type="button" className="text-xs font-medium text-primary hover:underline" onClick={() => setSelectedFeatureCategories([])}>
                      Clear
                    </button>
                  ) : null}
                </div>
                <div className="mt-2 flex gap-2 overflow-x-auto pb-1 lg:block lg:space-y-1 lg:overflow-visible lg:pb-0">
                  <button
                    type="button"
                    className={categoryFilterClassName(allFeatureCategoriesSelected)}
                    aria-pressed={allFeatureCategoriesSelected}
                    onClick={() => setSelectedFeatureCategories([])}
                  >
                    <span>All features</span>
                    <span className={cn("text-xs", allFeatureCategoriesSelected ? "text-background/80" : "text-muted-foreground")}>{normalizedFeatures.length}</span>
                  </button>
                  {featureCategoryOptions.map(({ category, count }) => {
                    const selected = selectedFeatureCategorySet.has(category);
                    return (
                      <button
                        key={category}
                        type="button"
                        className={categoryFilterClassName(selected)}
                        aria-pressed={selected}
                        onClick={() => toggleFeatureCategory(category, setSelectedFeatureCategories)}
                      >
                        <span className="inline-flex min-w-0 items-center gap-2">
                          {selected ? <Check className="h-3.5 w-3.5 shrink-0" /> : null}
                          <span className="truncate">{category}</span>
                        </span>
                        <span className={cn("text-xs", selected ? "text-background/80" : "text-muted-foreground")}>{count}</span>
                      </button>
                    );
                  })}
                </div>
              </aside>

              <div className="min-w-0">
                <p className="text-xs text-muted-foreground">
                  Showing {visibleFeatures.length} of {normalizedFeatures.length} features
                  {selectedFeatureCategories.length > 0 ? ` in ${selectedFeatureCategories.join(", ")}` : ""}
                  {inspectionSearch.trim() ? ` matching "${inspectionSearch.trim()}"` : ""}.
                </p>

                {selectedVersion && compatibilityVisible ? (
                  <div id={sectionElementId("compatibility")} className="mt-3 rounded-ui border border-border bg-background p-3 text-sm">
                    <h3 className="font-medium">Compatibility</h3>
                    <dl className="mt-2 grid gap-3 sm:grid-cols-2">
                      <DetailItem label="Target frameworks" value={selectedVersion.compatibility.targetFrameworks.join(", ") || "None"} />
                      <DetailItem label="Elsa range" value={selectedVersion.compatibility.elsaVersionRange ?? "Unspecified"} />
                      <DetailItem label="Required capabilities" value={selectedVersion.compatibility.requiredCapabilities.join(", ") || "None"} />
                      <DetailItem label="Unsupported combinations" value={selectedVersion.compatibility.unsupportedCombinations.join(", ") || "None"} />
                    </dl>
                    {selectedVersion.compatibility.notes.length > 0 ? <p className="mt-2 text-muted-foreground">{selectedVersion.compatibility.notes.join(" ")}</p> : null}
                  </div>
                ) : null}

                {visibleFeatures.length === 0 ? (
                  <p className="mt-3 text-sm text-muted-foreground">No feature metadata matches the selected filters.</p>
                ) : (
                  <div id={sectionElementId("dependencies")} className="mt-3 space-y-3">
                    {visibleFeatures.map((feature) => (
                      <div key={feature.featureId} className="rounded-ui border border-border bg-background p-3 text-sm">
                        <div className="flex flex-wrap items-center gap-2">
                          <h3 className="font-medium">{feature.displayName}</h3>
                          {feature.advanced ? <Badge>Advanced</Badge> : null}
                          {feature.experimental ? <Badge>Experimental</Badge> : null}
                        </div>
                        <p className="mt-1 text-muted-foreground">{feature.description ?? feature.typeName}</p>
                        <dl className="mt-3 grid gap-3 sm:grid-cols-2">
                          <DetailItem label="Feature ID" value={feature.featureId} />
                          <DetailItem label="Type" value={feature.typeName} />
                          <DetailItem label="Category" value={formatFeatureCategories(feature.categories)} />
                          <DetailItem label="Dependencies" value={formatDependencies(feature.dependencies)} />
                          <DetailItem label="Conflicts" value={formatConflicts(feature.conflicts)} />
                          <DetailItem label="Infrastructure" value={formatInfrastructure(feature.infrastructure)} />
                        </dl>
                        {feature.settings.length > 0 ? (
                          <div className="mt-3 overflow-x-auto">
                            <table className="min-w-full text-left text-xs">
                              <thead className="text-muted-foreground">
                                <tr>
                                  <th className="py-1 pr-3">Setting</th>
                                  <th className="py-1 pr-3">Name</th>
                                  <th className="py-1 pr-3">Type</th>
                                  <th className="py-1 pr-3">Required</th>
                                  <th className="py-1 pr-3">Secret</th>
                                  <th className="py-1 pr-3">Restart</th>
                                  <th className="py-1 pr-3">Default</th>
                                  <th className="py-1 pr-3">Validation</th>
                                  <th className="py-1 pr-3">Environment</th>
                                  <th className="py-1 pr-3">Notes</th>
                                </tr>
                              </thead>
                              <tbody>
                                {feature.settings.map((setting) => (
                                  <tr key={setting.name}>
                                    <td className="py-1 pr-3 font-medium">{setting.displayName}</td>
                                    <td className="py-1 pr-3">{setting.name}</td>
                                    <td className="py-1 pr-3">{setting.jsonType}</td>
                                    <td className="py-1 pr-3">{setting.required ? "Yes" : "No"}</td>
                                    <td className="py-1 pr-3">{setting.secret ? "Yes" : "No"}</td>
                                    <td className="py-1 pr-3">{setting.restartRequired ? "Yes" : "No"}</td>
                                    <td className="py-1 pr-3">{setting.defaultValueJson ?? "None"}</td>
                                    <td className="py-1 pr-3">{setting.validationJson && setting.validationJson !== "{}" ? setting.validationJson : "None"}</td>
                                    <td className="py-1 pr-3">{setting.environmentVariable ?? "None"}</td>
                                    <td className="py-1 pr-3">{[setting.category, setting.description].filter(Boolean).join(" - ") || "None"}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </div>
                        ) : null}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>
          </section>

          <section id={sectionElementId("manifest")} className="rounded-ui border border-border bg-surface p-4">
            <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
              <h2 className="text-base font-medium">Manifest</h2>
              <label className="relative block md:w-72">
                <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
                <Input value={manifestSearch} onChange={(event) => setManifestSearch(event.target.value)} className="pl-9" placeholder="Search manifest" aria-label="Search manifest" />
              </label>
            </div>
            {manifest.isError ? (
              <RequestStateView state="stale" title="Manifest content could not load" description="Manifest metadata is still available. Refresh to retry manifest content." />
            ) : selectedVersion?.manifest.available ? (
              <div className="mt-3 space-y-3">
                <dl className="grid gap-3 sm:grid-cols-3">
                  <DetailItem label="Schema" value={selectedVersion.manifest.schemaVersion ?? "Unknown"} />
                  <DetailItem label="Stored hash" value={selectedVersion.manifest.manifestHash} />
                  <DetailItem label="Suspicious hash" value={selectedVersion.manifest.suspiciousManifestHash ?? "None"} />
                </dl>
                {manifest.isLoading ? (
                  <p className="text-sm text-muted-foreground">Loading manifest content</p>
                ) : manifestVisible ? (
                  <pre className="max-h-[28rem] overflow-auto rounded-ui border border-border bg-background p-3 text-xs">{formattedManifest.value}</pre>
                ) : (
                  <p className="text-sm text-muted-foreground">No manifest content matches the search.</p>
                )}
              </div>
            ) : (
              <p className="mt-2 text-sm text-muted-foreground">Manifest content is not available for this version.</p>
            )}
          </section>
        </div>

        <aside className="space-y-4">
          <section id={sectionElementId("actions")} className="rounded-ui border border-border bg-surface p-4">
            <h2 className="text-base font-medium">Version Actions</h2>
            {selectedVersion ? (
              <div className="mt-3 space-y-3">
                <Input value={rejectionReason} onChange={(event) => setRejectionReason(event.target.value)} placeholder="Rejection reason" aria-label="Rejection reason" />
                {actionTokenStale ? <p className="text-sm text-warning">This version changed since review. Refresh before approving or rejecting it.</p> : null}
                <div className="flex flex-col gap-2">
                  <Button onClick={() => confirmAction(details.packageId, selectedVersion.version, "approve") && approveVersion.mutate()} disabled={!selectedAction || approveVersion.isPending || rejectVersion.isPending}>
                    <Check className="h-4 w-4" />
                    Approve Version
                  </Button>
                  <SecondaryButton onClick={() => confirmAction(details.packageId, selectedVersion.version, "reject") && rejectVersion.mutate()} disabled={!selectedAction || !rejectionReason.trim() || approveVersion.isPending || rejectVersion.isPending}>
                    <X className="h-4 w-4" />
                    Reject Version
                  </SecondaryButton>
                </div>
                {actionMessage ? <p className="text-sm text-muted-foreground">{actionMessage}</p> : null}
              </div>
            ) : (
              <p className="mt-2 text-sm text-muted-foreground">Version actions are unavailable until a version is indexed.</p>
            )}
          </section>

          <section className="rounded-ui border border-border bg-surface p-4">
            <h2 className="text-base font-medium">Source</h2>
            {details.source ? (
              <dl className="mt-3 space-y-3">
                <DetailItem label="Name" value={details.source.name} />
                <DetailItem label="Status" value={details.source.status} />
                <DetailItem label="Enabled" value={details.source.enabled ? "Yes" : "No"} />
                <DetailItem label="Last sync" value={formatDateTime(details.source.lastSyncedAt)} />
                <DetailItem label="Successful sync" value={formatDateTime(details.source.lastSuccessfulSyncAt)} />
              </dl>
            ) : (
              <p className="mt-2 text-sm text-muted-foreground">No source metadata is attached to this package.</p>
            )}
          </section>

          <section className="rounded-ui border border-border bg-surface p-4">
            <h2 className="text-base font-medium">Versions</h2>
            <div className="mt-3 space-y-2">
              {details.versions.map((item) => (
                <Link
                  key={item.version}
                  to={versionPath(details.packageId, item.version, activeSection)}
                  className="block rounded-ui border border-border bg-background px-3 py-2 text-sm hover:bg-muted"
                  aria-current={selectedVersion?.version === item.version ? "page" : undefined}
                >
                  <span className="flex items-center justify-between gap-3">
                    <span className="font-medium">{item.version}</span>
                    <span className="text-muted-foreground">{formatDateTime(item.indexedAt)}</span>
                  </span>
                  <span className="mt-2 flex flex-wrap gap-1">
                    <Badge className={statusToneClass(sourceStatusTone(item.approvalStatus))}>{item.approvalStatus}</Badge>
                    <Badge className={statusToneClass(sourceStatusTone(item.validationStatus))}>{item.validationStatus}</Badge>
                    <Badge className={statusToneClass(sourceStatusTone(item.isListed ? "Listed" : "Unlisted"))}>
                      {item.isListed ? "Listed" : "Unlisted"}
                    </Badge>
                    {item.suspiciousChangeDetected ? <Badge className={statusToneClass(sourceStatusTone("Suspicious"))}>Suspicious</Badge> : null}
                  </span>
                </Link>
              ))}
            </div>
          </section>
        </aside>
      </div>
    </section>
  );
}

function DetailItem({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs uppercase text-muted-foreground">{label}</dt>
      <dd className="mt-1 break-words text-sm font-medium">{value}</dd>
    </div>
  );
}

function requestState(error: Error) {
  if (error instanceof ApiError) {
    if (error.kind === "Unauthorized" || error.kind === "Forbidden") return "unauthorized";
    if (error.kind === "NotFound") return "not-found";
  }
  return "unexpected";
}

function isAccessError(error: Error | null) {
  return error instanceof ApiError && (error.kind === "Unauthorized" || error.kind === "Forbidden");
}

function actionErrorMessage(error: Error) {
  if (error instanceof ApiError) {
    if (error.kind === "Conflict") return "Version state changed. Refresh package details before retrying this action.";
    if (error.kind === "Validation") return error.message;
    if (error.kind === "NotFound") return "This package version no longer exists.";
    if (error.kind === "Unauthorized" || error.kind === "Forbidden") return "You no longer have access to perform this action.";
  }
  return "Package action failed. Refresh and try again.";
}

function versionPath(packageId: string, version: string, section: string) {
  const base = `/admin/packages/${encodeURIComponent(packageId)}/versions/${encodeURIComponent(version)}`;
  return section === "summary" ? base : `${base}/${section}`;
}

function sectionElementId(section: string) {
  return `package-details-${section}`;
}

function reviewTokenKey(packageId: string, version: string) {
  return `${packageId}@${version}`;
}

function toggleFeatureCategory(category: string, setSelectedFeatureCategories: Dispatch<SetStateAction<string[]>>) {
  setSelectedFeatureCategories((current) =>
    current.includes(category) ? current.filter((item) => item !== category) : [...current, category]
  );
}

function categoryFilterClassName(selected: boolean) {
  return cn(
    "flex min-w-36 items-center justify-between gap-3 whitespace-nowrap rounded-ui border px-3 py-2 text-left text-sm transition-colors lg:w-full",
    selected ? "border-foreground bg-foreground text-background" : "border-border bg-background text-foreground hover:bg-muted"
  );
}

function confirmAction(packageId: string, version: string, action: "approve" | "reject") {
  return window.confirm(`${action === "approve" ? "Approve" : "Reject"} ${packageId} version ${version}?`);
}

function formatFeatureCategories(categories: string[]) {
  return categories.join(", ") || "Uncategorized";
}

function formatDependencies(dependencies: PackageFeatureDependency[]) {
  return dependencies
    .map((item) =>
      [
        item.packageId ?? item.featureId ?? "dependency",
        item.versionRange,
        item.optional ? "optional" : null,
        item.reason
      ].filter(Boolean).join(" ")
    )
    .join("; ") || "None";
}

function formatConflicts(conflicts: PackageFeatureConflict[]) {
  return conflicts
    .map((item) =>
      [
        item.packageId ?? item.featureId ?? "conflict",
        item.versionRange,
        item.reason
      ].filter(Boolean).join(" ")
    )
    .join("; ") || "None";
}

function formatInfrastructure(requirements: PackageInfrastructureRequirement[]) {
  return requirements
    .map((item) =>
      [
        item.kind ?? item.id ?? "requirement",
        item.optional ? "optional" : null,
        item.reason,
        item.capabilities?.join("/"),
        item.providers?.join("/"),
        item.configurationKeys?.join("/")
      ].filter(Boolean).join(" ")
    )
    .join("; ") || "None";
}
