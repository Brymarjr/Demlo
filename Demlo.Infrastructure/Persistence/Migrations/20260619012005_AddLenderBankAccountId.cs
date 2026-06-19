using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demlo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLenderBankAccountId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankAccountId",
                table: "lender_profiles",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "lender_profiles");
        }
    }
}
