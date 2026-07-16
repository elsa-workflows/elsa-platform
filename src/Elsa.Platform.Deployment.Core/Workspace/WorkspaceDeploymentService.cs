using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class WorkspaceDeploymentService(IWorkspaceDeploymentStore store)
{
    public Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        store.GetCockpitAsync(workspaceId, cancellationToken);

    public Task<WorkspaceDeploymentApplication> CreateApplicationAsync(
        Guid workspaceId,
        CreateWorkflowApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        return store.CreateApplicationAsync(workspaceId, request, cancellationToken);
    }

    public Task<WorkspaceDeploymentApplication> UpdateApplicationAsync(
        Guid workspaceId,
        Guid applicationId,
        UpdateWorkflowApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        return store.UpdateApplicationAsync(workspaceId, applicationId, request, cancellationToken);
    }

    public Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(
        Guid workspaceId,
        CreateDeploymentEnvironmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        return store.CreateEnvironmentAsync(workspaceId, request, cancellationToken);
    }

    public Task<WorkspaceDeploymentEnvironment> UpdateEnvironmentAsync(
        Guid workspaceId,
        Guid environmentId,
        UpdateDeploymentEnvironmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        return store.UpdateEnvironmentAsync(workspaceId, environmentId, request, cancellationToken);
    }

    public Task<WorkspaceWorkflowEngine> RegisterEngineAsync(
        Guid workspaceId,
        RegisterWorkflowEngineRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BaseUrl);
        if (request.CredentialAssignmentStatus == EngineCredentialAssignmentStatus.Assigned && !request.CredentialReferenceId.HasValue)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.CredentialProvider);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.CredentialReference);
        }

        if (!Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out _))
            throw new ArgumentException("Engine base URL must be an absolute URI.", nameof(request));

        return store.RegisterEngineAsync(workspaceId, request, cancellationToken);
    }

    public Task<WorkspaceWorkflowEngine> UpdateEngineAsync(
        Guid workspaceId,
        Guid engineId,
        UpdateWorkflowEngineRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BaseUrl);
        if (request.CredentialAssignmentStatus == EngineCredentialAssignmentStatus.Assigned && !request.CredentialReferenceId.HasValue)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.CredentialProvider);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.CredentialReference);
        }

        if (!Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out _))
            throw new ArgumentException("Engine base URL must be an absolute URI.", nameof(request));

        return store.UpdateEngineAsync(workspaceId, engineId, request, cancellationToken);
    }

    public Task<IReadOnlyList<WorkspaceDeploymentSecretStore>> ListSecretStoresAsync(
        Guid workspaceId,
        bool includeArchived = false,
        CancellationToken cancellationToken = default) =>
        store.ListSecretStoresAsync(workspaceId, includeArchived, cancellationToken);

    public Task<WorkspaceDeploymentSecretStore> CreateSecretStoreAsync(
        Guid workspaceId,
        CreateDeploymentSecretStoreRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        return store.CreateSecretStoreAsync(workspaceId, Normalize(request), cancellationToken);
    }

    public Task<WorkspaceDeploymentSecretStore> UpdateSecretStoreAsync(
        Guid workspaceId,
        Guid secretStoreId,
        UpdateDeploymentSecretStoreRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        return store.UpdateSecretStoreAsync(workspaceId, secretStoreId, Normalize(request), cancellationToken);
    }

    public Task<WorkspaceDeploymentSecretStore> ArchiveSecretStoreAsync(
        Guid workspaceId,
        Guid secretStoreId,
        Guid? actorAccountId,
        CancellationToken cancellationToken = default) =>
        store.ArchiveSecretStoreAsync(workspaceId, secretStoreId, actorAccountId, cancellationToken);

    public Task<IReadOnlyList<WorkspaceDeploymentCredentialReference>> ListCredentialReferencesAsync(
        Guid workspaceId,
        Guid? secretStoreId = null,
        bool includeArchived = false,
        CancellationToken cancellationToken = default) =>
        store.ListCredentialReferencesAsync(workspaceId, secretStoreId, includeArchived, cancellationToken);

    public async Task<WorkspaceDeploymentCredentialReference> CreateCredentialReferenceAsync(
        Guid workspaceId,
        CreateDeploymentCredentialReferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reference);
        var secretStore = await GetSecretStoreAsync(workspaceId, request.SecretStoreId, cancellationToken);
        ValidateProtectedSecret(secretStore.Type, request.ProtectedSecret);
        return await store.CreateCredentialReferenceAsync(workspaceId, request, cancellationToken);
    }

    public async Task<WorkspaceDeploymentCredentialReference> UpdateCredentialReferenceAsync(
        Guid workspaceId,
        Guid credentialReferenceId,
        UpdateDeploymentCredentialReferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reference);
        var reference = await GetCredentialReferenceAsync(workspaceId, credentialReferenceId, cancellationToken);
        ValidateProtectedSecret(reference.SecretStoreType, request.ProtectedSecret);
        return await store.UpdateCredentialReferenceAsync(workspaceId, credentialReferenceId, request, cancellationToken);
    }

    public async Task<WorkspaceDeploymentCredentialReference> RotateCredentialReferenceAsync(
        Guid workspaceId,
        Guid credentialReferenceId,
        RotateDeploymentCredentialReferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProtectedSecret);
        var reference = await GetCredentialReferenceAsync(workspaceId, credentialReferenceId, cancellationToken);
        if (reference.SecretStoreType != DeploymentSecretStoreType.LocalEncryptedDatabase)
            throw new InvalidOperationException("Only local encrypted database credential references can be rotated in Elsa Platform.");

        return await store.RotateCredentialReferenceAsync(workspaceId, credentialReferenceId, request, cancellationToken);
    }

    public Task<WorkspaceDeploymentCredentialReference> ArchiveCredentialReferenceAsync(
        Guid workspaceId,
        Guid credentialReferenceId,
        Guid? actorAccountId,
        CancellationToken cancellationToken = default) =>
        store.ArchiveCredentialReferenceAsync(workspaceId, credentialReferenceId, actorAccountId, cancellationToken);

    public Task<IReadOnlyList<WorkspaceDeploymentCredentialUsage>> ListCredentialReferenceUsageAsync(
        Guid workspaceId,
        Guid credentialReferenceId,
        CancellationToken cancellationToken = default) =>
        store.ListCredentialReferenceUsageAsync(workspaceId, credentialReferenceId, cancellationToken);

    public Task<WorkspaceEngineCredentialSecret?> GetEngineCredentialSecretAsync(
        Guid workspaceId,
        Guid engineId,
        CancellationToken cancellationToken = default) =>
        store.GetEngineCredentialSecretAsync(workspaceId, engineId, cancellationToken);

    public Task<WorkspaceDeploymentCredentialSecret?> GetCredentialSecretAsync(
        Guid workspaceId,
        Guid credentialReferenceId,
        CancellationToken cancellationToken = default) =>
        store.GetCredentialSecretAsync(workspaceId, credentialReferenceId, cancellationToken);

    public Task<WorkspaceDesiredStateRevision> CreateRevisionAsync(
        Guid workspaceId,
        CreateDesiredStateRevisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Label);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DesiredStateJson);
        return store.CreateRevisionAsync(
            workspaceId,
            request with { DesiredStateJson = request.DesiredStateJson },
            cancellationToken);
    }

    public Task<IReadOnlyList<WorkspaceDesiredStateRevisionSummary>> ListApplicationRevisionsAsync(
        Guid workspaceId,
        Guid applicationId,
        CancellationToken cancellationToken = default) =>
        store.ListApplicationRevisionsAsync(workspaceId, applicationId, cancellationToken);

    public Task<WorkspaceDesiredStateRevisionDetail?> GetRevisionDetailAsync(
        Guid workspaceId,
        Guid revisionId,
        CancellationToken cancellationToken = default) =>
        store.GetRevisionDetailAsync(workspaceId, revisionId, cancellationToken);

    public static string ComputeDesiredStateHash(string desiredStateJson)
    {
        var canonicalJson = CanonicalizeJson(desiredStateJson);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CanonicalizeJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonicalJson(writer, document.RootElement);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonicalJson(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private async Task<WorkspaceDeploymentSecretStore> GetSecretStoreAsync(Guid workspaceId, Guid secretStoreId, CancellationToken cancellationToken)
    {
        var stores = await store.ListSecretStoresAsync(workspaceId, includeArchived: true, cancellationToken);
        return stores.SingleOrDefault(x => x.Id == secretStoreId)
            ?? throw new KeyNotFoundException("Deployment secret store does not exist in the workspace.");
    }

    private async Task<WorkspaceDeploymentCredentialReference> GetCredentialReferenceAsync(Guid workspaceId, Guid credentialReferenceId, CancellationToken cancellationToken)
    {
        var references = await store.ListCredentialReferencesAsync(workspaceId, secretStoreId: null, includeArchived: true, cancellationToken);
        return references.SingleOrDefault(x => x.Id == credentialReferenceId)
            ?? throw new KeyNotFoundException("Deployment credential reference does not exist in the workspace.");
    }

    private static CreateDeploymentSecretStoreRequest Normalize(CreateDeploymentSecretStoreRequest request) =>
        request with { Provider = NormalizeProvider(request.Type, request.Provider) };

    private static UpdateDeploymentSecretStoreRequest Normalize(UpdateDeploymentSecretStoreRequest request) =>
        request with { Provider = NormalizeProvider(request.Type, request.Provider) };

    private static string NormalizeProvider(DeploymentSecretStoreType type, string? provider) =>
        string.IsNullOrWhiteSpace(provider) ? DefaultProvider(type) : provider.Trim();

    private static string DefaultProvider(DeploymentSecretStoreType type) =>
        type switch
        {
            DeploymentSecretStoreType.LocalEncryptedDatabase => "Local encrypted database",
            DeploymentSecretStoreType.AzureKeyVault => "Azure Key Vault",
            DeploymentSecretStoreType.KubernetesSecrets => "Kubernetes Secrets",
            DeploymentSecretStoreType.EnvironmentVariableName => "Environment variable name",
            DeploymentSecretStoreType.GenericExternalReference => "Generic external reference",
            _ => type.ToString()
        };

    private static void ValidateProtectedSecret(DeploymentSecretStoreType type, string? protectedSecret)
    {
        if (type != DeploymentSecretStoreType.LocalEncryptedDatabase && !string.IsNullOrWhiteSpace(protectedSecret))
            throw new InvalidOperationException("External engine credential stores accept locator metadata only, not secret values.");
    }
}
