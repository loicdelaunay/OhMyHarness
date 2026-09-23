using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OhMyHarness.Core.Migrations
{
    /// <inheritdoc />
    public partial class PersistentMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Memories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Scope = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: true),
                    ChatId = table.Column<int>(type: "INTEGER", nullable: true),
                    Partition = table.Column<string>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    OriginChatId = table.Column<int>(type: "INTEGER", nullable: true),
                    Author = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Memories_Chats_ChatId",
                        column: x => x.ChatId,
                        principalTable: "Chats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Memories_Chats_OriginChatId",
                        column: x => x.OriginChatId,
                        principalTable: "Chats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Memories_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Memories_ChatId",
                table: "Memories",
                column: "ChatId");

            migrationBuilder.CreateIndex(
                name: "IX_Memories_OriginChatId",
                table: "Memories",
                column: "OriginChatId");

            migrationBuilder.CreateIndex(
                name: "IX_Memories_Partition_Category_Key",
                table: "Memories",
                columns: new[] { "Partition", "Category", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Memories_ProjectId",
                table: "Memories",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Memories_Scope_ProjectId_ChatId_UpdatedUtc",
                table: "Memories",
                columns: new[] { "Scope", "ProjectId", "ChatId", "UpdatedUtc" });
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE MemorySearch USING fts5(Key, Title, Content, Tags, content='Memories', content_rowid='Id', tokenize='unicode61 remove_diacritics 2');
                CREATE TRIGGER Memories_ai AFTER INSERT ON Memories BEGIN
                    INSERT INTO MemorySearch(rowid, Key, Title, Content, Tags) VALUES (new.Id, new.Key, new.Title, new.Content, new.Tags);
                END;
                CREATE TRIGGER Memories_ad AFTER DELETE ON Memories BEGIN
                    INSERT INTO MemorySearch(MemorySearch, rowid, Key, Title, Content, Tags) VALUES ('delete', old.Id, old.Key, old.Title, old.Content, old.Tags);
                END;
                CREATE TRIGGER Memories_au AFTER UPDATE ON Memories BEGIN
                    INSERT INTO MemorySearch(MemorySearch, rowid, Key, Title, Content, Tags) VALUES ('delete', old.Id, old.Key, old.Title, old.Content, old.Tags);
                    INSERT INTO MemorySearch(rowid, Key, Title, Content, Tags) VALUES (new.Id, new.Key, new.Title, new.Content, new.Tags);
                END;
                INSERT INTO MemorySearch(MemorySearch) VALUES ('rebuild');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Memories_ai; DROP TRIGGER IF EXISTS Memories_ad; DROP TRIGGER IF EXISTS Memories_au; DROP TABLE IF EXISTS MemorySearch;");
            migrationBuilder.DropTable(
                name: "Memories");
        }
    }
}
