using System;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    [DbContext(typeof(CatalogDbContext))]
    [Migration("20260526120000_AddEngineHealthVerification")]
    public partial class AddEngineHealthVerification : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastVerificationAt",
                table: "WorkflowEngines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationMessage",
                table: "WorkflowEngines",
                type: "TEXT",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastVerificationAt",
                table: "WorkflowEngines");

            migrationBuilder.DropColumn(
                name: "VerificationMessage",
                table: "WorkflowEngines");
        }
    }
}
