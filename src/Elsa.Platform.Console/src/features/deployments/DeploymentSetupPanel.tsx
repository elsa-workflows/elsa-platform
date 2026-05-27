import { useEffect, useState } from "react";
import { Button, Input, Select } from "@/components/ui";
import type { EnvironmentSummary, RegisterDeploymentEngineRequest, WorkspaceDeploymentTier } from "@/features/deployments/deploymentModels";

export type DeploymentSetupValues = {
  applicationName: string;
  environmentName: string;
  environmentTier: EnvironmentSummary["tier"];
  environmentTierId: string;
  engineName: string;
  baseUrl: string;
  credentialReference: string;
};

export function DeploymentSetupPanel({
  canManageSetup,
  tiers,
  isSubmitting,
  error,
  onSubmit
}: {
  canManageSetup: boolean;
  tiers: WorkspaceDeploymentTier[];
  isSubmitting: boolean;
  error?: string;
  onSubmit: (values: DeploymentSetupValues) => void;
}) {
  const activeTiers = tiers.filter((tier) => tier.status === "Active");
  const tierOptions = activeTiers.length > 0
    ? activeTiers.map((tier) => ({ id: tier.id, name: tier.name }))
    : ["Dev", "Test", "Stage", "Production"].map((tier) => ({ id: tier, name: tier }));
  const [values, setValues] = useState<DeploymentSetupValues>({
    applicationName: "",
    environmentName: "Prod",
    environmentTier: "Production",
    environmentTierId: "",
    engineName: "",
    baseUrl: "",
    credentialReference: ""
  });
  useEffect(() => {
    if (values.environmentTierId || tierOptions.length === 0) return;
    const preferredTier = tierOptions.find((tier) => tier.name === "Production") ?? tierOptions[0];
    setValues((current) => ({
      ...current,
      environmentTierId: preferredTier.id,
      environmentTier: legacyTierFromName(preferredTier.name)
    }));
  }, [activeTiers.length, tierOptions, values.environmentTierId]);

  const canSubmit =
    canManageSetup &&
    (values.environmentTierId.length > 0 || activeTiers.length === 0) &&
    values.applicationName.trim().length > 0 &&
    values.environmentName.trim().length > 0 &&
    values.engineName.trim().length > 0 &&
    values.baseUrl.trim().length > 0 &&
    values.credentialReference.trim().length > 0;

  return (
    <form
      className="rounded-ui border border-border bg-surface p-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (canSubmit) onSubmit(values);
      }}
    >
      <div className="grid gap-3 md:grid-cols-2">
        <label className="text-sm font-medium">
          Application
          <Input className="mt-1" value={values.applicationName} onChange={(event) => setValues((current) => ({ ...current, applicationName: event.target.value }))} />
        </label>
        <label className="text-sm font-medium">
          Environment
          <Input className="mt-1" value={values.environmentName} onChange={(event) => setValues((current) => ({ ...current, environmentName: event.target.value }))} />
        </label>
        <label className="text-sm font-medium">
          Tier
          <Select
            className="mt-1 w-full"
            value={values.environmentTierId}
            onChange={(event) => {
              const tier = activeTiers.find((item) => item.id === event.target.value);
              setValues((current) => ({
                ...current,
                environmentTierId: event.target.value,
                environmentTier: legacyTierFromName(tier?.name)
              }));
            }}
          >
            <option value="" disabled>Select a tier</option>
            {tierOptions.map((tier) => (
              <option key={tier.id} value={tier.id}>
                {tier.name}
              </option>
            ))}
          </Select>
        </label>
        <label className="text-sm font-medium">
          Engine
          <Input className="mt-1" value={values.engineName} onChange={(event) => setValues((current) => ({ ...current, engineName: event.target.value }))} />
        </label>
        <label className="text-sm font-medium">
          Base URL
          <Input className="mt-1" value={values.baseUrl} onChange={(event) => setValues((current) => ({ ...current, baseUrl: event.target.value }))} />
        </label>
        <label className="text-sm font-medium">
          Credential reference
          <Input className="mt-1" value={values.credentialReference} onChange={(event) => setValues((current) => ({ ...current, credentialReference: event.target.value }))} />
        </label>
      </div>
      {error ? <p className="mt-3 text-sm text-destructive">{error}</p> : null}
      {!canManageSetup ? <p className="mt-3 text-sm text-muted-foreground">Deployment setup permission is required.</p> : null}
      {activeTiers.length === 0 ? <p className="mt-3 text-sm text-muted-foreground">Default tiers are used until workspace tiers finish loading.</p> : null}
      <div className="mt-4">
        <Button type="submit" disabled={!canSubmit || isSubmitting}>
          Create setup
        </Button>
      </div>
    </form>
  );
}

function legacyTierFromName(name?: string): EnvironmentSummary["tier"] {
  if (name === "Dev" || name === "Test" || name === "Stage" || name === "Production") return name;
  return "Production";
}

export function setupEngineRequest(values: DeploymentSetupValues): RegisterDeploymentEngineRequest {
  return {
    name: values.engineName,
    baseUrl: values.baseUrl,
    region: null,
    credentialProvider: "External secret store",
    credentialReference: values.credentialReference,
    capabilities: [{ id: "engine.reload-configuration", label: "Reload engine configuration", boundary: "EngineApi" }],
    controls: [
      {
        id: "reload-configuration",
        label: "Reload Configuration",
        boundary: "EngineApi",
        capabilityId: "engine.reload-configuration",
        description: "Reloads engine API configuration from desired state."
      }
    ],
    hostingProvider: null
  };
}
