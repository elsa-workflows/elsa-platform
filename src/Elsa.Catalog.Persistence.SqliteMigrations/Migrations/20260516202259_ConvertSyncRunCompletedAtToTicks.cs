using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Catalog.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class ConvertSyncRunCompletedAtToTicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CompletedAtTicks",
                table: "SyncRuns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "SyncRuns"
                SET "CompletedAtTicks" = CAST((julianday("CompletedAt") - 1721425.5) * 864000000000 AS INTEGER)
                WHERE "CompletedAt" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "SyncRuns");

            migrationBuilder.RenameColumn(
                name: "CompletedAtTicks",
                table: "SyncRuns",
                newName: "CompletedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAtText",
                table: "SyncRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "SyncRuns"
                SET "CompletedAtText" = strftime('%Y-%m-%d %H:%M:%f+00:00', ("CompletedAt" / 864000000000.0) + 1721425.5)
                WHERE "CompletedAt" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "SyncRuns");

            migrationBuilder.RenameColumn(
                name: "CompletedAtText",
                table: "SyncRuns",
                newName: "CompletedAt");
        }
    }
}
