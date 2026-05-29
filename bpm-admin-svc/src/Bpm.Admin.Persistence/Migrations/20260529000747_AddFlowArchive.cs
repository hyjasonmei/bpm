using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Admin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlowArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Admin_Flows",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedTableNamesJson",
                table: "Admin_Flows",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Admin_FeatureRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FlowId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FlowCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    TableNamesJson = table.Column<string>(type: "TEXT", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_FeatureRegistrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Admin_FeatureRegistrations_FlowCode_Version",
                table: "Admin_FeatureRegistrations",
                columns: new[] { "FlowCode", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_Admin_FeatureRegistrations_FlowId",
                table: "Admin_FeatureRegistrations",
                column: "FlowId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Admin_FeatureRegistrations");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Admin_Flows");

            migrationBuilder.DropColumn(
                name: "ArchivedTableNamesJson",
                table: "Admin_Flows");
        }
    }
}
