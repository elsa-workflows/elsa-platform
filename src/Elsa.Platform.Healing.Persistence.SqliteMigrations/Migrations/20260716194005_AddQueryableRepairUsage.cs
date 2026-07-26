using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Platform.Healing.Persistence.SqliteMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddQueryableRepairUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AgentDurationTicks",
                table: "HealingRepairAttempts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "InputUnits",
                table: "HealingRepairAttempts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "OutputUnits",
                table: "HealingRepairAttempts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "RepositoryRunDurationTicks",
                table: "HealingRepairAttempts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "RepositoryRuns",
                table: "HealingRepairAttempts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentDurationTicks",
                table: "HealingRepairAttempts");

            migrationBuilder.DropColumn(
                name: "InputUnits",
                table: "HealingRepairAttempts");

            migrationBuilder.DropColumn(
                name: "OutputUnits",
                table: "HealingRepairAttempts");

            migrationBuilder.DropColumn(
                name: "RepositoryRunDurationTicks",
                table: "HealingRepairAttempts");

            migrationBuilder.DropColumn(
                name: "RepositoryRuns",
                table: "HealingRepairAttempts");
        }
    }
}
