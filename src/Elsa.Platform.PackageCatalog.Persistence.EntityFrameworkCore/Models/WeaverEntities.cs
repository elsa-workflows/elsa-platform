using Elsa.Platform.Weaver.Core.Configuration;
using Elsa.Platform.Weaver.Core.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Models;

internal sealed class WeaverSessionEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid AccountId { get; set; }
    public string? CopilotSessionId { get; set; }
    public string? RoutePath { get; set; }
    public string? ContextJson { get; set; }
    public WeaverMode Mode { get; set; }
    public WeaverProviderMode ProviderMode { get; set; }
    public string Model { get; set; } = string.Empty;
    public string? ReasoningEffort { get; set; }
    public WeaverSessionStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<WeaverMessageEntity> Messages { get; set; } = [];
    public List<WeaverToolCallEntity> ToolCalls { get; set; } = [];
    public List<WeaverPlanEntity> Plans { get; set; } = [];
}

internal sealed class WeaverMessageEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public WeaverSessionEntity Session { get; set; } = null!;
    public WeaverMessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public WeaverRedactionState RedactionState { get; set; }
    public int Sequence { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class WeaverToolCallEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public WeaverSessionEntity Session { get; set; } = null!;
    public string ToolName { get; set; } = string.Empty;
    public string? ArgumentsJson { get; set; }
    public string? ArgumentsHash { get; set; }
    public string? ResultSummaryJson { get; set; }
    public WeaverToolAuthorizationResult AuthorizationResult { get; set; }
    public WeaverToolCallStatus Status { get; set; }
    public long? DurationMilliseconds { get; set; }
    public string? TraceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

internal sealed class WeaverPlanEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public WeaverSessionEntity Session { get; set; } = null!;
    public int Version { get; set; }
    public WeaverPlanType PlanType { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string TargetJson { get; set; } = "{}";
    public string ImpactJson { get; set; } = "{}";
    public string ValidationJson { get; set; } = "{}";
    public string? RollbackJson { get; set; }
    public WeaverPlanRisk Risk { get; set; }
    public WeaverPlanStatus Status { get; set; }
    public Guid CreatedByAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<WeaverPlanApprovalEntity> Approvals { get; set; } = [];
    public List<WeaverPlanExecutionEntity> Executions { get; set; } = [];
}

internal sealed class WeaverPlanApprovalEntity
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public WeaverPlanEntity Plan { get; set; } = null!;
    public int PlanVersion { get; set; }
    public Guid AccountId { get; set; }
    public WeaverPlanApprovalDecision Decision { get; set; }
    public string? PermissionSnapshotJson { get; set; }
    public Guid? ConfirmationId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class WeaverPlanExecutionEntity
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public WeaverPlanEntity Plan { get; set; } = null!;
    public int PlanVersion { get; set; }
    public WeaverPlanExecutionStatus Status { get; set; }
    public string LinkedResourceJson { get; set; } = "[]";
    public string? DiagnosticsJson { get; set; }
    public string? TraceId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

internal sealed class WeaverSessionConfiguration : IEntityTypeConfiguration<WeaverSessionEntity>
{
    public void Configure(EntityTypeBuilder<WeaverSessionEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CopilotSessionId).HasMaxLength(256);
        builder.Property(x => x.RoutePath).HasMaxLength(2048);
        builder.Property(x => x.Mode).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ProviderMode).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Model).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ReasoningEffort).HasMaxLength(64);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.UpdatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.CompletedAt).HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.HasIndex(x => new { x.WorkspaceId, x.CreatedAt });
        builder.HasMany(x => x.Messages).WithOne(x => x.Session).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.ToolCalls).WithOne(x => x.Session).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Plans).WithOne(x => x.Session).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WeaverMessageConfiguration : IEntityTypeConfiguration<WeaverMessageEntity>
{
    public void Configure(EntityTypeBuilder<WeaverMessageEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.RedactionState).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.SessionId, x.Sequence }).IsUnique();
    }
}

internal sealed class WeaverToolCallConfiguration : IEntityTypeConfiguration<WeaverToolCallEntity>
{
    public void Configure(EntityTypeBuilder<WeaverToolCallEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ToolName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ArgumentsHash).HasMaxLength(128);
        builder.Property(x => x.AuthorizationResult).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.TraceId).HasMaxLength(128);
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.CompletedAt).HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.HasIndex(x => new { x.SessionId, x.CreatedAt });
    }
}

internal sealed class WeaverPlanConfiguration : IEntityTypeConfiguration<WeaverPlanEntity>
{
    public void Configure(EntityTypeBuilder<WeaverPlanEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PlanType).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.Title).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Summary).HasMaxLength(4096).IsRequired();
        builder.Property(x => x.TargetJson).IsRequired();
        builder.Property(x => x.ImpactJson).IsRequired();
        builder.Property(x => x.ValidationJson).IsRequired();
        builder.Property(x => x.Risk).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.UpdatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.SessionId, x.Version }).IsUnique();
        builder.HasMany(x => x.Approvals).WithOne(x => x.Plan).HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Executions).WithOne(x => x.Plan).HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WeaverPlanApprovalConfiguration : IEntityTypeConfiguration<WeaverPlanApprovalEntity>
{
    public void Configure(EntityTypeBuilder<WeaverPlanApprovalEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Decision).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Reason).HasMaxLength(2048);
        builder.Property(x => x.CreatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.HasIndex(x => new { x.PlanId, x.PlanVersion, x.AccountId });
    }
}

internal sealed class WeaverPlanExecutionConfiguration : IEntityTypeConfiguration<WeaverPlanExecutionEntity>
{
    public void Configure(EntityTypeBuilder<WeaverPlanExecutionEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.LinkedResourceJson).IsRequired();
        builder.Property(x => x.TraceId).HasMaxLength(128);
        builder.Property(x => x.StartedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.CompletedAt).HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null, value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        builder.HasIndex(x => new { x.PlanId, x.PlanVersion }).IsUnique();
    }
}
