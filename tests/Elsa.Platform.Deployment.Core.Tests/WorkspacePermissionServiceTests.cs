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
    public async Task Bootstrap_does_not_restore_a_revoked_owner_permission()
    {
        var service = new WorkspacePermissionService(_store);
        await service.BootstrapOwnerPermissionsAsync(_workspaceId, _accountId);
        await service.RevokeAsync(
            _workspaceId,
            new RevokeWorkspacePermissionRequest(_accountId, WorkspaceDeploymentPermissions.Read, _accountId));

        var effective = await service.BootstrapOwnerPermissionsAsync(_workspaceId, _accountId);

        effective.Has(WorkspaceDeploymentPermissions.Read).Should().BeFalse();
        _store.Grants.Count(x => x.Permission == WorkspaceDeploymentPermissions.Read).Should().Be(1);
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

    [Fact]
    public async Task Bootstrap_owner_permissions_includes_contributed_owner_defaults()
    {
        var contribution = new TestPermissionContribution(Set("healing.read", "healing.configure"), Set("healing.read"));
        var service = new WorkspacePermissionService(_store, [contribution]);

        var effective = await service.BootstrapOwnerPermissionsAsync(_workspaceId, _accountId);

        effective.Permissions.Should().BeEquivalentTo(WorkspaceDeploymentPermissions.All.Append("healing.read"));
        effective.Has("healing.configure").Should().BeFalse();
    }

    [Fact]
    public async Task Explicit_member_grant_accepts_a_contributed_permission()
    {
        var service = new WorkspacePermissionService(_store, [Contribution()]);

        await service.GrantAsync(_workspaceId, new GrantWorkspacePermissionRequest(_accountId, "healing.configure", null));

        (await service.GetEffectivePermissionsAsync(_workspaceId, _accountId)).Has("healing.configure").Should().BeTrue();
    }

    [Fact]
    public async Task Revoked_contributed_grant_is_not_effective()
    {
        var service = new WorkspacePermissionService(_store, [Contribution()]);
        _store.Grants.Add(new WorkspacePermissionGrant(
            Guid.NewGuid(),
            _workspaceId,
            _accountId,
            "healing.read",
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

        var effective = await service.GetEffectivePermissionsAsync(_workspaceId, _accountId);

        effective.Has("healing.read").Should().BeFalse();
    }

    [Fact]
    public async Task Require_rejects_missing_contributed_permission()
    {
        var service = new WorkspacePermissionService(_store, [Contribution()]);

        var act = () => service.RequireAsync(_workspaceId, _accountId, "healing.configure");

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*healing.configure*");
    }

    [Fact]
    public async Task Grant_rejects_an_account_that_is_not_a_workspace_member()
    {
        var service = new WorkspacePermissionService(_store, [Contribution()]);
        _store.MembershipCreatedAt = null;

        var act = () => service.GrantAsync(
            _workspaceId,
            new GrantWorkspacePermissionRequest(_accountId, "healing.configure", Guid.NewGuid()));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not a member*");
        _store.Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task Revoke_is_idempotent_and_records_the_actor()
    {
        var service = new WorkspacePermissionService(_store, [Contribution()]);
        var actorId = Guid.NewGuid();
        await service.GrantAsync(_workspaceId, new GrantWorkspacePermissionRequest(_accountId, "healing.read", actorId));

        var first = await service.RevokeAsync(_workspaceId, new RevokeWorkspacePermissionRequest(_accountId, "healing.read", actorId));
        var replay = await service.RevokeAsync(_workspaceId, new RevokeWorkspacePermissionRequest(_accountId, "healing.read", actorId));

        first.Changed.Should().BeTrue();
        first.Grants.Should().ContainSingle(x => x.RevokedByAccountId == actorId);
        replay.Changed.Should().BeFalse();
        _store.Grants.Should().ContainSingle();
    }

    [Fact]
    public async Task Grants_from_a_previous_membership_are_not_effective_after_rejoining()
    {
        var service = new WorkspacePermissionService(_store);
        await service.BootstrapOwnerPermissionsAsync(_workspaceId, _accountId);
        var previousGrantCount = _store.Grants.Count;
        var rejoinedAt = DateTimeOffset.UtcNow;
        for (var index = 0; index < _store.Grants.Count; index++)
            _store.Grants[index] = _store.Grants[index] with { CreatedAt = rejoinedAt.AddMinutes(-1) };
        _store.MembershipCreatedAt = rejoinedAt;

        var beforeProvisioning = await service.GetEffectivePermissionsAsync(_workspaceId, _accountId);
        var reprovisioned = await service.BootstrapOwnerPermissionsAsync(_workspaceId, _accountId);

        beforeProvisioning.Permissions.Should().BeEmpty();
        reprovisioned.Permissions.Should().BeEquivalentTo(WorkspaceDeploymentPermissions.All);
        _store.Grants.Should().HaveCount(previousGrantCount * 2);
    }

    private static TestPermissionContribution Contribution() =>
        new(Set("healing.read", "healing.configure"), Set("healing.read", "healing.configure"));

    private static IReadOnlySet<string> Set(params string[] values) =>
        values.ToHashSet(StringComparer.Ordinal);

    private sealed record TestPermissionContribution(
        IReadOnlySet<string> All,
        IReadOnlySet<string> OwnerDefaults) : IWorkspacePermissionContribution;

    private sealed class RecordingPermissionStore : IWorkspacePermissionStore
    {
        public List<WorkspacePermissionGrant> Grants { get; } = [];
        public DateTimeOffset? MembershipCreatedAt { get; set; } = DateTimeOffset.UtcNow.AddDays(-1);

        public Task<DateTimeOffset?> GetWorkspaceMembershipCreatedAtAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(MembershipCreatedAt);

        public Task<IReadOnlyList<WorkspacePermissionGrant>> GetPermissionGrantsAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspacePermissionGrant>>(Grants.Where(x => x.WorkspaceId == workspaceId && x.AccountId == accountId).ToList());

        public Task<IReadOnlyList<WorkspacePermissionGrant>> ListPermissionGrantsAsync(Guid workspaceId, Guid? accountId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspacePermissionGrant>>(Grants.Where(x => x.WorkspaceId == workspaceId && (!accountId.HasValue || x.AccountId == accountId.Value)).ToList());

        public Task<IReadOnlyList<WorkspacePermissionAuditRecord>> ListPermissionAuditRecordsAsync(Guid workspaceId, Guid? accountId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspacePermissionAuditRecord>>([]);

        public Task<WorkspacePermissionGrant> GrantPermissionAsync(Guid workspaceId, GrantWorkspacePermissionRequest request, CancellationToken cancellationToken = default)
        {
            var existing = Grants.SingleOrDefault(x =>
                x.WorkspaceId == workspaceId &&
                x.AccountId == request.AccountId &&
                x.Permission == request.Permission &&
                x.RevokedAt is null &&
                (!MembershipCreatedAt.HasValue || x.CreatedAt >= MembershipCreatedAt.Value));
            if (existing is not null)
                return Task.FromResult(existing);

            var now = DateTimeOffset.UtcNow;
            var grant = new WorkspacePermissionGrant(Guid.NewGuid(), workspaceId, request.AccountId, request.Permission, request.GrantedByAccountId, now, now, null);
            Grants.Add(grant);
            return Task.FromResult(grant);
        }

        public Task<RevokeWorkspacePermissionResult> RevokePermissionAsync(Guid workspaceId, RevokeWorkspacePermissionRequest request, CancellationToken cancellationToken = default)
        {
            var active = Grants.Where(x =>
                x.WorkspaceId == workspaceId &&
                x.AccountId == request.AccountId &&
                x.Permission == request.Permission &&
                x.RevokedAt is null).ToList();
            if (active.Count == 0)
                return Task.FromResult(new RevokeWorkspacePermissionResult([], false));

            var now = DateTimeOffset.UtcNow;
            var revoked = active.Select(x => x with { RevokedAt = now, UpdatedAt = now, RevokedByAccountId = request.RevokedByAccountId }).ToList();
            foreach (var grant in revoked)
                Grants[Grants.FindIndex(x => x.Id == grant.Id)] = grant;
            return Task.FromResult(new RevokeWorkspacePermissionResult(revoked, true));
        }
    }
}
