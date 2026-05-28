using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Admin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlowChatMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastChefHeartbeatAt",
                table: "Admin_Flows",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Admin_FlowChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FlowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sender = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    ArtifactsJson = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_FlowChatMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Admin_FlowChatMessages_FlowId",
                table: "Admin_FlowChatMessages",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_FlowChatMessages_FlowId_CreatedAt",
                table: "Admin_FlowChatMessages",
                columns: new[] { "FlowId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Admin_FlowChatMessages");

            migrationBuilder.DropColumn(
                name: "LastChefHeartbeatAt",
                table: "Admin_Flows");
        }
    }
}
