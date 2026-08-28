# Weaver Configuration

Weaver is the Elsa Control workspace assistant. It runs from the API backend and uses a configured agent runtime to inspect authorized workspace state, draft operational plans, and execute only approved platform actions.

Weaver is disabled by default.

## Basic Settings

```json
{
  "Weaver": {
    "Enabled": false,
    "ProviderMode": "Disabled",
    "Model": "gpt-5",
    "ReasoningEffort": "medium"
  }
}
```

`Enabled` controls whether users can start sessions. `ProviderMode` selects how the runtime reaches a model provider.

Supported provider modes:

- `Disabled`: Weaver cannot start sessions.
- `Fake`: deterministic local runtime for development and tests; no external model is used.
- `GitHubCopilot`: use GitHub Copilot SDK authentication.
- `BringYourOwnKey`: use provider API keys managed by the platform deployment.

## Local Fake Runtime

Use this mode for local UI/backend testing without model credentials:

```json
{
  "Weaver": {
    "Enabled": true,
    "ProviderMode": "Fake",
    "Model": "fake"
  }
}
```

## BYOK Runtime

BYOK uses API keys from your chosen model provider. Store the key in an environment variable or secret provider available to the API host.

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
    }
  }
}
```

```bash
export WEAVER_OPENAI_API_KEY="<provider-api-key>"
```

Provider API keys must never be committed, logged, stored in Weaver session records, or returned to the console.

## GitHub Copilot Runtime

Use GitHub Copilot-backed mode when the API host has a valid GitHub token or authenticated Copilot runtime.
The API uses the GitHub Copilot SDK in empty-mode hosting, which means only explicitly registered Elsa Control tools are available to the agent; built-in shell, filesystem, git, and edit tools are not exposed.

```json
{
  "Weaver": {
    "Enabled": true,
    "ProviderMode": "GitHubCopilot",
    "Model": "gpt-5",
    "Provider": {
      "GitHubTokenEnvironmentVariable": "COPILOT_GITHUB_TOKEN"
    }
  }
}
```

## Runtime Limits

`Runtime:CopilotHome` controls where the SDK stores runtime state and resumable session files. If omitted, Weaver uses `.weaver/copilot` relative to the API process working directory.

```json
{
  "Weaver": {
    "Runtime": {
      "CopilotHome": ".weaver/copilot",
      "TurnTimeoutSeconds": 120,
      "MaxConcurrentSessions": 4,
      "ToolResultMaxBytes": 20000
    }
  }
}
```

Use bounded timeouts and concurrency in hosted environments. Large tool results should be summarized before reaching the model.

## Telemetry

```json
{
  "Weaver": {
    "Telemetry": {
      "Enabled": true,
      "OtlpEndpoint": "http://localhost:4318"
    }
  }
}
```

Telemetry should include trace identifiers and timings, not raw secrets or full sensitive prompt/tool payloads.

## Security Notes

- Weaver must call typed platform tools, not arbitrary shell or filesystem tools, in hosted production.
- Workspace content and browser page text are treated as untrusted evidence.
- All mutating actions require explicit platform approval.
- Raw secrets, tokens, connection strings, provider API keys, and raw artifact payloads are redacted or omitted.

## Production Checklist

Use `BringYourOwnKey` for hosted deployments unless the host explicitly manages GitHub Copilot authentication. Keep provider keys in the deployment secret store and expose only the environment variable name through configuration.

Recommended hosted defaults:

```json
{
  "Weaver": {
    "Enabled": true,
    "ProviderMode": "BringYourOwnKey",
    "Model": "gpt-5",
    "ReasoningEffort": "medium",
    "Provider": {
      "Type": "openai",
      "ApiKeyEnvironmentVariable": "WEAVER_OPENAI_API_KEY"
    },
    "Runtime": {
      "TurnTimeoutSeconds": 120,
      "MaxConcurrentSessions": 4,
      "ToolResultMaxBytes": 20000
    },
    "Telemetry": {
      "Enabled": true
    }
  }
}
```

Before enabling for customers:

- Confirm the API process can read the configured provider key environment variable.
- Confirm logs do not include prompt text, provider keys, connection strings, or raw tool payloads.
- Keep `Fake` mode limited to local development and automated tests.
- Keep generic shell, filesystem, and edit tools disabled in hosted sessions.
- Validate approval and execute endpoints through workspace permissions before exposing `Operate` mode broadly.

## Troubleshooting

`Weaver is disabled.`: Set `Weaver:Enabled` to `true` and choose a provider mode other than `Disabled`.

`Weaver provider mode is disabled.`: Set `Weaver:ProviderMode` to `Fake`, `GitHubCopilot`, or `BringYourOwnKey`.

`Weaver BYOK provider requires an API key environment variable.`: Set `Weaver:Provider:ApiKeyEnvironmentVariable` to the name of the environment variable containing the provider key.

Provider authentication failures: Verify the key or GitHub token exists in the API host environment, has the expected provider access, and is not scoped to a local shell that the service process cannot see.

No response in the drawer: Check the API logs for the `WeaverConfigurationHostedService` startup message, verify the selected workspace is accessible to the current user, and inspect `/api/workspaces/{workspaceId}/weaver/configuration`.

Unexpected secret text in a response: Disable Weaver immediately with `Weaver:Enabled=false`, preserve the session ID for audit, and add or tighten a redaction rule before re-enabling.
