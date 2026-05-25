import { useState } from "react";
import { Button, Input, Select } from "@/components/ui";
import type { EnvironmentSummary, RegisterDeploymentEngineRequest } from "@/features/deployments/deploymentModels";

export type DeploymentSetupValues = {
  applicationName: string;
  environmentName: string;
  environmentTier: EnvironmentSummary["tier"];
  engineName: string;
  baseUrl: string;
  credentialReference: string;
};

export function DeploymentSetupPanel({
  canManageSetup,
  isSubmitting,
  error,
  onSubmit
}: {
  canManageSetup: boolean;
  isSubmitting: boolean;
  error?: string;
  onSubmit: (values: DeploymentSetupValues) => void;
}) {
  const [values, setValues] = useState<DeploymentSetupValues>({
    applicationName: "",
    environmentName: "Prod",
    environmentTier: "Production",
    engineName: "",
    baseUrl: "",
    credentialReference: ""
  });
  const canSubmit =
    canManageSetup &&
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
          <Select className="mt-1 w-full" value={values.environmentTier} onChange={(event) => setValues((current) => ({ ...current, environmentTier: event.target.value as EnvironmentSummary["tier"] }))}>
            <option value="Dev">Dev</option>
            <option value="Test">Test</option>
            <option value="Stage">Stage</option>
            <option value="Production">Production</option>
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
      <div className="mt-4">
        <Button type="submit" disabled={!canSubmit || isSubmitting}>
          Create setup
        </Button>
      </div>
    </form>
  );
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
