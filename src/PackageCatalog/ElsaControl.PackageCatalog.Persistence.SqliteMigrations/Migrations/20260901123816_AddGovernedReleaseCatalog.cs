using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernedReleaseCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GovernedReleaseCatalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogIdentityHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProjectionFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ManifestReference = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ManifestDigest = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    PayloadDigest = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    SignatureEvidenceReference = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    SignatureEvidenceDigest = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    RegistryClass = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DistributionId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Generation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ReleaseLine = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ReleaseVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProducerLifecycle = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Edition = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceRepository = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    SourceCommit = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SourceRunId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CatalogLifecycle = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AdmittedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernedReleaseCatalog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GovernedReleaseCatalogTopologies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TopologyId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PackageManifestSchema = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernedReleaseCatalogTopologies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GovernedReleaseCatalogTopologies_GovernedReleaseCatalog_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "GovernedReleaseCatalog",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GovernedReleaseCatalogCapabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TopologyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Capability = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernedReleaseCatalogCapabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GovernedReleaseCatalogCapabilities_GovernedReleaseCatalogTopologies_TopologyId",
                        column: x => x.TopologyId,
                        principalTable: "GovernedReleaseCatalogTopologies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GovernedReleaseCatalogComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TopologyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComponentId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ImageReference = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ImageDigest = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    CompanionComponentId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernedReleaseCatalogComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GovernedReleaseCatalogComponents_GovernedReleaseCatalogTopologies_TopologyId",
                        column: x => x.TopologyId,
                        principalTable: "GovernedReleaseCatalogTopologies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GovernedReleaseCatalogComponentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TopologyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComponentId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernedReleaseCatalogComponentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GovernedReleaseCatalogComponentVersions_GovernedReleaseCatalogTopologies_TopologyId",
                        column: x => x.TopologyId,
                        principalTable: "GovernedReleaseCatalogTopologies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GovernedReleaseCatalogEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TopologyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Digest = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernedReleaseCatalogEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GovernedReleaseCatalogEvidence_GovernedReleaseCatalogTopologies_TopologyId",
                        column: x => x.TopologyId,
                        principalTable: "GovernedReleaseCatalogTopologies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GovernedReleaseCatalogRuntimeKinds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TopologyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuntimeKind = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernedReleaseCatalogRuntimeKinds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GovernedReleaseCatalogRuntimeKinds_GovernedReleaseCatalogTopologies_TopologyId",
                        column: x => x.TopologyId,
                        principalTable: "GovernedReleaseCatalogTopologies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GovernedReleaseCatalogComponentCapabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComponentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Capability = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernedReleaseCatalogComponentCapabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GovernedReleaseCatalogComponentCapabilities_GovernedReleaseCatalogComponents_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "GovernedReleaseCatalogComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GovernedReleaseCatalogEndpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComponentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Visibility = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RequiresTls = table.Column<bool>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernedReleaseCatalogEndpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GovernedReleaseCatalogEndpoints_GovernedReleaseCatalogComponents_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "GovernedReleaseCatalogComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GovernedReleaseCatalogPlatformDigests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComponentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Digest = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernedReleaseCatalogPlatformDigests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GovernedReleaseCatalogPlatformDigests_GovernedReleaseCatalogComponents_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "GovernedReleaseCatalogComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GovernedReleaseCatalogRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComponentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernedReleaseCatalogRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GovernedReleaseCatalogRoles_GovernedReleaseCatalogComponents_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "GovernedReleaseCatalogComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalog_CatalogIdentityHash",
                table: "GovernedReleaseCatalog",
                column: "CatalogIdentityHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalog_CatalogLifecycle_Channel_ProducerLifecycle_ReleaseLine_ReleaseVersion_Id",
                table: "GovernedReleaseCatalog",
                columns: new[] { "CatalogLifecycle", "Channel", "ProducerLifecycle", "ReleaseLine", "ReleaseVersion", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalog_DistributionId_Generation_ReleaseLine_ReleaseVersion_RegistryClass",
                table: "GovernedReleaseCatalog",
                columns: new[] { "DistributionId", "Generation", "ReleaseLine", "ReleaseVersion", "RegistryClass" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalog_ManifestDigest_RegistryClass",
                table: "GovernedReleaseCatalog",
                columns: new[] { "ManifestDigest", "RegistryClass" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalogCapabilities_Capability_TopologyId",
                table: "GovernedReleaseCatalogCapabilities",
                columns: new[] { "Capability", "TopologyId" });

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalogCapabilities_TopologyId_Capability",
                table: "GovernedReleaseCatalogCapabilities",
                columns: new[] { "TopologyId", "Capability" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalogComponentCapabilities_Capability_ComponentId",
                table: "GovernedReleaseCatalogComponentCapabilities",
                columns: new[] { "Capability", "ComponentId" });

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalogComponentCapabilities_ComponentId_Capability",
                table: "GovernedReleaseCatalogComponentCapabilities",
                columns: new[] { "ComponentId", "Capability" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalogComponents_TopologyId_ComponentId",
                table: "GovernedReleaseCatalogComponents",
                columns: new[] { "TopologyId", "ComponentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalogComponentVersions_TopologyId_ComponentId",
                table: "GovernedReleaseCatalogComponentVersions",
                columns: new[] { "TopologyId", "ComponentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalogEndpoints_ComponentId_Name",
                table: "GovernedReleaseCatalogEndpoints",
                columns: new[] { "ComponentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalogEvidence_TopologyId_Kind",
                table: "GovernedReleaseCatalogEvidence",
                columns: new[] { "TopologyId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalogPlatformDigests_ComponentId_Platform",
                table: "GovernedReleaseCatalogPlatformDigests",
                columns: new[] { "ComponentId", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalogRoles_ComponentId_Role",
                table: "GovernedReleaseCatalogRoles",
                columns: new[] { "ComponentId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalogRuntimeKinds_RuntimeKind_TopologyId",
                table: "GovernedReleaseCatalogRuntimeKinds",
                columns: new[] { "RuntimeKind", "TopologyId" });

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalogRuntimeKinds_TopologyId_RuntimeKind",
                table: "GovernedReleaseCatalogRuntimeKinds",
                columns: new[] { "TopologyId", "RuntimeKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalogTopologies_ReleaseId_TopologyId",
                table: "GovernedReleaseCatalogTopologies",
                columns: new[] { "ReleaseId", "TopologyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalogTopologies_TopologyId_ReleaseId",
                table: "GovernedReleaseCatalogTopologies",
                columns: new[] { "TopologyId", "ReleaseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GovernedReleaseCatalogCapabilities");

            migrationBuilder.DropTable(
                name: "GovernedReleaseCatalogComponentCapabilities");

            migrationBuilder.DropTable(
                name: "GovernedReleaseCatalogComponentVersions");

            migrationBuilder.DropTable(
                name: "GovernedReleaseCatalogEndpoints");

            migrationBuilder.DropTable(
                name: "GovernedReleaseCatalogEvidence");

            migrationBuilder.DropTable(
                name: "GovernedReleaseCatalogPlatformDigests");

            migrationBuilder.DropTable(
                name: "GovernedReleaseCatalogRoles");

            migrationBuilder.DropTable(
                name: "GovernedReleaseCatalogRuntimeKinds");

            migrationBuilder.DropTable(
                name: "GovernedReleaseCatalogComponents");

            migrationBuilder.DropTable(
                name: "GovernedReleaseCatalogTopologies");

            migrationBuilder.DropTable(
                name: "GovernedReleaseCatalog");
        }
    }
}
