using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StripeTerminalBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeConnectToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "charges_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "onboarding_complete",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "payouts_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "stripe_account_id",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "charges_enabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "onboarding_complete",
                table: "users");

            migrationBuilder.DropColumn(
                name: "payouts_enabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "stripe_account_id",
                table: "users");
        }
    }
}
