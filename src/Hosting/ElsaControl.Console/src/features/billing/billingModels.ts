export type OrganizationBillingState =
  | "Trial"
  | "Active"
  | "PastDue"
  | "Constrained"
  | "Suspended"
  | "Retained"
  | "Deleted"
  | string;

export type OrganizationBillingStatus = {
  organizationId: string;
  subscription: OrganizationBillingSubscription | null;
  entitlements: OrganizationBillingEntitlements | null;
  capacity: OrganizationBillingCapacity;
  capabilities: string[];
};

export type OrganizationBillingSubscription = {
  state: OrganizationBillingState;
  trialStartedAt: string;
  trialEndsAt: string;
  activatedAt: string | null;
  pastDueAt: string | null;
  constrainedAt: string | null;
  suspendedAt: string | null;
  retainedAt: string | null;
  deletedAt: string | null;
  updatedAt: string;
};

export type OrganizationBillingEntitlements = {
  canCreateCustomSources: boolean;
  maxSources: number;
  maxWorkspaces: number;
  maxInstances: number;
  maxPackagesIndexed: number | null;
  maxVersionsPerPackage: number | null;
  maxSyncsPerDay: number | null;
  privateFeedsEnabled: boolean;
  managedHostingEnabled: boolean;
  deploymentTargetsEnabled: boolean;
  syncedAt: string;
};

export type OrganizationBillingCapacity = {
  managedInstancesUsed: number;
  managedInstancesLimit: number | null;
  workspacesUsed: number;
  workspacesLimit: number | null;
};

export type OrganizationBillingSession = {
  url: string;
};

type BillingStateMeta = {
  label: string;
  title: string;
  description: string;
  tone: "active" | "warning" | "danger" | "neutral";
};

export const billingStateMeta: Record<string, BillingStateMeta> = {
  Trial: {
    label: "Trial",
    title: "Your Elsa organization is ready to grow",
    description: "The organization is using its included trial window. Add billing before the trial deadline to keep commercial access uninterrupted.",
    tone: "active"
  },
  Active: {
    label: "Active",
    title: "Billing is active",
    description: "Commercial access is in good standing. Manage payment details or invoices when you need to.",
    tone: "active"
  },
  PastDue: {
    label: "Past due",
    title: "Payment attention is required",
    description: "Elsa has received a billing signal that needs attention. Resolve it before access becomes constrained.",
    tone: "warning"
  },
  Constrained: {
    label: "Constrained",
    title: "Some organization actions are paused",
    description: "Existing service data is retained, but new provider-backed work may be held until billing is resolved.",
    tone: "warning"
  },
  Suspended: {
    label: "Suspended",
    title: "Service access is suspended",
    description: "The organization remains visible for recovery while billing access is restored.",
    tone: "danger"
  },
  Retained: {
    label: "Retained",
    title: "Your data is retained",
    description: "Elsa is preserving the organization while billing is resolved. Contact your administrator before the retention deadline.",
    tone: "warning"
  },
  Deleted: {
    label: "Closed",
    title: "The commercial record is closed",
    description: "This organization no longer has an active billing lifecycle.",
    tone: "neutral"
  }
};

export function getBillingStateMeta(state: OrganizationBillingState | null | undefined) {
  if (!state) {
    return {
      label: "Not started",
      title: "Billing has not been initialized",
      description: "Start checkout when you are ready to enable commercial access for this organization.",
      tone: "neutral" as const
    };
  }

  return billingStateMeta[state as keyof typeof billingStateMeta] ?? {
    label: "Needs review",
    title: "Billing status needs review",
    description: "Elsa Control has received a lifecycle state it cannot present yet. Contact an administrator before taking action.",
    tone: "neutral" as const
  };
}
