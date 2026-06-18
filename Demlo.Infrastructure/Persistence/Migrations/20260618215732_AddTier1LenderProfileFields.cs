using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demlo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTier1LenderProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "lender_profiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "lender_profiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OnboardingAddress",
                table: "lender_profiles",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "lender_profiles");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "lender_profiles");

            migrationBuilder.DropColumn(
                name: "OnboardingAddress",
                table: "lender_profiles");
        }
    }
}
