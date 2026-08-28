using ElsaControl.Weaver.Core.Plans;
using ElsaControl.Weaver.Core.Sessions;

namespace ElsaControl.Weaver.Core.Tests;

public sealed class WeaverPlanExecutionTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _accountId = Guid.NewGuid();
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.Parse("2026-06-07T12:00:00Z"));
    private readonly RecordingStore _store = new();

    [Fact]
    public async Task Approved_plan_executes_once_per_version()
    {
        var plan = NewPlan();
        _store.Plans[plan.Id] = plan;
        var service = new WeaverPlanExecutionService(_store, _clock);

        var approved = await service.RecordApprovalAsync(_workspaceId, plan.Id, plan.Version, _accountId, WeaverPlanApprovalDecision.Approved, null, null);
        var first = await service.ExecuteAsync(_workspaceId, plan.Id, plan.Version);
        var second = await service.ExecuteAsync(_workspaceId, plan.Id, plan.Version);

        Assert.Equal(WeaverPlanStatus.Approved, approved.Status);
        Assert.Equal(WeaverPlanExecutionStatus.Succeeded, first.Status);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(_store.Executions);
        Assert.Equal(WeaverPlanStatus.Succeeded, _store.Plans[plan.Id].Status);
    }

    [Fact]
    public async Task Unapproved_plan_cannot_execute()
    {
        var plan = NewPlan();
        _store.Plans[plan.Id] = plan;
        var service = new WeaverPlanExecutionService(_store, _clock);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(_workspaceId, plan.Id, plan.Version));

        Assert.Equal("Plan must be approved before execution.", exception.Message);
    }

    private WeaverPlan NewPlan() => new(
        Guid.NewGuid(),
        _sessionId,
        1,
        WeaverPlanType.Promotion,
        "Draft promotion plan",
        "Promote",
        "{}",
        "{}",
        "{}",
        null,
        WeaverPlanRisk.Medium,
        WeaverPlanStatus.ReadyForApproval,
        _accountId,
        _clock.GetUtcNow(),
        _clock.GetUtcNow());

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingStore : IWeaverSessionStore
    {
        public Dictionary<Guid, WeaverPlan> Plans { get; } = [];
        public List<WeaverPlanApproval> Approvals { get; } = [];
        public List<WeaverPlanExecution> Executions { get; } = [];

        public Task<WeaverSession> CreateSessionAsync(WeaverSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WeaverSession?> GetSessionAsync(Guid workspaceId, Guid sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WeaverMessage>> ListMessagesAsync(Guid workspaceId, Guid sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WeaverMessage> AddMessageAsync(Guid workspaceId, WeaverMessage message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WeaverToolCall> AddToolCallAsync(Guid workspaceId, WeaverToolCall toolCall, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WeaverToolCall>> ListToolCallsAsync(Guid workspaceId, Guid sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WeaverPlan> AddPlanAsync(Guid workspaceId, WeaverPlan plan, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WeaverPlan>> ListPlansAsync(Guid workspaceId, Guid sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<WeaverPlan?> GetPlanAsync(Guid workspaceId, Guid planId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Plans.GetValueOrDefault(planId));

        public Task<WeaverPlan> UpdatePlanStatusAsync(Guid workspaceId, Guid planId, int version, WeaverPlanStatus status, CancellationToken cancellationToken = default)
        {
            var plan = Plans[planId];
            plan = plan with { Status = status };
            Plans[planId] = plan;
            return Task.FromResult(plan);
        }

        public Task<WeaverPlanApproval> AddPlanApprovalAsync(Guid workspaceId, WeaverPlanApproval approval, CancellationToken cancellationToken = default)
        {
            Approvals.Add(approval);
            return Task.FromResult(approval);
        }

        public Task<WeaverPlanExecution?> GetPlanExecutionAsync(Guid workspaceId, Guid planId, int planVersion, CancellationToken cancellationToken = default) =>
            Task.FromResult(Executions.SingleOrDefault(x => x.PlanId == planId && x.PlanVersion == planVersion));

        public Task<WeaverPlanExecution> AddPlanExecutionAsync(Guid workspaceId, WeaverPlanExecution execution, CancellationToken cancellationToken = default)
        {
            Executions.Add(execution);
            return Task.FromResult(execution);
        }
    }
}
