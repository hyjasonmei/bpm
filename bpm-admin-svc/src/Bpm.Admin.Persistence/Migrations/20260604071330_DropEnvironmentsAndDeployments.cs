using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Admin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropEnvironmentsAndDeployments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Admin_Environments");

            migrationBuilder.DropTable(
                name: "Admin_FlowDeployments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Admin_Environments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_Environments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Admin_FlowDeployments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeployedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeployedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FlowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_FlowDeployments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Environments_Code",
                table: "Admin_Environments",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Environments_SortOrder",
                table: "Admin_Environments",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_FlowDeployments_FlowId",
                table: "Admin_FlowDeployments",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_FlowDeployments_FlowId_EnvironmentId",
                table: "Admin_FlowDeployments",
                columns: new[] { "FlowId", "EnvironmentId" },
                unique: true);
        }
    }
}
