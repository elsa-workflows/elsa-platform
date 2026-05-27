# Contract: Console Deployment Tiers UX

## Entry Points

- Deployment cockpit continues to show applications, environments, engines, revisions, history, drift, and observability.
- A workspace admin can open deployment tier management from the Deployments area.
- Users with deployment setup permission select an active tier when creating or editing an environment.

## Tier Management View

Visible to workspace admins:

- Tier list sorted by `sortOrder`, then name.
- Name, description, active or archived status, environment count, and assigned capability labels.
- Create tier action.
- Edit tier action.
- Archive and restore actions.
- Impact preview when capability changes affect existing environments.

Read-only or blocked state for non-admin users:

- Non-admin users may see tier labels where needed for deployment context.
- Tier create, edit, archive, restore, and capability selection controls are unavailable.
- Blocked actions explain that workspace administration authority is required.

## Create/Edit Tier Form

Fields:

- Name: required.
- Description: optional.
- Sort order: required numeric ordering value.
- Capabilities: multi-select from the platform-defined capability catalog.

Validation:

- Duplicate active names are rejected.
- Unknown capabilities are not selectable.
- Deprecated capabilities are shown only when already assigned.
- Saving capability changes for a tier used by environments requires impact preview acknowledgement.

## Impact Preview

When capability changes affect assigned environments, the console shows:

- Affected environment count.
- A bounded sample of affected applications and environments.
- Added capabilities.
- Removed capabilities.
- Changed deployment safeguards, such as confirmation, rollback, promotion eligibility, secret verification, or observability expectations.

The admin must acknowledge the preview before saving.

## Environment Setup And Editing

Environment create and edit flows:

- Load active workspace tiers.
- Require exactly one active tier selection.
- Show tier name and selected capability labels near the selector.
- Hide archived tiers from new assignments.
- Display archived tier labels for existing environments that still reference them, with a prompt to reassign before saving.

Empty tier configuration:

- The console relies on server-provided default tiers.
- If tier loading fails, environment creation is disabled and shows a retryable error.

## Cockpit Display

Environment summaries show:

- Environment name.
- Tier label.
- Tier status when archived.
- Existing deployment health, deployment status, drift status, revision, and engine information.

Tier-aware warnings:

- Production-like tiers use production-grade warning copy regardless of tier name.
- Tiers lacking promotion-target capability cannot be selected as promotion targets without a blocking validation message.
- Confirmation and rollback messaging is based on coded capabilities.

## Expected Test Coverage

- Admin can create a custom tier with capabilities.
- Admin can edit tier label, sort order, and capabilities.
- Duplicate tier names show a validation error.
- Non-admin cannot mutate tiers.
- Capability change on used tier shows impact preview.
- Archived tiers are hidden from new environment selection.
- Existing archived-tier environments remain readable.
- Environment create/edit sends tier identity, not fixed tier enum values.
