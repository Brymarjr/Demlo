using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demlo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaystackColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "transactions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "transactions",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Reference",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "transactions");
        }
    }
}
