using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demlo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTier1ProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "borrower_profiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "borrower_profiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EmploymentStatus",
                table: "borrower_profiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OnboardingAddress",
                table: "borrower_profiles",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "borrower_profiles");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "borrower_profiles");

            migrationBuilder.DropColumn(
                name: "EmploymentStatus",
                table: "borrower_profiles");

            migrationBuilder.DropColumn(
                name: "OnboardingAddress",
                table: "borrower_profiles");
        }
    }
}
