using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSandboxMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SandboxRedirects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Channel = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalTargetsJson = table.Column<string>(type: "TEXT", nullable: false),
                    RedirectedTargetsJson = table.Column<string>(type: "TEXT", nullable: false),
                    SampleSubject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DispatchedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SandboxRedirects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SandboxMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    SandboxConfigJson = table.Column<string>(type: "TEXT", nullable: true),
                    SandboxLastToggledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SandboxLastToggledByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxRedirects_TenantCode_Channel_DispatchedAt",
                table: "SandboxRedirects",
                columns: new[] { "TenantCode", "Channel", "DispatchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxRedirects_TenantCode_DispatchedAt",
                table: "SandboxRedirects",
                columns: new[] { "TenantCode", "DispatchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSettings_TenantCode",
                table: "TenantSettings",
                column: "TenantCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SandboxRedirects");

            migrationBuilder.DropTable(
                name: "TenantSettings");
        }
    }
}
