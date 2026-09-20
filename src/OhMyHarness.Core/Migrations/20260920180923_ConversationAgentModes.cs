using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OhMyHarness.Core.Migrations
{
    /// <inheritdoc />
    public partial class ConversationAgentModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExecutionMode",
                table: "Chats",
                type: "TEXT",
                nullable: false,
                defaultValue: "execute");

            migrationBuilder.AddColumn<string>(
                name: "OrchestrationMode",
                table: "Chats",
                type: "TEXT",
                nullable: false,
                defaultValue: "disabled");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecutionMode",
                table: "Chats");

            migrationBuilder.DropColumn(
                name: "OrchestrationMode",
                table: "Chats");
        }
    }
}
