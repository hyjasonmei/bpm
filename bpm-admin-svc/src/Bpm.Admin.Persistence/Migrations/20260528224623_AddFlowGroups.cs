using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Admin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlowGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "Admin_Flows",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Admin_FlowGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DisplayNameJson = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_FlowGroups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Flows_GroupId",
                table: "Admin_Flows",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_FlowGroups_Code",
                table: "Admin_FlowGroups",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Admin_FlowGroups_SortOrder",
                table: "Admin_FlowGroups",
                column: "SortOrder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Admin_FlowGroups");

            migrationBuilder.DropIndex(
                name: "IX_Admin_Flows_GroupId",
                table: "Admin_Flows");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "Admin_Flows");
        }
    }
}
