using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageSourceSoftDeleteAndHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSuccessfulSyncAt",
                table: "PackageSources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PollingInterval",
                table: "PackageSources",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SoftDeletedAt",
                table: "PackageSources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "PackageSources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSuccessfulSyncAt",
                table: "PackageSources");

            migrationBuilder.DropColumn(
                name: "PollingInterval",
                table: "PackageSources");

            migrationBuilder.DropColumn(
                name: "SoftDeletedAt",
                table: "PackageSources");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "PackageSources");
        }
    }
}
