using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Admin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagerAndDeptHead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Admin_DeptHeads",
                columns: table => new
                {
                    DeptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HeadUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_DeptHeads", x => x.DeptId);
                });

            migrationBuilder.CreateTable(
                name: "Admin_UserManagers",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_UserManagers", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Admin_DeptHeads_HeadUserId",
                table: "Admin_DeptHeads",
                column: "HeadUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_UserManagers_ManagerUserId",
                table: "Admin_UserManagers",
                column: "ManagerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Admin_DeptHeads");

            migrationBuilder.DropTable(
                name: "Admin_UserManagers");
        }
    }
}
