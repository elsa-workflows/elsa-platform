# Quickstart: Engine Credential Secret Stores

## Scenario 1: Create Local Engine Credential Store

1. Open the deployment setup credential surface for a workspace.
2. Create a store named `Valence Control engine credentials` with type `Local encrypted database`.
3. Add a credential reference named `Dev engine API` with a submitted credential value.
4. Verify the reference list shows safe metadata and does not reveal the submitted value.

Expected result: The store and reference are active workspace options for engine setup.

## Scenario 2: Create External Credential References

1. Create stores for Azure Key Vault, Kubernetes Secrets, environment variable name, and generic external reference.
2. Add one reference to each store using a safe locator.
3. Verify no form asks for raw secret values for these external store types.
4. Verify generic external reference help text says Valence Control cannot browse or verify it.

Expected result: All external references can be selected during engine setup as safe locators.

## Scenario 3: Register Engine With Deferred Credentials

1. Start the create application wizard.
2. Create an application and environment.
3. Skip credential setup or leave no active credential reference.
4. Register an engine with credentials deferred.
5. Open the engine detail view.

Expected result: The engine exists, shows deferred credential status, and marks credentialed platform-to-engine actions unavailable until credentials are assigned.

## Scenario 4: Assign Credentials Later

1. Create an active credential reference.
2. Open an engine that was registered with credentials deferred.
3. Assign the credential reference.
4. Verify the engine now shows the selected reference and no longer shows deferred credential status.

Expected result: The engine can use the selected reference for future platform-to-engine interactions.

## Scenario 5: Review Credential Usage Before Lifecycle Changes

1. Assign one credential reference to at least two engines.
2. Open the reference detail or archive flow.
3. Review the usage list.
4. Archive or update only after the affected engines are visible.

Expected result: Users can identify affected engines without seeing credential material.
