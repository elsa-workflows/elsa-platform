# Quickstart: Weaver Copilot Agent

## Local Configuration

Weaver is disabled by default until configured.

Example local BYOK configuration:

```json
{
  "Weaver": {
    "Enabled": true,
    "ProviderMode": "BringYourOwnKey",
    "Model": "gpt-5",
    "ReasoningEffort": "medium",
    "Provider": {
      "Type": "openai",
      "BaseUrl": "https://api.openai.com/v1",
      "ApiKeyEnvironmentVariable": "WEAVER_OPENAI_API_KEY"
    },
    "Runtime": {
      "CopilotHome": ".weaver/copilot",
      "TurnTimeoutSeconds": 120,
      "MaxConcurrentSessions": 4,
      "ToolResultMaxBytes": 20000
    },
    "Telemetry": {
      "Enabled": false,
      "OtlpEndpoint": null
    }
  }
}
```

Environment variable:

```bash
export WEAVER_OPENAI_API_KEY="<provider-api-key>"
```

Example GitHub Copilot-backed configuration:

```json
{
  "Weaver": {
    "Enabled": true,
    "ProviderMode": "GitHubCopilot",
    "Model": "gpt-5",
    "GitHubTokenEnvironmentVariable": "COPILOT_GITHUB_TOKEN"
  }
}
```

## Validation Scenarios

1. Start the API and console with Weaver disabled. Open the drawer and verify it reports Weaver is unavailable without starting an agent session.
2. Enable Weaver with fake/test provider mode. Open the drawer from a deployment environment page and ask "What is wrong here?" Verify the response uses the current route/workspace context.
3. Attempt to ask for raw secrets. Verify Weaver refuses and no secret value appears in transcript or tool logs.
4. Ask Weaver to draft a promotion plan. Verify a plan card appears with target, impact, validation, blockers, approval boundary, and rollback guidance.
5. Attempt to execute the plan without approval or permission. Verify execution is blocked.
6. Approve with a permitted account and execute. Verify existing deployment APIs are called and audit/session records are updated.

## Test Commands

```bash
dotnet test tests/ValenceControl.Api.Tests/ValenceControl.Api.Tests.csproj --no-restore
dotnet test tests/ValenceControl.Weaver.Core.Tests/ValenceControl.Weaver.Core.Tests.csproj --no-restore
npm run typecheck --prefix src/ValenceControl.Console
npm test --prefix src/ValenceControl.Console -- Weaver
git diff --check
```
