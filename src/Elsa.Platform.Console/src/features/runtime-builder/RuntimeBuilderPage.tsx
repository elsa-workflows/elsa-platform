import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, Copy, FileCode2, LogIn, PackagePlus, Play, RefreshCw, Save, Search, Settings2, Trash2 } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Badge, Button, EmptyState, Input, SecondaryButton, Select } from "@/components/ui";
import { RequestStateView } from "@/components/states/RequestStateViews";
import {
  createRuntimeConfiguration,
  generateBundle,
  getBuilderCatalog,
  getWorkspaceContext,
  listRuntimeConfigurations,
  planRuntime
} from "@/features/runtime-builder/runtimeBuilderApi";
import {
  defaultSelectedFeatures,
  deploymentTargets,
  findSelectedVersion,
  latestPackageVersion,
  normalizeFindingLevel,
  packageSelectionKey,
  selectedPackageKey,
  type BuilderBundleFile,
  type BuilderBundleResponse,
  type BuilderCatalog,
  type BuilderFinding,
  type BuilderPlanResponse,
  type BuilderPackage,
  type DeploymentTarget,
  type InfrastructureProvider,
  type RuntimeBuilderIntent,
  type RuntimeImage,
  type SelectedRuntimePackage
} from "@/features/runtime-builder/runtimeBuilderModels";
import { cn } from "@/lib/utils";
import { queryKeys } from "@/lib/query/queryClient";
import { sourceStatusTone, statusToneClass } from "@/lib/status/statusBadges";
import { ApiError } from "@/lib/api/httpClient";
import { startCustomerSignIn } from "@/lib/auth/authApi";

const defaultTarget: DeploymentTarget = "docker-compose";

