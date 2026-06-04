using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSandboxFlowScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CaseId",
                table: "SandboxCapturedMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlowCode",
                table: "SandboxCapturedMessages",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FlowSandboxConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    FlowCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CaptureEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowSandboxConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxCapturedMessages_TenantCode_FlowCode_CapturedAt",
                table: "SandboxCapturedMessages",
                columns: new[] { "TenantCode", "FlowCode", "CapturedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_FlowSandboxConfigs_TenantCode_FlowCode",
                table: "FlowSandboxConfigs",
                columns: new[] { "TenantCode", "FlowCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlowSandboxConfigs");

            migrationBuilder.DropIndex(
                name: "IX_SandboxCapturedMessages_TenantCode_FlowCode_CapturedAt",
                table: "SandboxCapturedMessages");

            migrationBuilder.DropColumn(
                name: "CaseId",
                table: "SandboxCapturedMessages");

            migrationBuilder.DropColumn(
                name: "FlowCode",
                table: "SandboxCapturedMessages");
        }
    }
}
