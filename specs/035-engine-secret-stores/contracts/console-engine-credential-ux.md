# Contract: Console Engine Credential UX

## Create Application Wizard

The create application wizard includes credential setup before engine registration:

1. Application
2. Environments
3. Engine credentials
4. Engines
5. Review or finish

Credential setup may be skipped when the administrator wants to defer engine credentials.

## No Store Empty State

When engine registration starts with no active stores:
- Show a clear empty state instead of only disabled selects.
- Primary action: create engine credential store.
- Secondary action: continue with credentials deferred.
- Explain that these credentials are only for Elsa Platform calling the engine, not runtime secrets.

## Store Creation Form

Fields:
- Store name
- Store type
- Description

Store type help text:
- Local encrypted database: Elsa Platform stores protected engine credential material.
- Azure Key Vault: Elsa Platform stores a Key Vault locator only.
- Kubernetes Secrets: Elsa Platform stores a namespace/name/key locator only.
- Environment variable name: Elsa Platform stores an engine-host environment variable name only.
- Generic external reference: Elsa Platform stores a customer-governed reference it cannot browse or verify.

## Credential Reference Form

Fields:
- Reference name
- Store
- Locator or secret value input appropriate to store type
- Description

Rules:
- Local encrypted database shows a secret-value field for create/rotation and never shows the submitted value afterward.
- External types show locator fields and do not ask for raw secret values.
- The UI labels every reference as an engine credential reference.

## Engine Registration

Engine registration can proceed in two modes:
- Assigned credential: select an active credential reference.
- Deferred credential: register the engine without credentials and show follow-up status.

Deferred credential status:
- The engine can exist in environment setup.
- Credentialed platform-to-engine actions are marked unavailable until a credential reference is assigned.
- The engine detail page offers an assign credential action when the user has setup permission.

## Credential Usage And Lifecycle

Before archiving or changing a credential reference:
- Show affected engines by application and environment.
- Warn that platform-to-engine communication may be affected.
- Do not show raw secret values.
