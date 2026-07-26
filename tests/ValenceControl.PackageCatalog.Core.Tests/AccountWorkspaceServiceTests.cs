using ValenceControl.PackageCatalog.Core.Accounts;
using ValenceControl.PackageCatalog.Core.Packages;
using FluentAssertions;

namespace ValenceControl.PackageCatalog.Core.Tests;

public sealed class AccountWorkspaceServiceTests
{
    private readonly FakeAccountWorkspaceStore _store = new();
    private readonly AccountWorkspaceService _service;

    public AccountWorkspaceServiceTests()
    {
        _service = new AccountWorkspaceService(_store);
    }

    [Fact]
    public async Task First_sign_in_provisions_owner_organization_and_personal_workspace()
    {
        var provisioner = new RecordingOwnerProvisioner();
        var service = new AccountWorkspaceService(_store, [provisioner]);

        var context = await service.GetOrCreateAsync(Identity("first-user"));

        context.Account.Id.Should().NotBeEmpty();
        context.Organizations.Should().ContainSingle(x => x.Role == OrganizationRole.Owner);
        context.Workspaces.Should().ContainSingle(x => x.Role == WorkspaceRole.Owner && x.Kind == WorkspaceKind.Personal);
        context.Workspaces.Single().OrganizationId.Should().Be(context.Organizations.Single().Id);
        _store.AddedAccounts.Should().ContainSingle();
        _store.AddedAccounts.Single().OrganizationMemberships.Should().ContainSingle(x => x.Role == OrganizationRole.Owner);
        _store.AddedAccounts.Single().Memberships.Should().ContainSingle(x => x.Role == WorkspaceRole.Owner);
        provisioner.Provisioned.Should().ContainSingle().Which.Should().Be((context.Workspaces.Single().Id, context.Account.Id));
    }

    [Fact]
    public async Task Existing_owner_reads_idempotently_reconcile_owner_provisioning()
    {
        var provisioner = new RecordingOwnerProvisioner();
        var service = new AccountWorkspaceService(_store, [provisioner]);
        await service.GetOrCreateAsync(Identity("existing-user"));

        await service.GetOrCreateAsync(Identity("existing-user"));

        provisioner.Provisioned.Should().HaveCount(2);
        provisioner.Provisioned.Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Existing_owner_retry_repairs_a_transient_provisioning_failure()
    {
        await _service.GetOrCreateAsync(Identity("retry-owner"));
        var provisioner = new FailOnceOwnerProvisioner();
        var service = new AccountWorkspaceService(_store, [provisioner]);

        var firstAttempt = () => service.GetOrCreateAsync(Identity("retry-owner"));
        await firstAttempt.Should().ThrowAsync<InvalidOperationException>();
        var context = await service.GetOrCreateAsync(Identity("retry-owner"));

        provisioner.Attempts.Should().Be(2);
        provisioner.Provisioned.Should().ContainSingle()
            .Which.Should().Be((context.Workspaces.Single().Id, context.Account.Id));
    }

    [Fact]
    public async Task Organization_membership_without_workspace_membership_does_not_authorize_workspace_access()
    {
        var organizationId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        _store.Lookup = new ExternalIdentityLookup(
            Guid.NewGuid(),
            new AccountWorkspaceContext(
                new AccountSummary(accountId, "Member", "member@example.test"),
                [])
            {
                Organizations = [new OrganizationSummary(organizationId, "Acme", OrganizationRole.Member)]
            });

        var access = await _service.GetWorkspaceAccessAsync(Identity("member"), workspaceId);

        access.Should().BeNull();
    }

    private static TrustedWorkspaceIdentity Identity(string subject) =>
        new("issuer", subject, "Member", $"{subject}@example.test");

    private sealed class RecordingOwnerProvisioner : IWorkspaceOwnerProvisioner
    {
        public List<(Guid WorkspaceId, Guid AccountId)> Provisioned { get; } = [];

        public Task ProvisionAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default)
        {
            Provisioned.Add((workspaceId, accountId));
            return Task.CompletedTask;
        }
    }