export function RuntimeBuilderPage() {
  const queryClient = useQueryClient();
  const [workspaceId, setWorkspaceId] = useState("");
  const [imageSlug, setImageSlug] = useState("");
  const [imageTag, setImageTag] = useState("");
  const [hostPort, setHostPort] = useState("");
  const [target, setTarget] = useState<DeploymentTarget>(defaultTarget);
  const [envOverrides, setEnvOverrides] = useState<Record<string, string>>({});
  const [selectedPackages, setSelectedPackages] = useState<Record<string, SelectedRuntimePackage>>({});
  const [selectedInfrastructure, setSelectedInfrastructure] = useState<Record<string, boolean>>({});
  const [packageSearch, setPackageSearch] = useState("");
  const [localPackagesEnabled, setLocalPackagesEnabled] = useState(false);
  const [localPackagesPath, setLocalPackagesPath] = useState("./packages");
  const [configurationName, setConfigurationName] = useState("Workflow runtime");
  const [configurationDescription, setConfigurationDescription] = useState("");
  const [bundle, setBundle] = useState<BuilderBundleResponse | null>(null);
  const [selectedFilePath, setSelectedFilePath] = useState<string | null>(null);

  const workspaces = useQuery({
    queryKey: queryKeys.workspaceContext,
    queryFn: getWorkspaceContext
  });
  const effectiveWorkspaceId = workspaceId || workspaces.data?.workspaces[0]?.id || "";
  const catalog = useQuery({
    queryKey: queryKeys.runtimeBuilderCatalog(effectiveWorkspaceId),
    queryFn: () => getBuilderCatalog(effectiveWorkspaceId),
    enabled: Boolean(effectiveWorkspaceId)
  });
  const configurations = useQuery({
    queryKey: queryKeys.runtimeConfigurations(effectiveWorkspaceId),
    queryFn: () => listRuntimeConfigurations(effectiveWorkspaceId),
    enabled: Boolean(effectiveWorkspaceId)
  });

  useEffect(() => {
    if (!workspaceId && workspaces.data?.workspaces[0]?.id) {
      setWorkspaceId(workspaces.data.workspaces[0].id);
    }
  }, [workspaceId, workspaces.data?.workspaces]);

  useEffect(() => {
    if (!catalog.data || imageSlug) return;
    const image = catalog.data.images[0];
    if (!image) return;
    setImageSlug(image.slug);
    setImageTag(image.defaultTag);
    setHostPort(String(image.hostPort));
  }, [catalog.data, imageSlug]);

  const selectedImage = useMemo(
    () => catalog.data?.images.find((image) => image.slug === imageSlug) ?? catalog.data?.images[0] ?? null,
    [catalog.data?.images, imageSlug]
  );

  const filteredPackages = useMemo(() => {
    return filterPackages(catalog.data?.packages ?? [], packageSearch);
  }, [catalog.data?.packages, packageSearch]);

  const selectedPackageItems = useMemo(() => {
    return Object.values(selectedPackages)
      .map((selection) => {
        const packageItem = catalog.data?.packages.find(
          (item) => item.source.id === selection.sourceId && item.packageId.toLowerCase() === selection.packageId.toLowerCase()
        );
        return packageItem ? { selection, packageItem } : null;
      })
      .filter((item): item is { selection: SelectedRuntimePackage; packageItem: BuilderPackage } => Boolean(item));
  }, [catalog.data?.packages, selectedPackages]);

  const currentIntent = useMemo(() => {
    if (!catalog.data || !selectedImage) return null;
    return buildIntent({
      catalog: catalog.data,
      selectedImage,
      imageTag,
      hostPort,
      target,
      envOverrides,
      selectedPackages,
      selectedInfrastructure,
      localPackagesEnabled,
      localPackagesPath
    });
  }, [
    catalog.data,
    envOverrides,
    hostPort,
    imageTag,
    localPackagesEnabled,
    localPackagesPath,
    selectedImage,
    selectedInfrastructure,
    selectedPackages,
    target
  ]);

  const plan = useMutation({
    mutationFn: () => planRuntime(effectiveWorkspaceId, currentIntent!),
    onSuccess: (response) => {
      if (catalog.data) {
        applyIntent(response.resolved, catalog.data);
      }
      setBundle(null);
      setSelectedFilePath(null);
      void queryClient.invalidateQueries({ queryKey: queryKeys.runtimeBuilderCatalog(effectiveWorkspaceId) });
    }
  });

  const bundleGeneration = useMutation({
    mutationFn: () => generateBundle(effectiveWorkspaceId, currentIntent!),
    onSuccess: (response) => {
      setBundle(response);
      setSelectedFilePath(response.files[0]?.path ?? null);
    }
  });

  const saveConfiguration = useMutation({
    mutationFn: () =>
      createRuntimeConfiguration(effectiveWorkspaceId, {
        name: configurationName.trim(),
        description: configurationDescription.trim() || null,
        intent: currentIntent!
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.runtimeConfigurations(effectiveWorkspaceId) });
    }
  });

  function applyIntent(intent: RuntimeBuilderIntent, data: BuilderCatalog) {
    const image = data.images.find((item) => item.slug === intent.image.slug) ?? data.images[0];
    setImageSlug(image?.slug ?? intent.image.slug);
    setImageTag(intent.image.tag ?? image?.defaultTag ?? "");
    setHostPort(String(intent.image.hostPort ?? image?.hostPort ?? ""));
    setEnvOverrides(intent.image.envOverrides ?? {});
    setTarget((intent.target as DeploymentTarget | null) ?? defaultTarget);
    setLocalPackagesEnabled(Boolean(intent.localPackages?.enabled));
    setLocalPackagesPath(intent.localPackages?.directoryPath ?? "./packages");
    setSelectedInfrastructure(
      Object.fromEntries(intent.infrastructure.map((item) => [item.providerId, true]))
    );
    setSelectedPackages(
      Object.fromEntries(
        intent.packages.map((item) => [
          packageSelectionKey(item.sourceId, item.packageId),
          {
            sourceId: item.sourceId,
            packageId: item.packageId,
            version: item.version,
            selectedFeatures: [...(item.selectedFeatures ?? [])]
          }
        ])
      )
    );
  }

  function addPackage(packageItem: BuilderPackage) {
    const version = latestPackageVersion(packageItem);
    if (!version) return;
    const selection: SelectedRuntimePackage = {
      sourceId: packageItem.source.id,
      packageId: packageItem.packageId,
      version: version.version,
      selectedFeatures: defaultSelectedFeatures(version)
    };
    setSelectedPackages((current) => ({ ...current, [selectedPackageKey(selection)]: selection }));
    setBundle(null);
  }

  function removePackage(selection: SelectedRuntimePackage) {
    setSelectedPackages((current) => {
      const next = { ...current };
      delete next[selectedPackageKey(selection)];
      return next;
    });
    setBundle(null);
  }

  function updatePackage(selection: SelectedRuntimePackage, nextSelection: SelectedRuntimePackage) {
    setSelectedPackages((current) => ({ ...current, [selectedPackageKey(selection)]: nextSelection }));
    setBundle(null);
  }

  if (workspaces.isLoading) return <RequestStateView state="loading" title="Loading workspace context" />;
  if (workspaces.isError && !workspaces.data) {
    if (workspaces.error instanceof ApiError && workspaces.error.kind === "Unauthorized") {
      return (
        <EmptyState
          title="Sign in to load workspace context"
          description="Runtime Builder needs a customer identity before it can resolve workspaces, packages, and saved configurations."
          action={
            <Button onClick={() => startCustomerSignIn()}>
              <LogIn className="h-4 w-4" />
              Sign in
            </Button>
          }
        />
      );
    }

    return <RequestStateView state={workspaceContextErrorState(workspaces.error)} title="Workspace context could not load" />;
  }
  if ((workspaces.data?.workspaces.length ?? 0) === 0) {
    return <EmptyState title="No workspaces available" description="Runtime Builder needs a workspace before it can resolve packages and save configurations." />;
  }
  if (catalog.isLoading) return <RequestStateView state="loading" title="Loading Runtime Builder catalog" />;
  if (catalog.isError && !catalog.data) return <RequestStateView state="unexpected" title="Runtime Builder catalog could not load" />;
  if (!catalog.data || !selectedImage) return <EmptyState title="Runtime Builder catalog is empty" description="Add approved packages and runtime images before building a runtime." />;

  const selectedFile = bundle?.files.find((file) => file.path === selectedFilePath) ?? bundle?.files[0] ?? null;
  const findings = [...(plan.data?.findings ?? []), ...(bundle?.findings ?? [])];
  const canSubmit = Boolean(currentIntent && effectiveWorkspaceId && selectedImage);
  const autoAdded = plan.data?.autoAdded;
  const hasPlannerAdditions = hasAutoAddedItems(autoAdded);

  return (
    <section className="space-y-5">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <h1 className="text-2xl font-semibold">Runtime Builder</h1>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
            Compose runtime images, package features, infrastructure providers, and deployment artifacts for a workspace.
          </p>
        </div>
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
          <Select value={effectiveWorkspaceId} onChange={(event) => setWorkspaceId(event.target.value)} aria-label="Workspace">
            {workspaces.data?.workspaces.map((workspace) => (
              <option key={workspace.id} value={workspace.id}>
                {workspace.name}
              </option>
            ))}
          </Select>
          <SecondaryButton onClick={() => void catalog.refetch()} title="Refresh Runtime Builder catalog">
            <RefreshCw className="h-4 w-4" />
            Refresh
          </SecondaryButton>
        </div>
      </div>

      {catalog.isRefetchError ? <RequestStateView state="stale" title="Showing the last loaded Runtime Builder catalog" /> : null}
      {plan.isError ? <RequestStateView state="unexpected" title="Planning failed" description="Review selected packages and try again." /> : null}
      {bundleGeneration.isError ? <RequestStateView state="unexpected" title="Bundle generation failed" description="Plan the runtime, then retry bundle generation." /> : null}
      {saveConfiguration.isError ? <RequestStateView state="unexpected" title="Runtime configuration could not be saved" /> : null}
      {saveConfiguration.isSuccess ? (
        <div className="rounded-ui border border-success/40 bg-surface p-3 text-sm text-success">Runtime configuration saved.</div>
      ) : null}

      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_24rem]">
        <div className="space-y-5">
          <section className="rounded-ui border border-border bg-surface p-4">
            <div className="flex items-center gap-2">
              <Settings2 className="h-4 w-4 text-primary" />
              <h2 className="text-base font-medium">Runtime Image</h2>
            </div>
            <div className="mt-4 grid gap-3 md:grid-cols-2 2xl:grid-cols-[minmax(12rem,1fr)_10rem_8rem_12rem]">
              <label className="space-y-1 text-sm">
                <span className="font-medium">Image</span>
                <Select
                  className="w-full"
                  value={selectedImage.slug}
                  onChange={(event) => {
                    const image = catalog.data!.images.find((item) => item.slug === event.target.value);
                    if (!image) return;
                    setImageSlug(image.slug);
                    setImageTag(image.defaultTag);
                    setHostPort(String(image.hostPort));
                    setEnvOverrides({});
                    setBundle(null);
                  }}
                >
                  {catalog.data.images.map((image) => (
                    <option key={image.slug} value={image.slug}>
                      {image.displayName}
                    </option>
                  ))}
                </Select>
              </label>
              <label className="space-y-1 text-sm">
                <span className="font-medium">Tag</span>
                <Select value={imageTag || selectedImage.defaultTag} onChange={(event) => setImageTag(event.target.value)} className="w-full">
                  {selectedImage.availableTags.map((tag) => (
                    <option key={tag} value={tag}>
                      {tag}
                    </option>
                  ))}
                </Select>
              </label>
              <label className="space-y-1 text-sm">
                <span className="font-medium">Host port</span>
                <Input value={hostPort} onChange={(event) => setHostPort(event.target.value)} inputMode="numeric" />
              </label>
              <label className="space-y-1 text-sm">
                <span className="font-medium">Target</span>
                <Select value={target} onChange={(event) => setTarget(event.target.value as DeploymentTarget)} className="w-full">
                  {deploymentTargets.map((item) => (
                    <option key={item.value} value={item.value}>
                      {item.label}
                    </option>
                  ))}
                </Select>
              </label>
            </div>
            <div className="mt-3 flex flex-wrap gap-2">
              <Badge>{selectedImage.image}</Badge>
              <Badge>{selectedImage.licenseTier}</Badge>
              <Badge>{selectedImage.stability}</Badge>
              {selectedImage.capabilities.map((capability) => (
                <Badge key={capability}>{capability}</Badge>
              ))}
            </div>
            <p className="mt-3 text-sm text-muted-foreground">{selectedImage.description}</p>

            {selectedImage.envVars.length > 0 ? (
              <div className="mt-4 grid gap-3 md:grid-cols-2">
                {selectedImage.envVars.map((envVar) => (
                  <label key={envVar.name} className="space-y-1 text-sm">
                    <span className="flex items-center gap-2 font-medium">
                      {envVar.displayName}
                      {envVar.required ? <Badge className={statusToneClass(sourceStatusTone("Blocking"))}>Required</Badge> : null}
                      {envVar.secret ? <Badge>Secret</Badge> : null}
                    </span>
                    <Input
                      type={envVar.secret ? "password" : "text"}
                      value={envOverrides[envVar.name] ?? ""}
                      onChange={(event) => setEnvOverrides((current) => ({ ...current, [envVar.name]: event.target.value }))}
                      placeholder={envVar.defaultValue ?? envVar.name}
                    />
                    <span className="block text-xs text-muted-foreground">{envVar.description}</span>
                  </label>
                ))}
              </div>
            ) : null}
          </section>

          <section className="rounded-ui border border-border bg-surface p-4">
            <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
              <div>
                <h2 className="text-base font-medium">Packages and Features</h2>
                <p className="mt-1 text-sm text-muted-foreground">Add approved package versions and choose the feature surface for this runtime.</p>
              </div>
              <label className="relative block md:w-72">
                <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
                <Input
                  value={packageSearch}
                  onChange={(event) => setPackageSearch(event.target.value)}
                  className="pl-9"
                  placeholder="Search packages"
                  aria-label="Search builder packages"
                />
              </label>
            </div>

            <div className="mt-4 grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)]">
              <div className="space-y-2">
                <h3 className="text-sm font-medium">Catalog</h3>
                {filteredPackages.length === 0 ? (
                  <p className="rounded-ui border border-dashed border-border p-4 text-sm text-muted-foreground">No packages match the current search.</p>
                ) : (
                  <ul className="max-h-[26rem] space-y-2 overflow-auto pr-1">
                    {filteredPackages.map((packageItem) => {
                      const latest = latestPackageVersion(packageItem);
                      const isSelected = packageSelectionKey(packageItem.source.id, packageItem.packageId) in selectedPackages;
                      return (
                        <li key={`${packageItem.source.id}:${packageItem.packageId}`} className="rounded-ui border border-border bg-background p-3">
                          <div className="flex items-start justify-between gap-3">
                            <div className="min-w-0">
                              <p className="truncate text-sm font-medium">{packageItem.displayName || packageItem.packageId}</p>
                              <p className="truncate text-xs text-muted-foreground">{packageItem.packageId}</p>
                              <p className="mt-1 text-xs text-muted-foreground">
                                {latest?.version ?? "No versions"} · {packageItem.source.name}
                              </p>
                            </div>
                            <SecondaryButton
                              className="h-8 shrink-0 px-2"
                              disabled={!latest || isSelected}
                              aria-label={`Add ${packageItem.packageId}`}
                              onClick={() => addPackage(packageItem)}
                            >
                              {isSelected ? <Check className="h-4 w-4" /> : <PackagePlus className="h-4 w-4" />}
                            </SecondaryButton>
                          </div>
                        </li>
                      );
                    })}
                  </ul>
                )}
              </div>

              <div className="space-y-2">
                <h3 className="text-sm font-medium">Selected</h3>
                {selectedPackageItems.length === 0 ? (
                  <p className="rounded-ui border border-dashed border-border p-4 text-sm text-muted-foreground">Add a package to configure selected features.</p>
                ) : (
                  <ul className="space-y-3">
                    {selectedPackageItems.map(({ selection, packageItem }) => (
                      <SelectedPackageEditor
                        key={selectedPackageKey(selection)}
                        packageItem={packageItem}
                        selection={selection}
                        onChange={(next) => updatePackage(selection, next)}
                        onRemove={() => removePackage(selection)}
                      />
                    ))}
                  </ul>
                )}
              </div>
            </div>
          </section>

          <section className="rounded-ui border border-border bg-surface p-4">
            <h2 className="text-base font-medium">Infrastructure</h2>
            <div className="mt-4 grid gap-3 md:grid-cols-2">
              {catalog.data.infrastructureProviders.map((provider) => (
                <label key={provider.id} className="flex items-start gap-3 rounded-ui border border-border bg-background p-3">
                  <input
                    type="checkbox"
                    className="mt-1 h-4 w-4 rounded border-border"
                    checked={Boolean(selectedInfrastructure[provider.id])}
                    onChange={(event) => {
                      setSelectedInfrastructure((current) => ({ ...current, [provider.id]: event.target.checked }));
                      setBundle(null);
                    }}
                  />
                  <span className="min-w-0">
                    <span className="block text-sm font-medium">{provider.displayName}</span>
                    <span className="block text-xs text-muted-foreground">
                      {provider.kind} · {provider.strategy} · {provider.provider}
                    </span>
                    {provider.capabilities.length > 0 ? (
                      <span className="mt-2 flex flex-wrap gap-1">
                        {provider.capabilities.map((capability) => (
                          <Badge key={capability}>{capability}</Badge>
                        ))}
                      </span>
                    ) : null}
                  </span>
                </label>
              ))}
            </div>
          </section>

          <section className="rounded-ui border border-border bg-surface p-4">
            <h2 className="text-base font-medium">Saved Configuration</h2>
            <div className="mt-4 grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto]">
              <label className="space-y-1 text-sm">
                <span className="font-medium">Name</span>
                <Input value={configurationName} onChange={(event) => setConfigurationName(event.target.value)} />
              </label>
              <label className="space-y-1 text-sm">
                <span className="font-medium">Description</span>
                <Input value={configurationDescription} onChange={(event) => setConfigurationDescription(event.target.value)} />
              </label>
              <div className="flex items-end">
                <Button
                  className="w-full"
                  disabled={!canSubmit || !configurationName.trim() || saveConfiguration.isPending}
                  onClick={() => saveConfiguration.mutate()}
                >
                  <Save className="h-4 w-4" />
                  Save
                </Button>
              </div>
            </div>
            <div className="mt-3 grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  className="h-4 w-4 rounded border-border"
                  checked={localPackagesEnabled}
                  onChange={(event) => setLocalPackagesEnabled(event.target.checked)}
                />
                Include local packages directory
              </label>
              <Input
                value={localPackagesPath}
                onChange={(event) => setLocalPackagesPath(event.target.value)}
                disabled={!localPackagesEnabled}
                aria-label="Local packages directory"
              />
            </div>
            {(configurations.data?.length ?? 0) > 0 ? (
              <div className="mt-4 flex flex-wrap gap-2">
                {configurations.data!.map((configuration) => (
                  <SecondaryButton
                    key={configuration.id}
                    className="h-8"
                    onClick={() => {
                      applyIntent(configuration.intent, catalog.data!);
                      setConfigurationName(configuration.name);
                      setConfigurationDescription(configuration.description ?? "");
                      setBundle(null);
                    }}
                  >
                    <Copy className="h-4 w-4" />
                    {configuration.name}
                  </SecondaryButton>
                ))}
              </div>
            ) : null}
          </section>
        </div>

        <aside className="space-y-4">
          <section className="rounded-ui border border-border bg-surface p-4">
            <h2 className="text-base font-medium">Build Summary</h2>
            <dl className="mt-4 grid grid-cols-2 gap-3 text-sm">
              <SummaryItem label="Image" value={selectedImage.displayName} />
              <SummaryItem label="Tag" value={imageTag || selectedImage.defaultTag} />
              <SummaryItem label="Packages" value={selectedPackageItems.length.toString()} />
              <SummaryItem label="Features" value={countFeatures(selectedPackages).toString()} />
              <SummaryItem label="Infrastructure" value={Object.values(selectedInfrastructure).filter(Boolean).length.toString()} />
              <SummaryItem label="Target" value={deploymentTargets.find((item) => item.value === target)?.label ?? target} />
            </dl>
            <div className="mt-4 grid gap-2">
              <Button disabled={!canSubmit || plan.isPending} onClick={() => plan.mutate()}>
                <Play className="h-4 w-4" />
                {plan.isPending ? "Planning" : "Plan Runtime"}
              </Button>
              <SecondaryButton disabled={!canSubmit || bundleGeneration.isPending} onClick={() => bundleGeneration.mutate()}>
                <FileCode2 className="h-4 w-4" />
                {bundleGeneration.isPending ? "Generating" : "Generate Bundle"}
              </SecondaryButton>
            </div>
            {hasPlannerAdditions && autoAdded ? (
              <div className="mt-4 rounded-ui border border-border bg-background p-3 text-sm">
                <p className="font-medium">Planner additions</p>
                <p className="mt-1 text-muted-foreground">
                  {autoAdded.packages.length} packages, {autoAdded.features.length} features, {autoAdded.infrastructure.length} infrastructure providers
                </p>
              </div>
            ) : null}
          </section>

          <FindingsPanel findings={findings} />
          <BundlePreview bundle={bundle} selectedFile={selectedFile} onFileSelect={setSelectedFilePath} />
        </aside>
      </div>
    </section>
  );
}

