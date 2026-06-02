export type AccountContext = {
  id: string;
  displayName: string | null;
  email: string | null;
};

export type OrganizationRole = "Reader" | "Member" | "WorkspaceCreator" | "BillingAdmin" | "Administrator" | "Owner" | string;

export type WorkspaceRole = "Reader" | "SourceAdmin" | "Owner" | string;

export type WorkspaceKind = "Personal" | "Shared" | "Organization" | string;

export type OrganizationContext = {
  id: string;
  name: string;
  role: OrganizationRole;
};

export type WorkspaceContext = {
  id: string;
  name: string;
  kind: WorkspaceKind;
  role: WorkspaceRole;
  organizationId: string;
  organizationName: string;
  organizationRole: OrganizationRole;
};

export type OrganizationWorkspaceContextResponse = {
  account: AccountContext;
  organizations: OrganizationContext[];
  workspaces: WorkspaceContext[];
};
