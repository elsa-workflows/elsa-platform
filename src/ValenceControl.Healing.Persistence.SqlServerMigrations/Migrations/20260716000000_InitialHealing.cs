using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValenceControl.Healing.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialHealing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HealingAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    AggregateType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CausationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PolicyVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    InputHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OutputHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SafeDetailJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    OccurredAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HealingComponentManifests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceRevision = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BuildId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ManifestDigest = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CanonicalJson = table.Column<string>(type: "nvarchar(max)", maxLength: 262144, nullable: false),
                    TrustState = table.Column<int>(type: "int", nullable: false),
                    VerifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    VerifiedAt = table.Column<long>(type: "bigint", nullable: true),
                    VerificationMethod = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingComponentManifests", x => x.Id);
                    table.UniqueConstraint("AK_HealingComponentManifests_WorkspaceId_ApplicationId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "HealingConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiscoveryEnabled = table.Column<bool>(type: "bit", nullable: false),
                    RepairEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AutomaticMergeEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SignalProfileVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DefaultAttemptLimit = table.Column<int>(type: "int", nullable: false),
                    VerificationWindow = table.Column<TimeSpan>(type: "time", nullable: false),
                    TimeBudget = table.Column<TimeSpan>(type: "time", nullable: false),
                    ConcurrencyBudget = table.Column<int>(type: "int", nullable: false),
                    InferenceBudget = table.Column<long>(type: "bigint", nullable: false),
                    RepositoryRunBudget = table.Column<int>(type: "int", nullable: false),
                    ApplicationKillSwitch = table.Column<bool>(type: "bit", nullable: false),
                    ClassificationPolicyJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingConfigurations", x => x.Id);
                    table.UniqueConstraint("AK_HealingConfigurations_WorkspaceId_ApplicationId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "HealingDeploymentObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DeployedAt = table.Column<long>(type: "bigint", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    SourceIdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TrustIdentity = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    EvidenceDigest = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AcceptedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingDeploymentObservations", x => x.Id);
                    table.UniqueConstraint("AK_HealingDeploymentObservations_WorkspaceId_ApplicationId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "HealingPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PolicyHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    PolicyKind = table.Column<int>(type: "int", nullable: false),
                    RequireReproduction = table.Column<bool>(type: "bit", nullable: true),
                    AllowHighConfidenceInference = table.Column<bool>(type: "bit", nullable: true),
                    MinimumInferenceConfidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    MaximumTier = table.Column<int>(type: "int", nullable: true),
                    PermittedFieldsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    AutomaticMergeEnabled = table.Column<bool>(type: "bit", nullable: true),
                    RequiredChecksJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    IndependentVerifier = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ForbiddenChangeCategoriesJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    RequireRollbackOrStopCapability = table.Column<bool>(type: "bit", nullable: true),
                    AllowedRootsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    ForbiddenRootsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    MaxFiles = table.Column<int>(type: "int", nullable: true),
                    MaxChangedLines = table.Column<int>(type: "int", nullable: true),
                    MaxPatchBytes = table.Column<int>(type: "int", nullable: true),
                    AllowBinary = table.Column<bool>(type: "bit", nullable: true),
                    AllowRenames = table.Column<bool>(type: "bit", nullable: true),
                    AllowSymlinks = table.Column<bool>(type: "bit", nullable: true),
                    AllowSubmodules = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingPolicies", x => x.Id);
                    table.UniqueConstraint("AK_HealingPolicies_WorkspaceId_ApplicationId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "HealingProviderConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    InstallationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RepositoryProviderId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RepositoryOwner = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    RepositoryName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    CredentialReference = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingProviderConnections", x => x.Id);
                    table.UniqueConstraint("AK_HealingProviderConnections_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "HealingProviderWebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderDeliveryId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    InstallationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RepositoryProviderId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Event = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    BodyDigest = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RetainedBody = table.Column<string>(type: "nvarchar(max)", maxLength: 262144, nullable: true),
                    ReceivedAt = table.Column<long>(type: "bigint", nullable: false),
                    ProcessedAt = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OutcomeCode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SafeOutcomeDetail = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingProviderWebhookDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HealingSignalInboxItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    ProfileVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OccurredAt = table.Column<long>(type: "bigint", nullable: false),
                    AcceptedAt = table.Column<long>(type: "bigint", nullable: false),
                    RedactedEnvelopeJson = table.Column<string>(type: "nvarchar(max)", maxLength: 262144, nullable: false),
                    EnvelopeHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LeaseOwner = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LeaseToken = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LeaseExpiresAt = table.Column<long>(type: "bigint", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<long>(type: "bigint", nullable: true),
                    OutcomeCode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SafeOutcomeDetail = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingSignalInboxItems", x => x.Id);
                    table.UniqueConstraint("AK_HealingSignalInboxItems_WorkspaceId_ApplicationId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "HealingTelemetrySources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CredentialSalt = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    CredentialHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    CredentialVersion = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    RotatedAt = table.Column<long>(type: "bigint", nullable: true),
                    RevokedAt = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingTelemetrySources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HealingWorkspaceConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceKillSwitch = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingWorkspaceConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HealingComponentManifestEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    KindName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PackageId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PackageVersion = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AssemblyName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    AssemblyVersion = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PublicKeyToken = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    RepositoryUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    RepositoryCommit = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SourceRoot = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    IsDirectDependency = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingComponentManifestEntries", x => x.Id);
                    table.UniqueConstraint("AK_HealingComponentManifestEntries_ManifestId_Id", x => new { x.ManifestId, x.Id });
                    table.UniqueConstraint("AK_HealingComponentManifestEntries_WorkspaceId_ApplicationId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.Id });
                    table.UniqueConstraint("AK_HealingComponentManifestEntries_WorkspaceId_ApplicationId_ManifestId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.ManifestId, x.Id });
                    table.ForeignKey(
                        name: "FK_HealingComponentManifestEntries_HealingComponentManifests_WorkspaceId_ApplicationId_ManifestId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.ManifestId },
                        principalTable: "HealingComponentManifests",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HealingComponentManifestRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ManifestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingComponentManifestRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingComponentManifestRegistrations_HealingComponentManifests_WorkspaceId_ApplicationId_ManifestId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.ManifestId },
                        principalTable: "HealingComponentManifests",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HealingEnvironmentConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HealingConfigurationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiscoveryEnabled = table.Column<bool>(type: "bit", nullable: true),
                    RepairEnabled = table.Column<bool>(type: "bit", nullable: true),
                    OccurrenceThreshold = table.Column<int>(type: "int", nullable: true),
                    DebounceWindow = table.Column<TimeSpan>(type: "time", nullable: true),
                    EnvironmentKillSwitch = table.Column<bool>(type: "bit", nullable: false),
                    ClassificationPolicyJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingEnvironmentConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingEnvironmentConfigurations_HealingConfigurations_WorkspaceId_ApplicationId_HealingConfigurationId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.HealingConfigurationId },
                        principalTable: "HealingConfigurations",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HealingSourceOwnershipBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    SelectorKind = table.Column<int>(type: "int", nullable: false),
                    SelectorPattern = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    ProviderConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepositoryProviderId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RepositoryOwner = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    RepositoryName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    TargetBranch = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    WorkflowIdentity = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    WorkflowRevision = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PathPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidencePolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MergePolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApprovedAt = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingSourceOwnershipBindings", x => x.Id);
                    table.UniqueConstraint("AK_HealingSourceOwnershipBindings_WorkspaceId_ApplicationId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.Id });
                    table.ForeignKey(
                        name: "FK_HealingSourceOwnershipBindings_HealingPolicies_WorkspaceId_ApplicationId_EvidencePolicyId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.EvidencePolicyId },
                        principalTable: "HealingPolicies",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingSourceOwnershipBindings_HealingPolicies_WorkspaceId_ApplicationId_MergePolicyId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.MergePolicyId },
                        principalTable: "HealingPolicies",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingSourceOwnershipBindings_HealingPolicies_WorkspaceId_ApplicationId_PathPolicyId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.PathPolicyId },
                        principalTable: "HealingPolicies",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingSourceOwnershipBindings_HealingProviderConnections_WorkspaceId_ProviderConnectionId",
                        columns: x => new { x.WorkspaceId, x.ProviderConnectionId },
                        principalTable: "HealingProviderConnections",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HealingComponentDependencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingComponentDependencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingComponentDependencies_HealingComponentManifestEntries_ManifestId_FromEntryId",
                        columns: x => new { x.ManifestId, x.FromEntryId },
                        principalTable: "HealingComponentManifestEntries",
                        principalColumns: new[] { "ManifestId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingComponentDependencies_HealingComponentManifestEntries_ManifestId_ToEntryId",
                        columns: x => new { x.ManifestId, x.ToEntryId },
                        principalTable: "HealingComponentManifestEntries",
                        principalColumns: new[] { "ManifestId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingComponentDependencies_HealingComponentManifests_ManifestId",
                        column: x => x.ManifestId,
                        principalTable: "HealingComponentManifests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HealingComponentManifestAssemblies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PublicKeyToken = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RelativePath = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingComponentManifestAssemblies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingComponentManifestAssemblies_HealingComponentManifestEntries_WorkspaceId_ApplicationId_ManifestId_ComponentEntryId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.ManifestId, x.ComponentEntryId },
                        principalTable: "HealingComponentManifestEntries",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "ManifestId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HealingComponentAttributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurrenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    Resolution = table.Column<int>(type: "int", nullable: false),
                    ReasonCodesJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingComponentAttributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingComponentAttributions_HealingComponentManifestEntries_WorkspaceId_ApplicationId_ComponentEntryId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.ComponentEntryId },
                        principalTable: "HealingComponentManifestEntries",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingComponentAttributions_HealingSourceOwnershipBindings_WorkspaceId_ApplicationId_BindingId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.BindingId },
                        principalTable: "HealingSourceOwnershipBindings",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HealingEnvironmentImpacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstSeenAt = table.Column<long>(type: "bigint", nullable: false),
                    LastSeenAt = table.Column<long>(type: "bigint", nullable: false),
                    OccurrenceCount = table.Column<long>(type: "bigint", nullable: false),
                    ProducingRevisionsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    CurrentDeployedRevision = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    VerificationStatus = table.Column<int>(type: "int", nullable: false),
                    OccurrenceThreshold = table.Column<int>(type: "int", nullable: false),
                    DebounceWindow = table.Column<TimeSpan>(type: "time", nullable: false),
                    ThresholdReachedAt = table.Column<long>(type: "bigint", nullable: true),
                    ReadyAfter = table.Column<long>(type: "bigint", nullable: true),
                    ClassificationPolicyVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ClassificationPolicyHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ClosedByActorId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ClosedAt = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingEnvironmentImpacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HealingEvidenceAccessDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleasedBundleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequesterId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RequestedTier = table.Column<int>(type: "int", nullable: false),
                    RequestedFieldsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    Authorized = table.Column<bool>(type: "bit", nullable: false),
                    ReasonCodesJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DecidedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingEvidenceAccessDecisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HealingEvidenceBundles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    CanonicalJson = table.Column<string>(type: "nvarchar(max)", maxLength: 262144, nullable: false),
                    Digest = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProvenanceJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    OmissionsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingEvidenceBundles", x => x.Id);
                    table.UniqueConstraint("AK_HealingEvidenceBundles_WorkspaceId_ApplicationId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "HealingHumanCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Command = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProviderActorId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ControlActorId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ProviderPermissionSnapshotJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    WorkspacePermissionGranted = table.Column<bool>(type: "bit", nullable: false),
                    ConfirmationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResultCode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SafeResultDetail = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    RequestedAt = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAt = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingHumanCommands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HealingIncidentEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousEpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OpenedAt = table.Column<long>(type: "bigint", nullable: false),
                    ClosedAt = table.Column<long>(type: "bigint", nullable: true),
                    ProducingRevisionsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    TargetRevision = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    RegressionReason = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingIncidentEpisodes", x => x.Id);
                    table.UniqueConstraint("AK_HealingIncidentEpisodes_WorkspaceId_ApplicationId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.Id });
                    table.UniqueConstraint("AK_HealingIncidentEpisodes_WorkspaceId_ApplicationId_IncidentId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId, x.Id });
                    table.ForeignKey(
                        name: "FK_HealingIncidentEpisodes_HealingIncidentEpisodes_WorkspaceId_ApplicationId_PreviousEpisodeId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.PreviousEpisodeId },
                        principalTable: "HealingIncidentEpisodes",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HealingIncidentOccurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InboxItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurrenceKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OccurredAt = table.Column<long>(type: "bigint", nullable: false),
                    AcceptedAt = table.Column<long>(type: "bigint", nullable: false),
                    Classification = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    ExceptionType = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    OperationName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    NormalizedStackJson = table.Column<string>(type: "nvarchar(max)", maxLength: 262144, nullable: false),
                    TraceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SpanId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RetryState = table.Column<int>(type: "int", nullable: false),
                    FingerprintVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EvidenceTier = table.Column<int>(type: "int", nullable: false),
                    EvidenceDigest = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingIncidentOccurrences", x => x.Id);
                    table.UniqueConstraint("AK_HealingIncidentOccurrences_WorkspaceId_ApplicationId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.Id });
                    table.ForeignKey(
                        name: "FK_HealingIncidentOccurrences_HealingIncidentEpisodes_WorkspaceId_ApplicationId_EpisodeId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.EpisodeId },
                        principalTable: "HealingIncidentEpisodes",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingIncidentOccurrences_HealingSignalInboxItems_WorkspaceId_ApplicationId_InboxItemId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.InboxItemId },
                        principalTable: "HealingSignalInboxItems",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HealingVerificationResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepairedRevision = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    WindowStartedAt = table.Column<long>(type: "bigint", nullable: true),
                    WindowEndsAt = table.Column<long>(type: "bigint", nullable: true),
                    RelevantOperationSuccessCount = table.Column<long>(type: "bigint", nullable: false),
                    LastRelevantOperationSuccessAt = table.Column<long>(type: "bigint", nullable: true),
                    RecurrenceCount = table.Column<long>(type: "bigint", nullable: false),
                    LastRecurrenceAt = table.Column<long>(type: "bigint", nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    DeploymentObservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupportingOccurrenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAt = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingVerificationResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingVerificationResults_HealingDeploymentObservations_WorkspaceId_ApplicationId_DeploymentObservationId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.DeploymentObservationId },
                        principalTable: "HealingDeploymentObservations",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingVerificationResults_HealingIncidentEpisodes_WorkspaceId_ApplicationId_EpisodeId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.EpisodeId },
                        principalTable: "HealingIncidentEpisodes",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingVerificationResults_HealingIncidentOccurrences_WorkspaceId_ApplicationId_SupportingOccurrenceId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.SupportingOccurrenceId },
                        principalTable: "HealingIncidentOccurrences",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HealingIncidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FingerprintVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RepairRepositoryKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Classification = table.Column<int>(type: "int", nullable: false),
                    SelectedBindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SelectedComponentEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FirstSeenAt = table.Column<long>(type: "bigint", nullable: false),
                    LastSeenAt = table.Column<long>(type: "bigint", nullable: false),
                    OccurrenceCount = table.Column<long>(type: "bigint", nullable: false),
                    ActiveEpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkItemProjectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NeedsHumanReason = table.Column<int>(type: "int", nullable: true),
                    ReadyAfter = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingIncidents", x => x.Id);
                    table.UniqueConstraint("AK_HealingIncidents_WorkspaceId_ApplicationId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.Id });
                    table.ForeignKey(
                        name: "FK_HealingIncidents_HealingComponentManifestEntries_WorkspaceId_ApplicationId_SelectedComponentEntryId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.SelectedComponentEntryId },
                        principalTable: "HealingComponentManifestEntries",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingIncidents_HealingIncidentEpisodes_WorkspaceId_ApplicationId_Id_ActiveEpisodeId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.Id, x.ActiveEpisodeId },
                        principalTable: "HealingIncidentEpisodes",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "IncidentId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingIncidents_HealingSourceOwnershipBindings_WorkspaceId_ApplicationId_SelectedBindingId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.SelectedBindingId },
                        principalTable: "HealingSourceOwnershipBindings",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HealingRepairAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    ProducingRevision = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TargetRevision = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EvidenceBundleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepairClassification = table.Column<int>(type: "int", nullable: false),
                    NonceHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LeaseOwner = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LeaseToken = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LeaseExpiresAt = table.Column<long>(type: "bigint", nullable: true),
                    BudgetJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    UsageJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    StartedAt = table.Column<long>(type: "bigint", nullable: true),
                    CompletedAt = table.Column<long>(type: "bigint", nullable: true),
                    OutcomeCode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SafeOutcomeDetail = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingRepairAttempts", x => x.Id);
                    table.UniqueConstraint("AK_HealingRepairAttempts_WorkspaceId_ApplicationId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.Id });
                    table.ForeignKey(
                        name: "FK_HealingRepairAttempts_HealingEvidenceBundles_WorkspaceId_ApplicationId_EvidenceBundleId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.EvidenceBundleId },
                        principalTable: "HealingEvidenceBundles",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingRepairAttempts_HealingIncidentEpisodes_WorkspaceId_ApplicationId_EpisodeId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.EpisodeId },
                        principalTable: "HealingIncidentEpisodes",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingRepairAttempts_HealingIncidents_WorkspaceId_ApplicationId_IncidentId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId },
                        principalTable: "HealingIncidents",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingRepairAttempts_HealingSourceOwnershipBindings_WorkspaceId_ApplicationId_BindingId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.BindingId },
                        principalTable: "HealingSourceOwnershipBindings",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HealingRepairWorkItemProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderWorkItemId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Number = table.Column<long>(type: "bigint", nullable: true),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    MachineSummaryHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderState = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ProjectionStatus = table.Column<int>(type: "int", nullable: false),
                    LastProjectedAt = table.Column<long>(type: "bigint", nullable: true),
                    LastObservedAt = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingRepairWorkItemProjections", x => x.Id);
                    table.UniqueConstraint("AK_HealingRepairWorkItemProjections_WorkspaceId_ApplicationId_IncidentId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId, x.Id });
                    table.ForeignKey(
                        name: "FK_HealingRepairWorkItemProjections_HealingIncidentEpisodes_WorkspaceId_ApplicationId_EpisodeId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.EpisodeId },
                        principalTable: "HealingIncidentEpisodes",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingRepairWorkItemProjections_HealingIncidents_WorkspaceId_ApplicationId_IncidentId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId },
                        principalTable: "HealingIncidents",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingRepairWorkItemProjections_HealingProviderConnections_WorkspaceId_ProviderConnectionId",
                        columns: x => new { x.WorkspaceId, x.ProviderConnectionId },
                        principalTable: "HealingProviderConnections",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HealingPolicyEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyKind = table.Column<int>(type: "int", nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PolicyHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    InputSnapshotHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    GateResultsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    ReasonCodesJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    EvaluatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingPolicyEvaluations", x => x.Id);
                    table.UniqueConstraint("AK_HealingPolicyEvaluations_WorkspaceId_ApplicationId_Id", x => new { x.WorkspaceId, x.ApplicationId, x.Id });
                    table.ForeignKey(
                        name: "FK_HealingPolicyEvaluations_HealingPolicies_WorkspaceId_ApplicationId_PolicyId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.PolicyId },
                        principalTable: "HealingPolicies",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingPolicyEvaluations_HealingRepairAttempts_WorkspaceId_ApplicationId_AttemptId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId },
                        principalTable: "HealingRepairAttempts",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HealingProviderOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", maxLength: 262144, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LeaseOwner = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LeaseToken = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LeaseExpiresAt = table.Column<long>(type: "bigint", nullable: true),
                    NextAttemptAt = table.Column<long>(type: "bigint", nullable: true),
                    ProviderCorrelationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    OutcomeCode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SafeError = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingProviderOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingProviderOperations_HealingIncidents_WorkspaceId_ApplicationId_IncidentId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.IncidentId },
                        principalTable: "HealingIncidents",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingProviderOperations_HealingProviderConnections_WorkspaceId_ProviderConnectionId",
                        columns: x => new { x.WorkspaceId, x.ProviderConnectionId },
                        principalTable: "HealingProviderConnections",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingProviderOperations_HealingRepairAttempts_WorkspaceId_ApplicationId_AttemptId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId },
                        principalTable: "HealingRepairAttempts",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HealingRepairResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    WorkflowRunId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    WorkflowRunAttempt = table.Column<int>(type: "int", nullable: false),
                    BaseRevision = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TargetRevision = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Classification = table.Column<int>(type: "int", nullable: false),
                    Confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    UnifiedDiff = table.Column<string>(type: "nvarchar(max)", maxLength: 1048576, nullable: false),
                    PatchDigest = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ChangedPathsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    ReproductionJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    RegressionJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    ValidationJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    RiskJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    SubmittedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingRepairResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingRepairResults_HealingRepairAttempts_WorkspaceId_ApplicationId_AttemptId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId },
                        principalTable: "HealingRepairAttempts",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HealingWorkloadIdentityExchanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Issuer = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Audience = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    RepositoryProviderId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RepositoryOwner = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    RepositoryName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    WorkflowReference = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    WorkflowRevision = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceReference = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    SourceRevision = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    WorkflowRunId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    WorkflowRunAttempt = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    JwtId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NonceHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IssuedAt = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    ExchangedAt = table.Column<long>(type: "bigint", nullable: true),
                    CapabilityTokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingWorkloadIdentityExchanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingWorkloadIdentityExchanges_HealingRepairAttempts_WorkspaceId_ApplicationId_AttemptId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId },
                        principalTable: "HealingRepairAttempts",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HealingRepairPullRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderPullRequestId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Branch = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BaseRevision = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    HeadRevision = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PatchDigest = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsDraft = table.Column<bool>(type: "bit", nullable: false),
                    Classification = table.Column<int>(type: "int", nullable: false),
                    CheckSnapshotJson = table.Column<string>(type: "nvarchar(max)", maxLength: 262144, nullable: false),
                    BranchProtectionSnapshotJson = table.Column<string>(type: "nvarchar(max)", maxLength: 262144, nullable: false),
                    MergePolicyEvaluationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MergeState = table.Column<int>(type: "int", nullable: false),
                    MergedRevision = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    MergedAt = table.Column<long>(type: "bigint", nullable: true),
                    ClosureReason = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    Version = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealingRepairPullRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealingRepairPullRequests_HealingPolicyEvaluations_WorkspaceId_ApplicationId_MergePolicyEvaluationId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.MergePolicyEvaluationId },
                        principalTable: "HealingPolicyEvaluations",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingRepairPullRequests_HealingProviderConnections_WorkspaceId_ProviderConnectionId",
                        columns: x => new { x.WorkspaceId, x.ProviderConnectionId },
                        principalTable: "HealingProviderConnections",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealingRepairPullRequests_HealingRepairAttempts_WorkspaceId_ApplicationId_AttemptId",
                        columns: x => new { x.WorkspaceId, x.ApplicationId, x.AttemptId },
                        principalTable: "HealingRepairAttempts",
                        principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HealingAuditEvents_WorkspaceId_AggregateType_AggregateId_EventType_CorrelationId",
                table: "HealingAuditEvents",
                columns: new[] { "WorkspaceId", "AggregateType", "AggregateId", "EventType", "CorrelationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingAuditEvents_WorkspaceId_AggregateType_AggregateId_Sequence",
                table: "HealingAuditEvents",
                columns: new[] { "WorkspaceId", "AggregateType", "AggregateId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingAuditEvents_WorkspaceId_CorrelationId_OccurredAt",
                table: "HealingAuditEvents",
                columns: new[] { "WorkspaceId", "CorrelationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentAttributions_OccurrenceId_ComponentEntryId",
                table: "HealingComponentAttributions",
                columns: new[] { "OccurrenceId", "ComponentEntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentAttributions_WorkspaceId_ApplicationId_BindingId",
                table: "HealingComponentAttributions",
                columns: new[] { "WorkspaceId", "ApplicationId", "BindingId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentAttributions_WorkspaceId_ApplicationId_ComponentEntryId",
                table: "HealingComponentAttributions",
                columns: new[] { "WorkspaceId", "ApplicationId", "ComponentEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentAttributions_WorkspaceId_ApplicationId_OccurrenceId",
                table: "HealingComponentAttributions",
                columns: new[] { "WorkspaceId", "ApplicationId", "OccurrenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentDependencies_ManifestId_FromEntryId_ToEntryId",
                table: "HealingComponentDependencies",
                columns: new[] { "ManifestId", "FromEntryId", "ToEntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentDependencies_ManifestId_ToEntryId",
                table: "HealingComponentDependencies",
                columns: new[] { "ManifestId", "ToEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentManifestAssemblies_ManifestId_ComponentEntryId_RelativePath",
                table: "HealingComponentManifestAssemblies",
                columns: new[] { "ManifestId", "ComponentEntryId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentManifestAssemblies_WorkspaceId_ApplicationId_ManifestId_ComponentEntryId",
                table: "HealingComponentManifestAssemblies",
                columns: new[] { "WorkspaceId", "ApplicationId", "ManifestId", "ComponentEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentManifestEntries_ManifestId_ComponentKey",
                table: "HealingComponentManifestEntries",
                columns: new[] { "ManifestId", "ComponentKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentManifestRegistrations_WorkspaceId_ApplicationId_ManifestId",
                table: "HealingComponentManifestRegistrations",
                columns: new[] { "WorkspaceId", "ApplicationId", "ManifestId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentManifestRegistrations_WorkspaceId_ApplicationId_RevisionId_IdempotencyKey",
                table: "HealingComponentManifestRegistrations",
                columns: new[] { "WorkspaceId", "ApplicationId", "RevisionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingComponentManifests_WorkspaceId_ApplicationId_RevisionId",
                table: "HealingComponentManifests",
                columns: new[] { "WorkspaceId", "ApplicationId", "RevisionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingConfigurations_WorkspaceId_ApplicationId",
                table: "HealingConfigurations",
                columns: new[] { "WorkspaceId", "ApplicationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingDeploymentObservations_WorkspaceId_ApplicationId_EnvironmentId_Revision",
                table: "HealingDeploymentObservations",
                columns: new[] { "WorkspaceId", "ApplicationId", "EnvironmentId", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingDeploymentObservations_WorkspaceId_ApplicationId_Source_SourceIdempotencyKey",
                table: "HealingDeploymentObservations",
                columns: new[] { "WorkspaceId", "ApplicationId", "Source", "SourceIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingEnvironmentConfigurations_WorkspaceId_ApplicationId_EnvironmentId",
                table: "HealingEnvironmentConfigurations",
                columns: new[] { "WorkspaceId", "ApplicationId", "EnvironmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingEnvironmentConfigurations_WorkspaceId_ApplicationId_HealingConfigurationId",
                table: "HealingEnvironmentConfigurations",
                columns: new[] { "WorkspaceId", "ApplicationId", "HealingConfigurationId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingEnvironmentImpacts_EpisodeId_EnvironmentId",
                table: "HealingEnvironmentImpacts",
                columns: new[] { "EpisodeId", "EnvironmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingEnvironmentImpacts_WorkspaceId_ApplicationId_EpisodeId",
                table: "HealingEnvironmentImpacts",
                columns: new[] { "WorkspaceId", "ApplicationId", "EpisodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingEvidenceAccessDecisions_WorkspaceId_ApplicationId_IncidentId",
                table: "HealingEvidenceAccessDecisions",
                columns: new[] { "WorkspaceId", "ApplicationId", "IncidentId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingEvidenceAccessDecisions_WorkspaceId_ApplicationId_ReleasedBundleId",
                table: "HealingEvidenceAccessDecisions",
                columns: new[] { "WorkspaceId", "ApplicationId", "ReleasedBundleId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingEvidenceAccessDecisions_WorkspaceId_IncidentId_DecidedAt",
                table: "HealingEvidenceAccessDecisions",
                columns: new[] { "WorkspaceId", "IncidentId", "DecidedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingEvidenceBundles_WorkspaceId_ApplicationId_IncidentId",
                table: "HealingEvidenceBundles",
                columns: new[] { "WorkspaceId", "ApplicationId", "IncidentId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingEvidenceBundles_WorkspaceId_Digest",
                table: "HealingEvidenceBundles",
                columns: new[] { "WorkspaceId", "Digest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingHumanCommands_WorkspaceId_ApplicationId_IncidentId",
                table: "HealingHumanCommands",
                columns: new[] { "WorkspaceId", "ApplicationId", "IncidentId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingHumanCommands_WorkspaceId_IncidentId_RequestedAt",
                table: "HealingHumanCommands",
                columns: new[] { "WorkspaceId", "IncidentId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingIncidentEpisodes_IncidentId_OpenedAt",
                table: "HealingIncidentEpisodes",
                columns: new[] { "IncidentId", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingIncidentEpisodes_WorkspaceId_ApplicationId_PreviousEpisodeId",
                table: "HealingIncidentEpisodes",
                columns: new[] { "WorkspaceId", "ApplicationId", "PreviousEpisodeId" },
                unique: true,
                filter: "[PreviousEpisodeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HealingIncidentOccurrences_InboxItemId",
                table: "HealingIncidentOccurrences",
                column: "InboxItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingIncidentOccurrences_WorkspaceId_ApplicationId_EpisodeId",
                table: "HealingIncidentOccurrences",
                columns: new[] { "WorkspaceId", "ApplicationId", "EpisodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingIncidentOccurrences_WorkspaceId_ApplicationId_FingerprintVersion_Fingerprint",
                table: "HealingIncidentOccurrences",
                columns: new[] { "WorkspaceId", "ApplicationId", "FingerprintVersion", "Fingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingIncidentOccurrences_WorkspaceId_ApplicationId_InboxItemId",
                table: "HealingIncidentOccurrences",
                columns: new[] { "WorkspaceId", "ApplicationId", "InboxItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingIncidentOccurrences_WorkspaceId_ApplicationId_IncidentId",
                table: "HealingIncidentOccurrences",
                columns: new[] { "WorkspaceId", "ApplicationId", "IncidentId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingIncidentOccurrences_WorkspaceId_ApplicationId_OccurrenceKey",
                table: "HealingIncidentOccurrences",
                columns: new[] { "WorkspaceId", "ApplicationId", "OccurrenceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingIncidents_Status_ReadyAfter",
                table: "HealingIncidents",
                columns: new[] { "Status", "ReadyAfter" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingIncidents_WorkspaceId_ApplicationId_FingerprintVersion_Fingerprint_RepairRepositoryKey",
                table: "HealingIncidents",
                columns: new[] { "WorkspaceId", "ApplicationId", "FingerprintVersion", "Fingerprint", "RepairRepositoryKey" },
                unique: true,
                filter: "[Status] NOT IN (8, 11, 13, 14)");

            migrationBuilder.CreateIndex(
                name: "IX_HealingIncidents_WorkspaceId_ApplicationId_Id_ActiveEpisodeId",
                table: "HealingIncidents",
                columns: new[] { "WorkspaceId", "ApplicationId", "Id", "ActiveEpisodeId" },
                unique: true,
                filter: "[ActiveEpisodeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HealingIncidents_WorkspaceId_ApplicationId_Id_WorkItemProjectionId",
                table: "HealingIncidents",
                columns: new[] { "WorkspaceId", "ApplicationId", "Id", "WorkItemProjectionId" },
                unique: true,
                filter: "[WorkItemProjectionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HealingIncidents_WorkspaceId_ApplicationId_SelectedBindingId",
                table: "HealingIncidents",
                columns: new[] { "WorkspaceId", "ApplicationId", "SelectedBindingId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingIncidents_WorkspaceId_ApplicationId_SelectedComponentEntryId",
                table: "HealingIncidents",
                columns: new[] { "WorkspaceId", "ApplicationId", "SelectedComponentEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingIncidents_WorkspaceId_ApplicationId_Status_LastSeenAt",
                table: "HealingIncidents",
                columns: new[] { "WorkspaceId", "ApplicationId", "Status", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingPolicies_WorkspaceId_ApplicationId_Name_PolicyVersion",
                table: "HealingPolicies",
                columns: new[] { "WorkspaceId", "ApplicationId", "Name", "PolicyVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingPolicyEvaluations_WorkspaceId_ApplicationId_AttemptId",
                table: "HealingPolicyEvaluations",
                columns: new[] { "WorkspaceId", "ApplicationId", "AttemptId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingPolicyEvaluations_WorkspaceId_ApplicationId_PolicyId",
                table: "HealingPolicyEvaluations",
                columns: new[] { "WorkspaceId", "ApplicationId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingPolicyEvaluations_WorkspaceId_AttemptId_PolicyId_EvaluatedAt",
                table: "HealingPolicyEvaluations",
                columns: new[] { "WorkspaceId", "AttemptId", "PolicyId", "EvaluatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingProviderConnections_WorkspaceId_Provider_RepositoryProviderId",
                table: "HealingProviderConnections",
                columns: new[] { "WorkspaceId", "Provider", "RepositoryProviderId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingProviderOperations_Status_NextAttemptAt_LeaseExpiresAt",
                table: "HealingProviderOperations",
                columns: new[] { "Status", "NextAttemptAt", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingProviderOperations_WorkspaceId_ApplicationId_AttemptId",
                table: "HealingProviderOperations",
                columns: new[] { "WorkspaceId", "ApplicationId", "AttemptId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingProviderOperations_WorkspaceId_ApplicationId_IncidentId",
                table: "HealingProviderOperations",
                columns: new[] { "WorkspaceId", "ApplicationId", "IncidentId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingProviderOperations_WorkspaceId_ProviderConnectionId_IdempotencyKey",
                table: "HealingProviderOperations",
                columns: new[] { "WorkspaceId", "ProviderConnectionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingProviderWebhookDeliveries_WorkspaceId_ProviderDeliveryId",
                table: "HealingProviderWebhookDeliveries",
                columns: new[] { "WorkspaceId", "ProviderDeliveryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairAttempts_EpisodeId_TargetRevision_AttemptNumber",
                table: "HealingRepairAttempts",
                columns: new[] { "EpisodeId", "TargetRevision", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairAttempts_Status_LeaseExpiresAt",
                table: "HealingRepairAttempts",
                columns: new[] { "Status", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairAttempts_WorkspaceId_ApplicationId_BindingId",
                table: "HealingRepairAttempts",
                columns: new[] { "WorkspaceId", "ApplicationId", "BindingId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairAttempts_WorkspaceId_ApplicationId_EpisodeId",
                table: "HealingRepairAttempts",
                columns: new[] { "WorkspaceId", "ApplicationId", "EpisodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairAttempts_WorkspaceId_ApplicationId_EvidenceBundleId",
                table: "HealingRepairAttempts",
                columns: new[] { "WorkspaceId", "ApplicationId", "EvidenceBundleId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairAttempts_WorkspaceId_ApplicationId_IncidentId",
                table: "HealingRepairAttempts",
                columns: new[] { "WorkspaceId", "ApplicationId", "IncidentId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairPullRequests_AttemptId",
                table: "HealingRepairPullRequests",
                column: "AttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairPullRequests_ProviderConnectionId_ProviderPullRequestId",
                table: "HealingRepairPullRequests",
                columns: new[] { "ProviderConnectionId", "ProviderPullRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairPullRequests_WorkspaceId_ApplicationId_AttemptId",
                table: "HealingRepairPullRequests",
                columns: new[] { "WorkspaceId", "ApplicationId", "AttemptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairPullRequests_WorkspaceId_ApplicationId_MergePolicyEvaluationId",
                table: "HealingRepairPullRequests",
                columns: new[] { "WorkspaceId", "ApplicationId", "MergePolicyEvaluationId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairPullRequests_WorkspaceId_ProviderConnectionId",
                table: "HealingRepairPullRequests",
                columns: new[] { "WorkspaceId", "ProviderConnectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairResults_AttemptId",
                table: "HealingRepairResults",
                column: "AttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairResults_AttemptId_IdempotencyKey",
                table: "HealingRepairResults",
                columns: new[] { "AttemptId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairResults_WorkspaceId_ApplicationId_AttemptId",
                table: "HealingRepairResults",
                columns: new[] { "WorkspaceId", "ApplicationId", "AttemptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairWorkItemProjections_IncidentId_EpisodeId",
                table: "HealingRepairWorkItemProjections",
                columns: new[] { "IncidentId", "EpisodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairWorkItemProjections_WorkspaceId_ApplicationId_EpisodeId",
                table: "HealingRepairWorkItemProjections",
                columns: new[] { "WorkspaceId", "ApplicationId", "EpisodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingRepairWorkItemProjections_WorkspaceId_ProviderConnectionId",
                table: "HealingRepairWorkItemProjections",
                columns: new[] { "WorkspaceId", "ProviderConnectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingSignalInboxItems_Status_NextAttemptAt_LeaseExpiresAt",
                table: "HealingSignalInboxItems",
                columns: new[] { "Status", "NextAttemptAt", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingSignalInboxItems_WorkspaceId_ApplicationId_IdempotencyKey",
                table: "HealingSignalInboxItems",
                columns: new[] { "WorkspaceId", "ApplicationId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingSourceOwnershipBindings_WorkspaceId_ApplicationId_EvidencePolicyId",
                table: "HealingSourceOwnershipBindings",
                columns: new[] { "WorkspaceId", "ApplicationId", "EvidencePolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingSourceOwnershipBindings_WorkspaceId_ApplicationId_MergePolicyId",
                table: "HealingSourceOwnershipBindings",
                columns: new[] { "WorkspaceId", "ApplicationId", "MergePolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingSourceOwnershipBindings_WorkspaceId_ApplicationId_Name",
                table: "HealingSourceOwnershipBindings",
                columns: new[] { "WorkspaceId", "ApplicationId", "Name" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_HealingSourceOwnershipBindings_WorkspaceId_ApplicationId_PathPolicyId",
                table: "HealingSourceOwnershipBindings",
                columns: new[] { "WorkspaceId", "ApplicationId", "PathPolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingSourceOwnershipBindings_WorkspaceId_ApplicationId_Status",
                table: "HealingSourceOwnershipBindings",
                columns: new[] { "WorkspaceId", "ApplicationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingSourceOwnershipBindings_WorkspaceId_ProviderConnectionId",
                table: "HealingSourceOwnershipBindings",
                columns: new[] { "WorkspaceId", "ProviderConnectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingTelemetrySources_WorkspaceId_ApplicationId_EnvironmentId_Name",
                table: "HealingTelemetrySources",
                columns: new[] { "WorkspaceId", "ApplicationId", "EnvironmentId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingTelemetrySources_WorkspaceId_ApplicationId_EnvironmentId_Status",
                table: "HealingTelemetrySources",
                columns: new[] { "WorkspaceId", "ApplicationId", "EnvironmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingVerificationResults_EpisodeId_EnvironmentId_RepairedRevision",
                table: "HealingVerificationResults",
                columns: new[] { "EpisodeId", "EnvironmentId", "RepairedRevision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingVerificationResults_WorkspaceId_ApplicationId_DeploymentObservationId",
                table: "HealingVerificationResults",
                columns: new[] { "WorkspaceId", "ApplicationId", "DeploymentObservationId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingVerificationResults_WorkspaceId_ApplicationId_EpisodeId",
                table: "HealingVerificationResults",
                columns: new[] { "WorkspaceId", "ApplicationId", "EpisodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingVerificationResults_WorkspaceId_ApplicationId_SupportingOccurrenceId",
                table: "HealingVerificationResults",
                columns: new[] { "WorkspaceId", "ApplicationId", "SupportingOccurrenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingWorkloadIdentityExchanges_AttemptId",
                table: "HealingWorkloadIdentityExchanges",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_HealingWorkloadIdentityExchanges_JwtId",
                table: "HealingWorkloadIdentityExchanges",
                column: "JwtId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingWorkloadIdentityExchanges_NonceHash",
                table: "HealingWorkloadIdentityExchanges",
                column: "NonceHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealingWorkloadIdentityExchanges_WorkspaceId_ApplicationId_AttemptId",
                table: "HealingWorkloadIdentityExchanges",
                columns: new[] { "WorkspaceId", "ApplicationId", "AttemptId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealingWorkspaceConfigurations_WorkspaceId",
                table: "HealingWorkspaceConfigurations",
                column: "WorkspaceId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_HealingComponentAttributions_HealingIncidentOccurrences_WorkspaceId_ApplicationId_OccurrenceId",
                table: "HealingComponentAttributions",
                columns: new[] { "WorkspaceId", "ApplicationId", "OccurrenceId" },
                principalTable: "HealingIncidentOccurrences",
                principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HealingEnvironmentImpacts_HealingIncidentEpisodes_WorkspaceId_ApplicationId_EpisodeId",
                table: "HealingEnvironmentImpacts",
                columns: new[] { "WorkspaceId", "ApplicationId", "EpisodeId" },
                principalTable: "HealingIncidentEpisodes",
                principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HealingEvidenceAccessDecisions_HealingEvidenceBundles_WorkspaceId_ApplicationId_ReleasedBundleId",
                table: "HealingEvidenceAccessDecisions",
                columns: new[] { "WorkspaceId", "ApplicationId", "ReleasedBundleId" },
                principalTable: "HealingEvidenceBundles",
                principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HealingEvidenceAccessDecisions_HealingIncidents_WorkspaceId_ApplicationId_IncidentId",
                table: "HealingEvidenceAccessDecisions",
                columns: new[] { "WorkspaceId", "ApplicationId", "IncidentId" },
                principalTable: "HealingIncidents",
                principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HealingEvidenceBundles_HealingIncidents_WorkspaceId_ApplicationId_IncidentId",
                table: "HealingEvidenceBundles",
                columns: new[] { "WorkspaceId", "ApplicationId", "IncidentId" },
                principalTable: "HealingIncidents",
                principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HealingHumanCommands_HealingIncidents_WorkspaceId_ApplicationId_IncidentId",
                table: "HealingHumanCommands",
                columns: new[] { "WorkspaceId", "ApplicationId", "IncidentId" },
                principalTable: "HealingIncidents",
                principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HealingIncidentEpisodes_HealingIncidents_WorkspaceId_ApplicationId_IncidentId",
                table: "HealingIncidentEpisodes",
                columns: new[] { "WorkspaceId", "ApplicationId", "IncidentId" },
                principalTable: "HealingIncidents",
                principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HealingIncidentOccurrences_HealingIncidents_WorkspaceId_ApplicationId_IncidentId",
                table: "HealingIncidentOccurrences",
                columns: new[] { "WorkspaceId", "ApplicationId", "IncidentId" },
                principalTable: "HealingIncidents",
                principalColumns: new[] { "WorkspaceId", "ApplicationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HealingIncidents_HealingRepairWorkItemProjections_WorkspaceId_ApplicationId_Id_WorkItemProjectionId",
                table: "HealingIncidents",
                columns: new[] { "WorkspaceId", "ApplicationId", "Id", "WorkItemProjectionId" },
                principalTable: "HealingRepairWorkItemProjections",
                principalColumns: new[] { "WorkspaceId", "ApplicationId", "IncidentId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER [TR_HealingAuditEvents_BlockMutation]
                ON [HealingAuditEvents]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    THROW 51000, 'Healing audit events are append-only', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_HealingAuditEvents_BlockMutation];");

            migrationBuilder.DropForeignKey(
                name: "FK_HealingIncidents_HealingComponentManifestEntries_WorkspaceId_ApplicationId_SelectedComponentEntryId",
                table: "HealingIncidents");

            migrationBuilder.DropForeignKey(
                name: "FK_HealingIncidents_HealingSourceOwnershipBindings_WorkspaceId_ApplicationId_SelectedBindingId",
                table: "HealingIncidents");

            migrationBuilder.DropForeignKey(
                name: "FK_HealingIncidents_HealingIncidentEpisodes_WorkspaceId_ApplicationId_Id_ActiveEpisodeId",
                table: "HealingIncidents");

            migrationBuilder.DropForeignKey(
                name: "FK_HealingRepairWorkItemProjections_HealingIncidentEpisodes_WorkspaceId_ApplicationId_EpisodeId",
                table: "HealingRepairWorkItemProjections");

            migrationBuilder.DropForeignKey(
                name: "FK_HealingRepairWorkItemProjections_HealingIncidents_WorkspaceId_ApplicationId_IncidentId",
                table: "HealingRepairWorkItemProjections");

            migrationBuilder.DropTable(
                name: "HealingAuditEvents");

            migrationBuilder.DropTable(
                name: "HealingComponentAttributions");

            migrationBuilder.DropTable(
                name: "HealingComponentDependencies");

            migrationBuilder.DropTable(
                name: "HealingComponentManifestAssemblies");

            migrationBuilder.DropTable(
                name: "HealingComponentManifestRegistrations");

            migrationBuilder.DropTable(
                name: "HealingEnvironmentConfigurations");

            migrationBuilder.DropTable(
                name: "HealingEnvironmentImpacts");

            migrationBuilder.DropTable(
                name: "HealingEvidenceAccessDecisions");

            migrationBuilder.DropTable(
                name: "HealingHumanCommands");

            migrationBuilder.DropTable(
                name: "HealingProviderOperations");

            migrationBuilder.DropTable(
                name: "HealingProviderWebhookDeliveries");

            migrationBuilder.DropTable(
                name: "HealingRepairPullRequests");

            migrationBuilder.DropTable(
                name: "HealingRepairResults");

            migrationBuilder.DropTable(
                name: "HealingTelemetrySources");

            migrationBuilder.DropTable(
                name: "HealingVerificationResults");

            migrationBuilder.DropTable(
                name: "HealingWorkloadIdentityExchanges");

            migrationBuilder.DropTable(
                name: "HealingWorkspaceConfigurations");

            migrationBuilder.DropTable(
                name: "HealingConfigurations");

            migrationBuilder.DropTable(
                name: "HealingPolicyEvaluations");

            migrationBuilder.DropTable(
                name: "HealingDeploymentObservations");

            migrationBuilder.DropTable(
                name: "HealingIncidentOccurrences");

            migrationBuilder.DropTable(
                name: "HealingRepairAttempts");

            migrationBuilder.DropTable(
                name: "HealingSignalInboxItems");

            migrationBuilder.DropTable(
                name: "HealingEvidenceBundles");

            migrationBuilder.DropTable(
                name: "HealingComponentManifestEntries");

            migrationBuilder.DropTable(
                name: "HealingComponentManifests");

            migrationBuilder.DropTable(
                name: "HealingSourceOwnershipBindings");

            migrationBuilder.DropTable(
                name: "HealingPolicies");

            migrationBuilder.DropTable(
                name: "HealingIncidentEpisodes");

            migrationBuilder.DropTable(
                name: "HealingIncidents");

            migrationBuilder.DropTable(
                name: "HealingRepairWorkItemProjections");

            migrationBuilder.DropTable(
                name: "HealingProviderConnections");
        }
    }
}
