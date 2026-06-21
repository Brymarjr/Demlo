using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demlo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTemporaryPasswordFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTemporaryPassword",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPasswordChangedAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTemporaryPassword",
                table: "users");

            migrationBuilder.DropColumn(
                name: "LastPasswordChangedAt",
                table: "users");
        }
    }
}
