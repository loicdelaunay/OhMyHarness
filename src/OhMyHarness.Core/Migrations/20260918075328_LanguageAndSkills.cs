using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OhMyHarness.Core.Migrations
{
    /// <inheritdoc />
    public partial class LanguageAndSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnabledSkills",
                table: "States",
                type: "TEXT",
                nullable: false,
                defaultValue: "sources,web");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "States",
                type: "TEXT",
                nullable: false,
                defaultValue: "fr");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnabledSkills",
                table: "States");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "States");
        }
    }
}
