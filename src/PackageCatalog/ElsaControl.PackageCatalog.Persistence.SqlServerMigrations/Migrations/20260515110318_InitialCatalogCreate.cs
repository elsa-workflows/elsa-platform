using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalogCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetType = table.Column<int>(type: "int", nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Actor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PackageSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    IncludePatterns = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExcludePatterns = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApprovalPolicy = table.Column<int>(type: "int", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Trigger = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SummaryCountersJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Packages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Approved = table.Column<bool>(type: "bit", nullable: false),
                    Listed = table.Column<bool>(type: "bit", nullable: false),
                    LatestVersion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Packages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Packages_PackageSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "PackageSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PackageVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ManifestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ManifestHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    NuspecJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IndexedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidationStatus = table.Column<int>(type: "int", nullable: false),
                    ValidationErrors = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovalStatus = table.Column<int>(type: "int", nullable: false),
                    IsListed = table.Column<bool>(type: "bit", nullable: false),
                    SuspiciousChangeDetected = table.Column<bool>(type: "bit", nullable: false),
                    SuspiciousManifestHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SchemaVersion = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageVersions_Packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Features",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeatureId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TypeName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequiredCapabilitiesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DependenciesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConflictsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Advanced = table.Column<bool>(type: "bit", nullable: false),
                    Experimental = table.Column<bool>(type: "bit", nullable: false),
                    ExtensionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Features", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Features_PackageVersions_PackageVersionId",
                        column: x => x.PackageVersionId,
                        principalTable: "PackageVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ManifestValidationResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErrorsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WarningsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidatorVersion = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManifestValidationResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManifestValidationResults_PackageVersions_PackageVersionId",
                        column: x => x.PackageVersionId,
                        principalTable: "PackageVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncRunItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SyncRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PackageId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PackageVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WarningsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRunItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncRunItems_PackageVersions_PackageVersionId",
                        column: x => x.PackageVersionId,
                        principalTable: "PackageVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SyncRunItems_SyncRuns_SyncRunId",
                        column: x => x.SyncRunId,
                        principalTable: "SyncRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FeatureSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeatureRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ClrType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JsonType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    DefaultValueJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValidationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Secret = table.Column<bool>(type: "bit", nullable: false),
                    RestartRequired = table.Column<bool>(type: "bit", nullable: false),
                    EnvironmentVariable = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UiJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExtensionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeatureSettings_Features_FeatureRecordId",
                        column: x => x.FeatureRecordId,
                        principalTable: "Features",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Features_PackageVersionId_FeatureId",
                table: "Features",
                columns: new[] { "PackageVersionId", "FeatureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeatureSettings_FeatureRecordId_Name",
                table: "FeatureSettings",
                columns: new[] { "FeatureRecordId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManifestValidationResults_PackageVersionId",
                table: "ManifestValidationResults",
                column: "PackageVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Packages_SourceId_PackageId",
                table: "Packages",
                columns: new[] { "SourceId", "PackageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackageVersions_PackageId_Version",
                table: "PackageVersions",
                columns: new[] { "PackageId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncRunItems_PackageVersionId",
                table: "SyncRunItems",
                column: "PackageVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncRunItems_SyncRunId",
                table: "SyncRunItems",
                column: "SyncRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalRecords");

            migrationBuilder.DropTable(
                name: "FeatureSettings");

            migrationBuilder.DropTable(
                name: "ManifestValidationResults");

            migrationBuilder.DropTable(
                name: "SyncRunItems");

            migrationBuilder.DropTable(
                name: "Features");

            migrationBuilder.DropTable(
                name: "SyncRuns");

            migrationBuilder.DropTable(
                name: "PackageVersions");

            migrationBuilder.DropTable(
                name: "Packages");

            migrationBuilder.DropTable(
                name: "PackageSources");
        }
    }
}
