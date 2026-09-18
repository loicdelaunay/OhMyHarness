using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OhMyHarness.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddThinkingLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThinkingLevel",
                table: "States",
                type: "TEXT",
                nullable: false,
                defaultValue: "auto");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThinkingLevel",
                table: "States");
        }
    }
}