    private sealed class FailOnceOwnerProvisioner : IWorkspaceOwnerProvisioner
    {
        public int Attempts { get; private set; }
        public List<(Guid WorkspaceId, Guid AccountId)> Provisioned { get; } = [];

        public Task ProvisionAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Attempts == 1)
                throw new InvalidOperationException("Transient provisioning failure.");
            Provisioned.Add((workspaceId, accountId));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAccountWorkspaceStore : IAccountWorkspaceStore
    {
        public List<Account> AddedAccounts { get; } = [];
        public ExternalIdentityLookup? Lookup { get; set; }

        public Task<ExternalIdentityLookup?> FindByExternalIdentityAsync(string issuer, string subject, CancellationToken cancellationToken = default) =>
            Task.FromResult(Lookup);

        public Task AddAccountAsync(Account account, CancellationToken cancellationToken = default)
        {
            AddedAccounts.Add(account);
            return Task.CompletedTask;
        }

        public Task UpdateExternalIdentitySeenAsync(Guid externalIdentityId, string? displayName, string? email, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var account = AddedAccounts.Single();
            var workspaceMembership = account.Memberships.Single();
            var organizationMembership = account.OrganizationMemberships.Single();
            var workspace = workspaceMembership.Workspace!;
            var organization = organizationMembership.Organization!;
            workspace.OrganizationId = organization.Id;
            workspace.Organization = organization;

            Lookup = new ExternalIdentityLookup(
                account.ExternalIdentities.Single().Id,
                new AccountWorkspaceContext(
                    new AccountSummary(account.Id, account.DisplayName, account.Email),
                    [new WorkspaceSummary(workspace.Id, workspace.Name, workspace.Kind, workspaceMembership.Role, organization.Id, organization.Name, organizationMembership.Role)])
                {
                    Organizations = [new OrganizationSummary(organization.Id, organization.Name, organizationMembership.Role)]
                });
            return Task.CompletedTask;
        }

        public Task<bool> WorkspaceExistsAsync(Guid workspaceId, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<OrganizationEntitlementSnapshot?> GetLatestOrganizationEntitlementAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<int> ActiveWorkspaceCountAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<bool> OrganizationWorkspaceNameExistsAsync(Guid organizationId, string name, Guid? excludingWorkspaceId, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<bool> WorkspaceBelongsToOrganizationAsync(Guid organizationId, Guid workspaceId, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<bool> OrganizationAccountMembershipExistsAsync(Guid organizationId, Guid accountId, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<IReadOnlyList<WorkspaceSummary>> ListOrganizationWorkspacesAsync(Guid organizationId, Guid accountId, bool includeAllOrganizationWorkspaces, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<OrganizationWorkspaceMutationResult> CreateOrganizationWorkspaceAsync(Guid organizationId, Guid creatorAccountId, CreateOrganizationWorkspaceRequest request, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<WorkspaceSummary?> UpdateOrganizationWorkspaceAsync(Guid organizationId, Guid workspaceId, Guid accountId, UpdateOrganizationWorkspaceRequest request, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<WorkspaceSummary> SetWorkspaceMembershipAsync(Guid organizationId, Guid workspaceId, Guid accountId, WorkspaceRole role, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<bool> CanRemoveWorkspaceMembershipAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task RemoveWorkspaceMembershipAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<WorkspaceEntitlementSnapshot?> GetLatestEntitlementAsync(Guid workspaceId, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<WorkspaceEntitlementSnapshot> SaveEntitlementAsync(WorkspaceEntitlementSnapshot entitlement, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<WorkspaceSourceAddResult> TryAddWorkspaceSourceAsync(PackageSource source, int maxSources, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<IReadOnlyList<PackageSource>> ListVisibleSourcesAsync(Guid workspaceId, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<IReadOnlyDictionary<Guid, int>> GetPackageCountsAsync(IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken = default) => throw Unsupported();

        private static NotSupportedException Unsupported() => new("This fake store method is not used by these tests.");
    }
}
