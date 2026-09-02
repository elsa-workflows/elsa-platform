using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class PersistGovernedReleasePackageDeclarations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ComponentDeclarationsDigest",
                table: "GovernedReleaseCatalog",
                type: "TEXT",
                maxLength: 71,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComponentDeclarationsFormat",
                table: "GovernedReleaseCatalog",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GovernedReleaseCatalogPackageDeclarations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PackageId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernedReleaseCatalogPackageDeclarations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GovernedReleaseCatalogPackageDeclarations_GovernedReleaseCatalog_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "GovernedReleaseCatalog",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GovernedReleaseCatalogPackageDeclarations_ReleaseId_PackageId",
                table: "GovernedReleaseCatalogPackageDeclarations",
                columns: new[] { "ReleaseId", "PackageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GovernedReleaseCatalogPackageDeclarations");

            migrationBuilder.DropColumn(
                name: "ComponentDeclarationsDigest",
                table: "GovernedReleaseCatalog");

            migrationBuilder.DropColumn(
                name: "ComponentDeclarationsFormat",
                table: "GovernedReleaseCatalog");
        }
    }
}
