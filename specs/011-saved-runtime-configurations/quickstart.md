# Quickstart: Saved Runtime Configurations

## Scenario 1: Save And Reopen

Create a workspace configuration, then fetch it.

Expected: returned intent matches submitted intent.

## Scenario 2: Clone And Edit

Clone a configuration and update the clone.

Expected: original remains unchanged.

## Scenario 3: Snapshot Version

Create a version, edit the draft, and fetch the version.

Expected: version remains immutable.

## Scenario 4: Generate Bundle

Generate a bundle from a saved configuration.

Expected: response follows the builder bundle contract.

## Validation Commands

```bash
dotnet build ValenceControl.sln --no-restore
dotnet test ValenceControl.sln --no-build
```
