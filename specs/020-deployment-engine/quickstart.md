# Quickstart: Deployment Engine MVP

## Goal

Demonstrate the Phase 1 deployment loop using in-memory target state, handlers, and history:

```text
validated artifact/resources -> diff plan -> dry-run -> apply -> history
```

## Expected Projects

```text
src/Elsa.Platform.Deployment.Engine/
tests/Elsa.Platform.Deployment.Engine.Tests/
```

## Sample Flow

1. Create a target descriptor:

   ```csharp
   var target = new TestDeploymentTarget("staging", environment: "staging");
   ```

2. Register resource handlers:

   ```csharp
   var handler = new InMemoryResourceHandler("workflowDefinition");
   var engine = new DeploymentEngine([handler], new InMemoryDeploymentHistoryStore());
   ```

3. Provide desired resources through an artifact reader test double:

   ```csharp
   var artifact = TestArtifactReader.FromResources([
       new DeploymentResource(new("workflowDefinition", "order-approval"), desiredStateHash: hash)
   ]);
   ```

4. Validate:

   ```csharp
   var validation = await engine.ValidateAsync(artifact, target);
   ```

5. Diff:

   ```csharp
   var plan = await engine.DiffAsync(artifact, target);
   ```

6. Dry-run:

   ```csharp
   var preview = await engine.DryRunAsync(plan, target);
   ```

7. Apply:

   ```csharp
   var result = await engine.ApplyAsync(plan, target);
   ```

8. Inspect history:

   ```csharp
   var history = await store.FindAsync(result.DeploymentId);
   ```

## Verification

Run focused tests:

```bash
dotnet test tests/Elsa.Platform.Deployment.Engine.Tests/Elsa.Platform.Deployment.Engine.Tests.csproj
```

Run full solution tests before PR:

```bash
dotnet test
```

## Phase 1 Boundaries

This quickstart must not require:

- CLI commands
- HTTP APIs
- persistent database stores
- Kubernetes or OCI infrastructure
- approval/signature/policy systems
- workflow runtime state
