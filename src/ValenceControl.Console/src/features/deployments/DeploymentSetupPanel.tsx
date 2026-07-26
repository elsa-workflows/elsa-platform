import { useEffect, useId, useMemo, useState } from "react";
import { Button, Input, Select } from "@/components/ui";
import type { EnvironmentSummary, RegisterDeploymentEngineRequest, WorkspaceDeploymentTier } from "@/features/deployments/deploymentModels";

export type DeploymentSetupValues = {
  applicationName: string;
  environmentName: string;
  environmentTier: EnvironmentSummary["tier"];
  environmentTierId: string;
};

export type EngineRegistrationValues = {
  engineName: string;
  baseUrl: string;
  credentialAssignmentStatus?: "Assigned" | "Deferred";
  credentialReferenceId?: string | null;
  credentialProvider?: string | null;
  credentialReference?: string | null;
};

export type CredentialReferenceOption = {
  provider: string;
  reference: string;
  label: string;
};

export function DeploymentSetupPanel({
  fixedApplicationName,
  canManageSetup,
  tiers,
  tiersLoading = false,
  isSubmitting,
  error,
  submitLabel = "Create setup",
  onSubmit
}: {
  fixedApplicationName?: string;
  canManageSetup: boolean;
  tiers: WorkspaceDeploymentTier[];
  tiersLoading?: boolean;
  isSubmitting: boolean;
  error?: string;
  submitLabel?: string;
  onSubmit: (values: DeploymentSetupValues) => void;
}) {
  const activeTiers = useMemo(() => tiers.filter((tier) => tier.status === "Active"), [tiers]);
  const tierOptions = useMemo(() => activeTiers.map((tier) => ({ id: tier.id, name: tier.name })), [activeTiers]);
  const [values, setValues] = useState<DeploymentSetupValues>({
    applicationName: fixedApplicationName ?? "",
    environmentName: "Prod",
    environmentTier: "Production",
    environmentTierId: ""
  });
  useEffect(() => {
    if (!fixedApplicationName) return;
    setValues((current) => ({ ...current, applicationName: fixedApplicationName }));
  }, [fixedApplicationName]);

  useEffect(() => {
    const selectedTierStillExists = tierOptions.some((tier) => tier.id === values.environmentTierId);
    if (selectedTierStillExists || tierOptions.length === 0) return;
    const preferredTier = tierOptions.find((tier) => tier.name === "Production") ?? tierOptions[0];
    setValues((current) => ({
      ...current,
      environmentTierId: preferredTier.id,
      environmentTier: legacyTierFromName(preferredTier.name)
    }));
  }, [tierOptions, values.environmentTierId]);

  const canSubmit =
    canManageSetup &&
    values.environmentTierId.length > 0 &&
    values.applicationName.trim().length > 0 &&
    values.environmentName.trim().length > 0;

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
          {fixedApplicationName ? (
            <div className="mt-1 rounded-ui border border-border bg-muted/30 px-3 py-2 text-sm text-muted-foreground">{fixedApplicationName}</div>
          ) : (
            <Input className="mt-1" value={values.applicationName} onChange={(event) => setValues((current) => ({ ...current, applicationName: event.target.value }))} />
          )}
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
            disabled={!canManageSetup || isSubmitting || tierOptions.length === 0}
            onChange={(event) => {
              const tier = activeTiers.find((item) => item.id === event.target.value);
              setValues((current) => ({
                ...current,
                environmentTierId: event.target.value,
                environmentTier: legacyTierFromName(tier?.name)
              }));
            }}
          >
            <option value="" disabled>{tiersLoading ? "Loading tiers" : "Select a tier"}</option>
            {tierOptions.map((tier) => (
              <option key={tier.id} value={tier.id}>
                {tier.name}
              </option>
            ))}
          </Select>
        </label>
      </div>
      {error ? <p className="mt-3 text-sm text-destructive">{error}</p> : null}
      {!canManageSetup ? <p className="mt-3 text-sm text-muted-foreground">Deployment setup permission is required.</p> : null}
      {tiersLoading ? <p className="mt-3 text-sm text-muted-foreground">Loading workspace deployment tiers.</p> : null}
      {!tiersLoading && activeTiers.length === 0 ? <p className="mt-3 text-sm text-muted-foreground">No active deployment tiers are available for this workspace.</p> : null}
      <div className="mt-4">
        <Button type="submit" disabled={!canSubmit || isSubmitting}>
          {submitLabel}
        </Button>
      </div>
    </form>
  );
}

function legacyTierFromName(name?: string): EnvironmentSummary["tier"] {
  if (name === "Dev" || name === "Test" || name === "Stage" || name === "Production") return name;
  return "Production";
}

export function engineRegistrationRequest(values: EngineRegistrationValues): RegisterDeploymentEngineRequest {
  return {
    name: values.engineName,
    baseUrl: values.baseUrl,
    region: null,
    credentialProvider: values.credentialProvider ?? null,
    credentialReference: values.credentialReference ?? null,
    credentialReferenceId: values.credentialReferenceId ?? null,
    credentialAssignmentStatus: values.credentialAssignmentStatus ?? (values.credentialReferenceId ? "Assigned" : "Deferred"),
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

export function CredentialReferenceInput({
  className,
  value,
  options,
  onChange
}: {
  className?: string;
  value: string;
  options: CredentialReferenceOption[];
  onChange: (value: string) => void;
}) {
  const listId = useId();

  return (
    <>
      <Input
        aria-label="Credential reference"
        className={className}
        list={options.length > 0 ? listId : undefined}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
      {options.length > 0 ? (
        <datalist id={listId}>
          {options.map((option) => (
            <option key={`${option.provider}:${option.reference}`} value={option.reference}>
              {option.label}
            </option>
          ))}
        </datalist>
      ) : null}
    </>
  );
}
