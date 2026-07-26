# Console UX Contract: Engine Credential Management

## Route and navigation

- Route: `/admin/deployments/credentials`
- Navigation group: Deployments
- Navigation label: `Engine credentials`
- Page title: `Engine credentials`
- Page description: Explains these credentials are for platform-to-engine communication and runtime secrets remain managed in runtimes.

## Default page state

The page loads data for the selected workspace:
- Deployment permissions
- Engine credential stores
- Credential references

The default view shows active stores/references. Archived records are inspectable through an explicit status filter or section.

## Empty states

When no stores exist:
- Show a concise empty state that explains engine credential stores.
- Show `Register store` action only when the user can manage setup.

When stores exist but references do not:
- Show store list and a reference empty state.
- Show `Register reference` action only when the user can manage setup.

When the user cannot manage setup:
- Show safe metadata and explanatory read-only copy.
- Hide or disable create, edit, rotate, and archive submission controls.

## Store management

Users with deployment setup permission can:
- Register a store with name and supported store type.
- Update safe store metadata where existing API permits.
- Archive a store after confirmation.

The UI must show:
- Store name
- Store type
- Provider label
- Status
- Active reference count

## Reference management

Users with deployment setup permission can:
- Register a reference under an active store.
- Update safe reference metadata where existing API permits.
- Rotate local encrypted references through a write-only credential input.
- Archive a reference after confirmation.

The UI must show:
- Reference name
- Store
- Store type
- Safe reference locator or `Protected local credential`
- Verification status
- Last verified timestamp when available
- Usage count
- Status

The UI must not show raw local encrypted credential values after submission.

## Usage disclosure

When a reference usage count is greater than zero:
- The count is interactive.
- Expanding usage loads affected engines.
- Each usage row shows application, environment, and engine names.

Before archive or rotation:
- If usage exists, show affected engines before submit.
- If no usage exists, state that no active engines currently use the reference.

## Engine setup integration

When engine registration/editing has no active credential references:
- Provide a route to `/admin/deployments/credentials`.

When a reference is created on the management page:
- Engine registration/editing in the same workspace can select it after query refresh/invalidation.

## Safety copy

Every creation and management area must avoid generic "secrets" wording that could imply runtime secret management. Prefer:
- `Engine credential store`
- `Credential reference`
- `Valence Control-to-engine credentials`
- `Runtime secrets remain managed inside runtimes`
