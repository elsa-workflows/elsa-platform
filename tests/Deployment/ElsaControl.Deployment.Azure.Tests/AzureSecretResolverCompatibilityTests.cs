using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureSecretResolverCompatibilityTests
{
    [Fact]
    public void Secret_request_retains_the_original_constructor_and_deconstruction_shape()
    {
        var resources = new AzureProviderResourceReferences(ResourceGroupName: "proof-rg");
        var request = new AzureSecretResolutionRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "assignment-1",
            "database:connectionstring",
            "secret://proof.vault.azure.net/secrets/sql-connection/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            resources)
        {
            OperationId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            AttemptNumber = 3
        };

        var (workspaceId, organizationId, instanceId, assignmentId, name, reference, deconstructedResources) = request;

        Assert.Equal(request.WorkspaceId, workspaceId);
        Assert.Equal(request.OrganizationId, organizationId);
        Assert.Equal(request.InstanceId, instanceId);
        Assert.Equal(request.ProviderAssignmentId, assignmentId);
        Assert.Equal(request.Name, name);
        Assert.Equal(request.Reference, reference);
        Assert.Equal(resources, deconstructedResources);
        Assert.Equal(3, request.AttemptNumber);
    }

    [Fact]
    public async Task Default_authorization_is_fail_closed_for_a_legacy_resolver()
    {
        IAzureSecretResolver resolver = new LegacyResolver();
        var request = new AzureSecretResolutionRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "assignment-1",
            "database:connectionstring",
            AzureManagedSecretReferences.SqlConnection);

        Assert.False(await resolver.IsAuthorizedAsync(request));
    }

    private sealed class LegacyResolver : IAzureSecretResolver
    {
        public ValueTask<AzureSecretLease> ResolveAsync(
            AzureSecretResolutionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AzureSecretLease("legacy-secret"));
    }
}
