using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElsaControl.PackageCatalog.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddUnknownBillingEventState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "State",
                table: "BillingProviderEvents",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Unknown events intentionally persist a null lifecycle state. A
            // rollback must preserve the non-null enum-backed column contract;
            // use the fail-safe terminal state before tightening nullability.
            migrationBuilder.Sql("UPDATE [BillingProviderEvents] SET [State] = N'Suspended' WHERE [State] IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "State",
                table: "BillingProviderEvents",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Suspended",
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldNullable: true);
        }
    }
}
