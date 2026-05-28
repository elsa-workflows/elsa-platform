import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Archive, RotateCcw, Save } from "lucide-react";
import { useMemo, useState } from "react";
import { Badge, Button, Input, SecondaryButton } from "@/components/ui";
import {
  archiveDeploymentTier,
  createDeploymentTier,
  previewDeploymentTierImpact,
  restoreDeploymentTier,
  updateDeploymentTier
} from "@/features/deployments/deploymentApi";
import type { DeploymentTierCapability, DeploymentTierImpactSummary, WorkspaceDeploymentTier } from "@/features/deployments/deploymentModels";
import { queryKeys } from "@/lib/query/queryClient";
import { cn } from "@/lib/utils";

type TierFormValues = {
  name: string;
  description: string;
  sortOrder: number;
  capabilities: string[];
};

export function DeploymentTiersPanel({
  workspaceId,
  canManageTiers,
  tiers,
  capabilities
}: {
  workspaceId: string;
  canManageTiers: boolean;
  tiers: WorkspaceDeploymentTier[];
  capabilities: DeploymentTierCapability[];
}) {
  const queryClient = useQueryClient();
  const [editingTierId, setEditingTierId] = useState("");
  const [impact, setImpact] = useState<DeploymentTierImpactSummary | null>(null);
  const editingTier = tiers.find((tier) => tier.id === editingTierId) ?? null;
  const nextSortOrder = tiers.reduce((max, tier) => Math.max(max, tier.sortOrder), 0) + 10;

  const refresh = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: queryKeys.deploymentTiers(workspaceId) }),
      queryClient.invalidateQueries({ queryKey: queryKeys.deploymentCockpit(workspaceId) })
    ]);
  };
  const createTier = useMutation({
    mutationFn: (values: TierFormValues) =>
      createDeploymentTier(workspaceId, {
        name: values.name,
        description: values.description || null,
        sortOrder: values.sortOrder,
        capabilities: values.capabilities
      }),
    onSuccess: refresh
  });
  const updateTier = useMutation({
    mutationFn: ({ tierId, values, impactAccepted }: { tierId: string; values: TierFormValues; impactAccepted: boolean }) =>
      updateDeploymentTier(workspaceId, tierId, {
        name: values.name,
        description: values.description || null,
        sortOrder: values.sortOrder,
        capabilities: values.capabilities,
        impactAccepted
      }),
    onSuccess: async () => {
      setImpact(null);
      await refresh();
    }
  });
  const previewImpact = useMutation({
    mutationFn: ({ tierId, capabilities: nextCapabilities }: { tierId: string; capabilities: string[] }) =>
      previewDeploymentTierImpact(workspaceId, tierId, { capabilities: nextCapabilities }),
    onSuccess: setImpact
  });
  const archiveTier = useMutation({
    mutationFn: (tierId: string) => archiveDeploymentTier(workspaceId, tierId),
    onSuccess: refresh
  });
  const restoreTier = useMutation({
    mutationFn: (tierId: string) => restoreDeploymentTier(workspaceId, tierId),
    onSuccess: refresh
  });

  const error =
    createTier.error instanceof Error
      ? createTier.error.message
      : updateTier.error instanceof Error
        ? updateTier.error.message
        : previewImpact.error instanceof Error
          ? previewImpact.error.message
          : archiveTier.error instanceof Error
            ? archiveTier.error.message
            : restoreTier.error instanceof Error
              ? restoreTier.error.message
              : undefined;

  return (
    <div className="space-y-4">
      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_380px]">
        <div className="space-y-3">
          {tiers.map((tier) => (
            <div key={tier.id} className="rounded-ui border border-border bg-surface p-3">
              <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <h3 className="text-sm font-semibold">{tier.name}</h3>
                    <Badge className={tier.status === "Active" ? "bg-success/10 text-success" : "bg-muted text-muted-foreground"}>{tier.status}</Badge>
                    {tier.isDefault ? <Badge>Default</Badge> : null}
                    <span className="text-xs text-muted-foreground">{tier.environmentCount} environments</span>
                  </div>
                  {tier.description ? <p className="mt-1 text-sm text-muted-foreground">{tier.description}</p> : null}
                  <div className="mt-2 flex flex-wrap gap-1">
                    {tier.capabilities.map((capabilityId) => (
                      <Badge key={capabilityId}>{capabilityLabel(capabilities, capabilityId)}</Badge>
                    ))}
                  </div>
                </div>
                <div className="flex gap-2">
                  <SecondaryButton className="h-8" disabled={!canManageTiers} onClick={() => { setEditingTierId(tier.id); setImpact(null); }}>
                    Edit
                  </SecondaryButton>
                  {tier.status === "Active" ? (
                    <SecondaryButton className="h-8" disabled={!canManageTiers || archiveTier.isPending} onClick={() => archiveTier.mutate(tier.id)}>
                      <Archive className="h-4 w-4" />
                      Archive
                    </SecondaryButton>
                  ) : (
                    <SecondaryButton className="h-8" disabled={!canManageTiers || restoreTier.isPending} onClick={() => restoreTier.mutate(tier.id)}>
                      <RotateCcw className="h-4 w-4" />
                      Restore
                    </SecondaryButton>
                  )}
                </div>
              </div>
            </div>
          ))}
        </div>
        <TierForm
          key={editingTier?.id ?? "new"}
          title={editingTier ? "Edit tier" : "Create tier"}
          canManageTiers={canManageTiers}
          capabilities={capabilities}
          initialValues={
            editingTier
              ? {
                  name: editingTier.name,
                  description: editingTier.description ?? "",
                  sortOrder: editingTier.sortOrder,
                  capabilities: editingTier.capabilities
                }
              : { name: "", description: "", sortOrder: nextSortOrder, capabilities: [] }
          }
          impact={impact}
          isSubmitting={createTier.isPending || updateTier.isPending}
          isPreviewing={previewImpact.isPending}
          error={error}
          onCancel={editingTier ? () => { setEditingTierId(""); setImpact(null); } : undefined}
          onPreview={
            editingTier
              ? (values) => previewImpact.mutate({ tierId: editingTier.id, capabilities: values.capabilities })
              : undefined
          }
          onSubmit={(values) =>
            editingTier
              ? updateTier.mutate({ tierId: editingTier.id, values, impactAccepted: Boolean(impact) })
              : createTier.mutate(values)
          }
        />
      </div>
    </div>
  );
}

