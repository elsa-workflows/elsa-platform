# Data Model: Engine Credential Management UI

## Engine Credential Management Surface

Workspace-level Console page for managing engine credential stores and references.

**Fields / state**:
- Selected workspace
- Active/archived filter
- Store list
- Credential reference list
- Selected reference usage details
- Pending lifecycle action state
- Read-only or management permission state

**Validation rules**:
- All data must be scoped to the selected workspace.
- Mutating controls require deployment setup permission.
- Raw secret values must never be populated from API responses.

## Engine Credential Store

Existing workspace-scoped store metadata used only for platform-to-engine credentials.

**Relevant fields**:
- ID
- Workspace ID
- Name
- Provider label
- Type
- Description
- Status
- Created/updated/archive metadata
- Count of related references in UI projection

**State transitions**:
- Active -> Archived through archive action.
- Archived records can be inspected but not used for new references or assignments.

**Validation rules**:
- Name is required.
- Type must be one of the supported engine credential store types.
- Store type cannot be changed in a way that would reinterpret existing references unless existing API validation allows it.

## Engine Credential Reference

Existing workspace-scoped reference under an engine credential store.

**Relevant fields**:
- ID
- Workspace ID
- Secret store ID/name/provider/type
- Name
- Reference locator
- Description
- Verification status
- Last verified timestamp
- Status
- Protected local secret presence flag
- Usage count
- Created/updated/archive metadata

**State transitions**:
- Active -> Archived through archive action.
- Local encrypted reference can receive a new protected secret value through rotation.
- External locator references can update safe locator metadata through existing update behavior.

**Validation rules**:
- Name is required.
- Active references can only be created under active stores.
- Local encrypted references accept write-only secret values during creation/rotation.
- External references accept safe locator metadata only, not raw secret values.
- Archived references are not eligible for new engine assignment.

## Credential Usage Summary

On-demand safe usage view for one credential reference.

**Fields**:
- Engine ID/name
- Application ID/name
- Environment ID/name

**Validation rules**:
- Usage data is shown only for references in the selected workspace.
- Usage disclosure must appear before archive or rotation submissions when the reference is used by engines.

## Lifecycle Confirmation

UI state for potentially disruptive actions.

**Fields**:
- Action type: archive store, archive reference, rotate local credential
- Target store or reference
- Current usage summary when applicable
- Confirmation prompt
- Submitted state and error state

**Validation rules**:
- Archive actions require confirmation.
- Rotation requires a non-empty replacement value.
- Confirmation must not include raw secret values.