function SelectedPackageEditor({
  packageItem,
  selection,
  onChange,
  onRemove
}: {
  packageItem: BuilderPackage;
  selection: SelectedRuntimePackage;
  onChange: (selection: SelectedRuntimePackage) => void;
  onRemove: () => void;
}) {
  const version = findSelectedVersion(packageItem, selection.version);
  const features = version?.features ?? [];

  return (
    <li className="rounded-ui border border-border bg-background p-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate text-sm font-medium">{packageItem.displayName || packageItem.packageId}</p>
          <p className="truncate text-xs text-muted-foreground">{packageItem.packageId}</p>
        </div>
        <SecondaryButton className="h-8 shrink-0 px-2" aria-label={`Remove ${packageItem.packageId}`} onClick={onRemove}>
          <Trash2 className="h-4 w-4" />
        </SecondaryButton>
      </div>
      <label className="mt-3 block space-y-1 text-sm">
        <span className="font-medium">Version</span>
        <Select
          className="w-full"
          value={selection.version}
          onChange={(event) => {
            const nextVersion = packageItem.versions.find((item) => item.version === event.target.value);
            onChange({
              ...selection,
              version: event.target.value,
              selectedFeatures: nextVersion ? defaultSelectedFeatures(nextVersion) : []
            });
          }}
        >
          {packageItem.versions.map((item) => (
            <option key={item.version} value={item.version}>
              {item.version}
            </option>
          ))}
        </Select>
      </label>
      {features.length > 0 ? (
        <div className="mt-3 space-y-2">
          {features.map((feature) => {
            const checked = selection.selectedFeatures.includes(feature.featureId);
            return (
              <label key={feature.featureId} className="flex items-start gap-3 rounded-ui border border-border p-2">
                <input
                  type="checkbox"
                  className="mt-1 h-4 w-4 rounded border-border"
                  checked={checked}
                  onChange={(event) => {
                    const selectedFeatures = event.target.checked
                      ? [...selection.selectedFeatures, feature.featureId]
                      : selection.selectedFeatures.filter((item) => item !== feature.featureId);
                    onChange({
                      ...selection,
                      selectedFeatures: selectedFeatures.sort((left, right) => left.localeCompare(right, undefined, { sensitivity: "base" }))
                    });
                  }}
                />
                <span className="min-w-0">
                  <span className="block text-sm font-medium">{feature.displayName}</span>
                  <span className="block text-xs text-muted-foreground">{feature.description ?? feature.featureId}</span>
                  <span className="mt-1 flex flex-wrap gap-1">
                    {feature.experimental ? <Badge>Experimental</Badge> : null}
                    {feature.advanced ? <Badge>Advanced</Badge> : null}
                    {feature.settings.filter((setting) => setting.required).length > 0 ? <Badge>Required settings</Badge> : null}
                  </span>
                </span>
              </label>
            );
          })}
        </div>
      ) : (
        <p className="mt-3 text-sm text-muted-foreground">This version does not expose selectable features.</p>
      )}
    </li>
  );
}

