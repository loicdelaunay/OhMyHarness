using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OhMyHarness.Core.Migrations
{
    /// <inheritdoc />
    public partial class OpenCodeProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoStart",
                table: "Providers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ExecutablePath",
                table: "Providers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Providers",
                type: "TEXT",
                nullable: false,
                defaultValue: "openai");

            migrationBuilder.AddColumn<bool>(
                name: "OpenCodeTools",
                table: "Providers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "Providers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ExternalChatSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChatId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProviderId = table.Column<int>(type: "INTEGER", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalChatSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalChatSessions_Chats_ChatId",
                        column: x => x.ChatId,
                        principalTable: "Chats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExternalChatSessions_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalChatSessions_ChatId_ProviderId",
                table: "ExternalChatSessions",
                columns: new[] { "ChatId", "ProviderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalChatSessions_ProviderId",
                table: "ExternalChatSessions",
                column: "ProviderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalChatSessions");

            migrationBuilder.DropColumn(
                name: "AutoStart",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "ExecutablePath",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "OpenCodeTools",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "Providers");
        }
    }
}