function TierForm({
  title,
  canManageTiers,
  capabilities,
  initialValues,
  impact,
  isSubmitting,
  isPreviewing,
  error,
  onCancel,
  onPreview,
  onSubmit
}: {
  title: string;
  canManageTiers: boolean;
  capabilities: DeploymentTierCapability[];
  initialValues: TierFormValues;
  impact: DeploymentTierImpactSummary | null;
  isSubmitting: boolean;
  isPreviewing: boolean;
  error?: string;
  onCancel?: () => void;
  onPreview?: (values: TierFormValues) => void;
  onSubmit: (values: TierFormValues) => void;
}) {
  const [values, setValues] = useState<TierFormValues>(initialValues);
  const groupedCapabilities = useMemo(() => {
    const grouped = new Map<string, DeploymentTierCapability[]>();
    for (const capability of capabilities) {
      const items = grouped.get(capability.category) ?? [];
      items.push(capability);
      grouped.set(capability.category, items);
    }
    return grouped;
  }, [capabilities]);
  const requiresImpactPreview = Boolean(onPreview);
  const canSubmit = canManageTiers && values.name.trim().length > 0 && (!requiresImpactPreview || Boolean(impact));

  function toggleCapability(capabilityId: string) {
    setValues((current) => ({
      ...current,
      capabilities: current.capabilities.includes(capabilityId)
        ? current.capabilities.filter((item) => item !== capabilityId)
        : [...current.capabilities, capabilityId].sort()
    }));
  }

  return (
    <form
      className="rounded-ui border border-border bg-surface p-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (canSubmit) onSubmit({ ...values, name: values.name.trim(), description: values.description.trim() });
      }}
    >
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-sm font-semibold">{title}</h2>
        {onCancel ? <SecondaryButton type="button" className="h-8" onClick={onCancel}>Cancel</SecondaryButton> : null}
      </div>
      <div className="mt-3 grid gap-3">
        <label className="text-sm font-medium">
          Name
          <Input className="mt-1" value={values.name} onChange={(event) => setValues((current) => ({ ...current, name: event.target.value }))} />
        </label>
        <label className="text-sm font-medium">
          Description
          <Input className="mt-1" value={values.description} onChange={(event) => setValues((current) => ({ ...current, description: event.target.value }))} />
        </label>
        <label className="text-sm font-medium">
          Sort order
          <Input className="mt-1" type="number" value={values.sortOrder} onChange={(event) => setValues((current) => ({ ...current, sortOrder: Number(event.target.value) }))} />
        </label>
      </div>
      <div className="mt-4 space-y-3">
        {Array.from(groupedCapabilities.entries()).map(([category, items]) => (
          <fieldset key={category} className="space-y-2">
            <legend className="text-xs font-semibold uppercase text-muted-foreground">{category}</legend>
            {items.map((capability) => (
              <label key={capability.id} className={cn("flex gap-2 rounded-ui border border-border p-2 text-sm", capability.isDeprecated ? "opacity-60" : "")}>
                <input
                  type="checkbox"
                  checked={values.capabilities.includes(capability.id)}
                  disabled={capability.isDeprecated || !canManageTiers}
                  onChange={() => toggleCapability(capability.id)}
                />
                <span>
                  <span className="font-medium">{capability.label}</span>
                  <span className="block text-xs text-muted-foreground">{capability.description}</span>
                </span>
              </label>
            ))}
          </fieldset>
        ))}
      </div>
      {impact ? (
        <div className="mt-4 rounded-ui border border-border bg-background p-3 text-sm">
          <div className="font-medium">{impact.affectedEnvironmentCount} environments affected</div>
          {impact.changedSafeguards.length > 0 ? (
            <ul className="mt-2 list-disc space-y-1 pl-5 text-muted-foreground">
              {impact.changedSafeguards.map((change) => <li key={change}>{change}</li>)}
            </ul>
          ) : null}
        </div>
      ) : null}
      {error ? <p className="mt-3 text-sm text-destructive">{error}</p> : null}
      {!canManageTiers ? <p className="mt-3 text-sm text-muted-foreground">Workspace owner access is required to manage tiers.</p> : null}
      <div className="mt-4 flex gap-2">
        {onPreview ? (
          <SecondaryButton type="button" disabled={!canManageTiers || values.name.trim().length === 0 || isPreviewing} onClick={() => onPreview(values)}>
            Preview impact
          </SecondaryButton>
        ) : null}
        <Button type="submit" disabled={!canSubmit || isSubmitting}>
          <Save className="h-4 w-4" />
          Save
        </Button>
      </div>
    </form>
  );
}

function capabilityLabel(capabilities: DeploymentTierCapability[], capabilityId: string) {
  return capabilities.find((capability) => capability.id === capabilityId)?.label ?? capabilityId;
}