function FindingsPanel({ findings }: { findings: BuilderFinding[] }) {
  return (
    <section className="rounded-ui border border-border bg-surface p-4">
      <h2 className="text-base font-medium">Findings</h2>
      {findings.length === 0 ? (
        <p className="mt-2 text-sm text-muted-foreground">No planner or bundle findings yet.</p>
      ) : (
        <ul className="mt-3 space-y-2">
          {findings.map((finding, index) => {
            const level = normalizeFindingLevel(finding.level);
            return (
              <li key={`${finding.code}:${index}`} className="rounded-ui border border-border bg-background p-3 text-sm">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge className={findingTone(level)}>{finding.level}</Badge>
                  <span className="font-medium">{finding.code}</span>
                </div>
                <p className="mt-1 text-muted-foreground">{finding.message}</p>
                {finding.scope ? <p className="mt-1 text-xs text-muted-foreground">{finding.scope}</p> : null}
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

function BundlePreview({
  bundle,
  selectedFile,
  onFileSelect
}: {
  bundle: BuilderBundleResponse | null;
  selectedFile: BuilderBundleFile | null;
  onFileSelect: (path: string) => void;
}) {
  return (
    <section className="rounded-ui border border-border bg-surface p-4">
      <h2 className="text-base font-medium">Bundle Preview</h2>
      {!bundle ? (
        <p className="mt-2 text-sm text-muted-foreground">Generate a bundle to inspect rendered files.</p>
      ) : (
        <div className="mt-3 space-y-3">
          <p className="text-sm text-muted-foreground">Bundle {bundle.bundleId}</p>
          <div className="flex flex-wrap gap-2">
            {bundle.files.map((file) => (
              <button
                key={file.path}
                type="button"
                className={cn(
                  "rounded-ui border border-border px-2 py-1 text-xs transition-colors",
                  selectedFile?.path === file.path ? "bg-foreground text-background" : "bg-background text-foreground hover:bg-muted"
                )}
                onClick={() => onFileSelect(file.path)}
              >
                {file.path}
              </button>
            ))}
          </div>
          {selectedFile ? (
            <pre className="max-h-[24rem] overflow-auto rounded-ui border border-border bg-background p-3 text-xs text-foreground">
              <code>{selectedFile.contents}</code>
            </pre>
          ) : null}
        </div>
      )}
    </section>
  );
}

function SummaryItem({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-1 truncate font-medium">{value}</dd>
    </div>
  );
}

function filterPackages(packages: BuilderPackage[], query: string) {
  const term = query.trim().toLowerCase();
  if (!term) return packages;
  return packages.filter((packageItem) => {
    const latest = latestPackageVersion(packageItem);
    return `${packageItem.packageId} ${packageItem.displayName} ${latest?.version ?? ""} ${packageItem.source.name}`.toLowerCase().includes(term);
  });
}

function buildIntent({
  catalog,
  selectedImage,
  imageTag,
  hostPort,
  target,
  envOverrides,
  selectedPackages,
  selectedInfrastructure,
  localPackagesEnabled,
  localPackagesPath
}: {
  catalog: BuilderCatalog;
  selectedImage: RuntimeImage;
  imageTag: string;
  hostPort: string;
  target: DeploymentTarget;
  envOverrides: Record<string, string>;
  selectedPackages: Record<string, SelectedRuntimePackage>;
  selectedInfrastructure: Record<string, boolean>;
  localPackagesEnabled: boolean;
  localPackagesPath: string;
}): RuntimeBuilderIntent {
  const packages = Object.values(selectedPackages).map((selection) => ({
    sourceId: selection.sourceId,
    packageId: selection.packageId,
    version: selection.version,
    selectedFeatures: selection.selectedFeatures
  }));
  const packageSources = uniqueSources(catalog, packages);
  const infrastructure = catalog.infrastructureProviders
    .filter((provider) => selectedInfrastructure[provider.id])
    .map((provider) => providerToSelection(provider));
  const trimmedEnvOverrides = Object.fromEntries(Object.entries(envOverrides).filter(([, value]) => value.trim()));
  const parsedHostPort = Number.parseInt(hostPort, 10);

  return {
    image: {
      slug: selectedImage.slug,
      tag: imageTag || selectedImage.defaultTag,
      hostPort: Number.isFinite(parsedHostPort) ? parsedHostPort : selectedImage.hostPort,
      envOverrides: Object.keys(trimmedEnvOverrides).length > 0 ? trimmedEnvOverrides : null
    },
    packages,
    packageSources,
    infrastructure,
    localPackages: localPackagesEnabled ? { enabled: true, directoryPath: localPackagesPath.trim() || null } : null,
    target
  };
}

function uniqueSources(catalog: BuilderCatalog, packages: Array<{ sourceId: string; packageId: string }>) {
  const sources = new Map<string, { sourceId: string; name?: string | null; url?: string | null; kind?: string | null }>();
  for (const selected of packages) {
    const packageItem = catalog.packages.find(
      (item) => item.source.id === selected.sourceId && item.packageId.toLowerCase() === selected.packageId.toLowerCase()
    );
    if (packageItem) {
      sources.set(packageItem.source.id, {
        sourceId: packageItem.source.id,
        name: packageItem.source.name,
        url: packageItem.source.url,
        kind: "NuGet"
      });
    }
  }
  return Array.from(sources.values());
}

function providerToSelection(provider: InfrastructureProvider) {
  return {
    kind: provider.kind,
    providerId: provider.id,
    strategy: provider.strategy,
    settings: null
  };
}

function countFeatures(selectedPackages: Record<string, SelectedRuntimePackage>) {
  return Object.values(selectedPackages).reduce((count, selection) => count + selection.selectedFeatures.length, 0);
}

function findingTone(level: string) {
  if (level === "error") return statusToneClass(sourceStatusTone("Error"));
  if (level === "warning") return statusToneClass(sourceStatusTone("Warning"));
  return statusToneClass(sourceStatusTone("Info"));
}

function hasAutoAddedItems(autoAdded: BuilderPlanResponse["autoAdded"] | undefined) {
  return Boolean(autoAdded && (autoAdded.packages.length > 0 || autoAdded.features.length > 0 || autoAdded.infrastructure.length > 0));
}

function workspaceContextErrorState(error: unknown) {
  return error instanceof ApiError && (error.kind === "Unauthorized" || error.kind === "Forbidden") ? "unauthorized" : "unexpected";
}
