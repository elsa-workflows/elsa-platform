# Quickstart: BYOC Deployment Targets

1. Register Azure Container Apps target.
2. Validate connectivity.
3. Generate preview from saved configuration version.
4. Start deployment.
5. Inspect run status.

Expected:

- Preview never applies cloud changes.
- Deployment records status and events.
- Credentials are never returned.

```bash
dotnet build Elsa.Platform.sln --no-restore
dotnet test Elsa.Platform.sln --no-build
```
