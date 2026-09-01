# Disposable Azure proof host

`ElsaControl.ProofHost` is the opt-in command-line shell for the Milestone B
disposable Azure proof. Its configuration boundary accepts only bounded identifiers, immutable
digests, safe evidence locators, executable paths and `secret://` references. Secret values and
raw manifest payloads are not accepted.

The available modes are:

```text
ElsaControl.ProofHost validate [options]
ElsaControl.ProofHost run [options]
ElsaControl.ProofHost cleanup [options]
```

`validate` is offline and prints a safe configuration summary. `run` and `cleanup` are mutation
modes and require the exact opt-in environment value:

```text
DISPOSABLE_PROOF_APPLY=YES
```

The parser accepts the same named options as `DISPOSABLE_PROOF_*` environment variables; a CLI
option takes precedence over its known environment value. Unknown variables with that prefix are
rejected to catch misspellings. Run `--help` for the entry point and use `validate` before granting
the mutation gate.

The retained admission inputs must identify the immutable Elsa 3.8 Combined image, release
manifest, and verifier-retained signature evidence by absolute OCI/HTTPS reference plus canonical
SHA-256 digest. `DISPOSABLE_PROOF_SOURCE_COMMIT` binds the plan to the 40-character source commit.
The supported feature set is fixed to `DefaultAuthentication`, `Liquid`, `StructuredLogs`,
`StructuredLogsDashboard`, `ConsoleLogs`, `ConsoleLogsDashboard`, and `OpenTelemetry`; the
workflow username is fixed to `proof-admin`, and the ACR name is fixed to
`valenceruntimeimages`. The three accepted credential inputs are `secret://` locators; their generated values stay inside
short-lived in-process leases and are never written to the state database or report.

`DISPOSABLE_PROOF_STATE_PATH` is an absolute path to the durable SQLite operation store. Keep this
file until cleanup has passed. Repeat apply/no-op validation happens inside one `run`, while the
same generated credential leases are alive. After an interrupted run, another `run` fails closed;
invoke `cleanup` with the same configuration and state path instead.
Cleanup is successful only after the provider reports no owned resource references and no endpoint.
The default cleanup observation window is 20 minutes to accommodate Azure deletion convergence.

The executable composes the Azure runner only in this isolated CLI. Nothing registers the host in
the production API or worker, and `validate` performs no Azure calls. Output is limited to the safe
proof report or stable value-free error codes; raw CLI output, manifest payloads, tokens, signer
identity, and credentials are never emitted.
