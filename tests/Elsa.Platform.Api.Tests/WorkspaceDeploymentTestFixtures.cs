using Elsa.Platform.Api.Authentication;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.Api.Tests;

internal static class WorkspaceDeploymentTestFixtures
{
    public const string DefaultIssuer = "https://elsaworkflows.io";
    public const string DefaultSubject = "deployment-owner";
    public const string OtherSubject = "deployment-other";

    public static HttpClient CreateTrustedWorkspaceClient(this PlatformApiTestApplication app, string subject = DefaultSubject)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.IssuerHeader, DefaultIssuer);
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.EmailHeader, $"{subject}@example.test");
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.NameHeader, subject);
        return client;
    }

    public static async Task<Guid> GetDefaultWorkspaceIdAsync(this HttpClient client, CancellationToken cancellationToken = default)
    {
        var response = await client.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces", cancellationToken);
        return response!.Workspaces.Single().Id;
    }

    public static async Task<Guid> AddWorkspaceMemberAsync(this PlatformApiTestApplication app, Guid workspaceId, string subject, WorkspaceRole role)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var account = new Account { DisplayName = subject, Email = $"{subject}@example.test" };
        account.ExternalIdentities.Add(new ExternalIdentity
        {
            Account = account,
            Issuer = DefaultIssuer,
            Subject = subject,
            DisplayName = subject,
            Email = $"{subject}@example.test"
        });
        var workspace = await db.Workspaces.Include(x => x.Organization).SingleAsync(x => x.Id == workspaceId);
        account.OrganizationMemberships.Add(new OrganizationMembership
        {
            Account = account,
            OrganizationId = workspace.OrganizationId,
            Role = OrganizationRole.Member
        });
        account.Memberships.Add(new WorkspaceMembership { Account = account, Workspace = workspace, Role = role });
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    public static async Task GrantWorkspaceDeploymentPermissionAsync(this PlatformApiTestApplication app, Guid workspaceId, Guid accountId, string permission)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspacePermissionStore>();
        await store.GrantPermissionAsync(workspaceId, new GrantWorkspacePermissionRequest(accountId, permission, null));
    }

    public static WorkspaceArtifactRegistrationRequest ArtifactRegistration(
        string artifactId = "sha256:claims-prod",
        string reference = "/tmp/claims-prod",
        string referenceProvider = "local") =>
        new(
            artifactId,
            ArtifactLayoutConstants.LayoutVersion,
            new WorkspaceArtifactDigest("sha256", "claims-prod"),
            WorkspaceArtifactFormat.Folder,
            referenceProvider,
            reference,
            new WorkspaceArtifactManifestSummary("claims", "1.0.0", "prod"),
            [
                new WorkspaceArtifactResourceSummary(
                    "workflowDefinition",
                    "payment-retry",
                    null,
                    "8",
                    new WorkspaceArtifactDigest("sha256", "workflow-hash"))
            ],
            []);

    public static WorkspaceArtifactRegistrationRequest WorkflowEnvelopeRegistration(
        string artifactId = "sha256:claims-prod",
        string reference = "/tmp/claims-prod") =>
        ArtifactRegistration(artifactId, reference) with
        {
            EnvelopeVersion = ArtifactEnvelopeConstants.EnvelopeVersion,
            ArtifactTypeId = ArtifactTypeIds.ElsaWorkflowDefinition,
            ArtifactSchemaVersion = ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion,
            PayloadReference = new ArtifactPayloadReference("producer-managed", $"studio://workflows/{artifactId}"),
            Producer = new ArtifactProducer("studio", "Elsa Studio", "4.0.0", $"workflow:{artifactId}"),
            DisplayMetadata = new ArtifactDisplayMetadata(
                "claims",
                "1.0.0",
                "Claims workflow",
                new Dictionary<string, string> { ["domain"] = "claims" },
                new Dictionary<string, string>()),
            CompatibilityHints =
            [
                new ArtifactCompatibilityHint(
                    ArtifactTypeIds.ElsaWorkflowDefinition,
                    "elsa-workflows",
                    ">=4.0.0",
                    ["workflow-definition.apply"],
                    new Dictionary<string, string>())
            ]
        };
}
