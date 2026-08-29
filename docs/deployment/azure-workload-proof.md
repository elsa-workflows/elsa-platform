# Azure workload proof runbook

The disposable West Europe Elsa 3.8 Combined proof is implemented in [`infra/azure-workload-proof`](../../infra/azure-workload-proof/README.md). That README is the operational runbook and covers the two-phase secret/bootstrap boundary, immutable image input, validation, retry/idempotency, evidence, cost controls and complete cleanup.

Start with the non-mutating checks:

```bash
scripts/azure-workload-proof.sh validate
```

For a pre-existing disposable resource group, the read-only check is:

```bash
scripts/azure-workload-proof.sh what-if \
  --proof-name <unique-suffix> \
  --resource-group <disposable-resource-group> \
  --image-repository valenceruntimeimages.azurecr.io/runtime-combined \
  --image-digest <64-hex-digest> \
  --registry-resource-group <runtime-acr-resource-group> \
  --sql-bootstrap-object-id <entra-object-id> \
  --sql-bootstrap-login <entra-login>
```

Live apply is intentionally explicit and cost-bounded. It requires `DISPOSABLE_PROOF_APPLY=YES`, uses only the supplied disposable resource group, never accepts a mutable image tag, and must be followed by the cleanup command in the linked runbook.
