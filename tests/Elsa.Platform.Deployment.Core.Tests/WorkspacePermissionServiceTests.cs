using Elsa.Platform.Deployment.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace Elsa.Platform.Deployment.Core.Tests;

public sealed class WorkspacePermissionServiceTests
{
    private readonly Guid _workspaceId = WorkspaceDeploymentTestFixtures.WorkspaceId;
    private readonly Guid _accountId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private readonly RecordingPermissionStore _store = new();

    [Fact]
    public async Task Bootstrap_owner_permissions_grants_every_deployment_permission()
    {
        var service = new WorkspacePermissionService(_store);

        var effective = await service.BootstrapOwnerPermissionsAsync(_workspaceId, _accountId);

        effective.Permissions.Should().BeEquivalentTo(WorkspaceDeploymentPermissions.All);
        _store.Grants.Should().HaveCount(WorkspaceDeploymentPermissions.All.Count);
    }

    [Fact]
    public async Task Effective_permissions_exclude_revoked_grants()
    {
        var service = new WorkspacePermissionService(_store);
        _store.Grants.Add(new WorkspacePermissionGrant(Guid.NewGuid(), _workspaceId, _accountId, WorkspaceDeploymentPermissions.Read, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null));
        _store.Grants.Add(new WorkspacePermissionGrant(Guid.NewGuid(), _workspaceId, _accountId, WorkspaceDeploymentPermissions.ManageSetup, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var effective = await service.GetEffectivePermissionsAsync(_workspaceId, _accountId);

        effective.Has(WorkspaceDeploymentPermissions.Read).Should().BeTrue();
        effective.Has(WorkspaceDeploymentPermissions.ManageSetup).Should().BeFalse();
    }

    private sealed class RecordingPermissionStore : IWorkspacePermissionStore
    {
        public List<WorkspacePermissionGrant> Grants { get; } = [];

        public Task<IReadOnlyList<WorkspacePermissionGrant>> GetPermissionGrantsAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspacePermissionGrant>>(Grants.Where(x => x.WorkspaceId == workspaceId && x.AccountId == accountId).ToList());

        public Task<WorkspacePermissionGrant> GrantPermissionAsync(Guid workspaceId, GrantWorkspacePermissionRequest request, CancellationToken cancellationToken = default)
        {
            var existing = Grants.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.AccountId == request.AccountId && x.Permission == request.Permission && x.RevokedAt is null);
            if (existing is not null)
                return Task.FromResult(existing);

            var now = DateTimeOffset.UtcNow;
            var grant = new WorkspacePermissionGrant(Guid.NewGuid(), workspaceId, request.AccountId, request.Permission, request.GrantedByAccountId, now, now, null);
            Grants.Add(grant);
            return Task.FromResult(grant);
        }
    }
}
