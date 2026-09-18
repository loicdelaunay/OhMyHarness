using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OhMyHarness.Core.Migrations
{
    /// <inheritdoc />
    public partial class PersistentPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PermissionGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Scope = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionGrants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PermissionGrants_Scope",
                table: "PermissionGrants",
                column: "Scope",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PermissionGrants");
        }
    }
}
