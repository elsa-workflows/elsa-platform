using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Catalog.Persistence.SqlServerMigrations.Migrations
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
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE [SyncRuns]
                SET [CompletedAtTicks] =
                    DATEDIFF_BIG(DAY, CONVERT(datetime2, '0001-01-01T00:00:00'), CAST(SWITCHOFFSET([CompletedAt], '+00:00') AS datetime2)) * CAST(864000000000 AS bigint)
                    + DATEDIFF_BIG(
                        NANOSECOND,
                        CAST(CAST(CAST(SWITCHOFFSET([CompletedAt], '+00:00') AS datetime2) AS date) AS datetime2),
                        CAST(SWITCHOFFSET([CompletedAt], '+00:00') AS datetime2)) / 100
                WHERE [CompletedAt] IS NOT NULL;
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
                name: "CompletedAtDateTime",
                table: "SyncRuns",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE [SyncRuns]
                SET [CompletedAtDateTime] = TODATETIMEOFFSET(
                    DATEADD(
                        MILLISECOND,
                        CAST(([CompletedAt] % 10000000) / 10000 AS int),
                        DATEADD(
                            SECOND,
                            CAST(([CompletedAt] % 864000000000) / 10000000 AS int),
                            DATEADD(DAY, CAST([CompletedAt] / 864000000000 AS int), CONVERT(datetime2, '0001-01-01T00:00:00')))),
                    '+00:00')
                WHERE [CompletedAt] IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "SyncRuns");

            migrationBuilder.RenameColumn(
                name: "CompletedAtDateTime",
                table: "SyncRuns",
                newName: "CompletedAt");
        }
    }
}
