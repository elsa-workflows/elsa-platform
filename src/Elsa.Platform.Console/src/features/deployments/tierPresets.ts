import { createDeploymentTier } from "@/features/deployments/deploymentApi";
import { deploymentTierCapabilities } from "@/features/deployments/deploymentModels";
import type { EnvironmentSummary, WorkspaceDeploymentTier } from "@/features/deployments/deploymentModels";

const {
  promotionSource,
  promotionTarget,
  confirmationRequired,
  rollbackEnabled,
  productionLike,
  observabilityRequired
} = deploymentTierCapabilities;

export type DefaultTierPreset = {
  name: string;
  description: string;
  /** Legacy tier classification used by the environment defaults. */
  legacyTier: EnvironmentSummary["tier"];
  sortOrder: number;
  capabilities: string[];
};

/**
 * Sensible Dev/Test/Prod tier defaults used when a workspace has no active tiers yet. Capability
 * assignments mirror a conventional promotion ladder: Dev is a promotion source, Test sits in the
 * middle, and Prod is a confirmation-gated, rollback-capable, observability-required target.
 */
export const defaultTierPresets: DefaultTierPreset[] = [
  {
    name: "Dev",
    description: "Development environments. Fast iteration, promotes upward.",
    legacyTier: "Dev",
    sortOrder: 10,
    capabilities: [promotionSource]
  },
  {
    name: "Test",
    description: "Integration and test environments. Promotes from Dev and up to Production.",
    legacyTier: "Test",
    sortOrder: 20,
    capabilities: [promotionSource, promotionTarget, confirmationRequired]
  },
  {
    name: "Production",
    description: "Production environments. Confirmation-gated, rollback-capable, observability required.",
    legacyTier: "Production",
    sortOrder: 30,
    capabilities: [promotionTarget, confirmationRequired, rollbackEnabled, productionLike, observabilityRequired]
  }
];

/**
 * Creates the default Dev/Test/Prod tiers for a workspace via the existing tier-creation endpoint,
 * one request at a time so a partial failure surfaces clearly. Returns the created tiers in order.
 */
export async function seedDefaultTiers(workspaceId: string): Promise<WorkspaceDeploymentTier[]> {
  const created: WorkspaceDeploymentTier[] = [];
  for (const preset of defaultTierPresets) {
    const tier = await createDeploymentTier(workspaceId, {
      name: preset.name,
      description: preset.description,
      sortOrder: preset.sortOrder,
      capabilities: preset.capabilities
    });
    created.push(tier);
  }
  return created;
}
