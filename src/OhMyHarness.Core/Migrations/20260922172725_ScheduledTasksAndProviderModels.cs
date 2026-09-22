using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OhMyHarness.Core.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledTasksAndProviderModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetectedModelsJson",
                table: "Providers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SelectedModelsJson",
                table: "Providers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ScheduledTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Instruction = table.Column<string>(type: "TEXT", nullable: false),
                    Cron = table.Column<string>(type: "TEXT", nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RememberHistory = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastChatId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProviderId = table.Column<int>(type: "INTEGER", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    ContextLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    SupportsImages = table.Column<bool>(type: "INTEGER", nullable: false),
                    ThinkingLevel = table.Column<string>(type: "TEXT", nullable: false),
                    ResourcePathsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExecutionMode = table.Column<string>(type: "TEXT", nullable: false),
                    OrchestrationMode = table.Column<string>(type: "TEXT", nullable: false),
                    SandboxEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnabledSkills = table.Column<string>(type: "TEXT", nullable: false),
                    AutoContinue = table.Column<bool>(type: "INTEGER", nullable: false),
                    NextRunUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRunUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastResult = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledTasks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTasks_Enabled_NextRunUtc",
                table: "ScheduledTasks",
                columns: new[] { "Enabled", "NextRunUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTasks_ProjectId",
                table: "ScheduledTasks",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledTasks");

            migrationBuilder.DropColumn(
                name: "DetectedModelsJson",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "SelectedModelsJson",
                table: "Providers");
        }
    }
}
