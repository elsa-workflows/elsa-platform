# Contract: Application Component Manifest v1

The manifest is a canonical build artifact that binds a resolved .NET application component graph to one source/build revision. It is separate from `elsa-package.json`.

## Canonical document

```json
{
  "schemaVersion": "1.0",
  "application": {
    "name": "Acme.WorkflowHost",
    "version": "2.4.1",
    "targetFramework": "net10.0",
    "runtimeIdentifier": "linux-x64"
  },
  "revision": {
    "sourceRevision": "40-character commit SHA",
    "repositoryUrl": "https://github.com/acme/workflow-host",
    "buildId": "optional-ci-build-id",
    "createdAt": "2026-07-16T00:00:00Z"
  },
  "components": [
    {
      "key": "nuget:Acme.Workflows:2.4.1",
      "kind": "package",
      "name": "Acme.Workflows",
      "version": "2.4.1",
      "contentHash": "sha256:...",
      "repositoryUrl": "https://github.com/acme/workflows",
      "repositoryCommit": "optional-source-commit",
      "directDependency": true,
      "assemblies": [
        {
          "name": "Acme.Workflows",
          "version": "2.4.1.0",
          "publicKeyToken": null,
          "relativePath": "lib/net10.0/Acme.Workflows.dll",
          "contentHash": "sha256:..."
        }
      ],
      "dependencies": ["nuget:Elsa:4.0.0"]
    }
  ],
  "manifestDigest": "sha256:..."
}
```

## Generation rules

- Read resolved dependency data from build artifacts such as `project.assets.json` and MSBuild items.
- Inspect managed assembly metadata without loading or executing assemblies.
- Normalize repository URLs and relative paths, but preserve the original safe value for display where useful.
- Compute SHA-256 over every listed application/package/assembly artifact.
- Sort object properties and arrays by contract-defined ordinal keys before digesting.
- Exclude absolute local paths, user names, credentials, feed authentication, environment variables, source contents, and package cache locations.
- Fail generation when the source revision is required but unavailable, a listed artifact cannot be hashed, duplicate component keys disagree, or a path escapes the build root.

## Upload contract

`POST /api/workspaces/{workspaceId}/healing/applications/{applicationId}/revisions/{revisionId}/component-manifests`

Headers:

- `Idempotency-Key`: required
- `Content-Digest`: required SHA-256 of canonical body

Behavior:

- The application and revision must belong to the selected workspace.
- Re-upload of the same digest is idempotent.
- A different digest for an already verified immutable revision is rejected rather than overwritten.
- Repository metadata is stored as advisory source information only.
- The response includes manifest ID, digest, trust state, validation findings, and whether full repair/auto-merge attribution is possible.

## Trust

V1 trust methods:

- Valence Control-managed build/release attestation.
- Authorized external delivery identity bound to application, revision, digest, and build run.
- Explicit workspace-owner verification for non-automatic use.

Only a trusted revision-bound manifest can authorize fully automated repair or auto-merge. Revocation immediately blocks new repair dispatch and merge, but preserves historical evidence.

## Compatibility

- Unknown fields are preserved when safe and ignored by older minor-version consumers.
- Unknown component kinds remain visible but cannot independently authorize repair.
- Breaking field or canonicalization changes require a new major schema version.
