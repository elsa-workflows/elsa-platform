# Elsa Platform Healing Client

This optional client adds the versioned Elsa Platform Healing profile to OpenTelemetry activities and submits authorized, already-redacted explicit incidents to an Elsa Platform application/environment route.

Most applications need only standard OpenTelemetry export. Use this package when application code needs the profile helpers or an explicit incident API. The client deliberately contains no repository, workflow, branch, path, evidence, or merge authority; Elsa Platform resolves those decisions from workspace-owned configuration.

See the [Platform Healing getting-started guide](https://github.com/elsa-workflows/elsa-platform/blob/main/docs/healing/getting-started.md#optional-application-client) for registration, authentication, and redaction requirements.
