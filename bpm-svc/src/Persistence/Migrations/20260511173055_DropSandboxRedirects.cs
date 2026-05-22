using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <summary>
    /// PR-J6 §13.3 — drop the legacy <c>SandboxRedirects</c> table now that
    /// <c>OutboundGate</c> writes capture rows only and no controller surfaces
    /// the legacy redirect log. The Mailbox UI (<c>SandboxCapturedMessages</c>)
    /// has been the sole consumer for one release; this cleanup removes the
    /// table outright.
    /// </summary>
    /// <inheritdoc />
    public partial class DropSandboxRedirects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SandboxRedirects");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SandboxRedirects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    Channel = table.Column<int>(type: "INTEGER", nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OriginalTargetsJson = table.Column<string>(type: "TEXT", nullable: false),
                    RedirectedTargetsJson = table.Column<string>(type: "TEXT", nullable: false),
                    SampleSubject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SandboxRedirects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxRedirects_TenantCode_Channel_DispatchedAt",
                table: "SandboxRedirects",
                columns: new[] { "TenantCode", "Channel", "DispatchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxRedirects_TenantCode_DispatchedAt",
                table: "SandboxRedirects",
                columns: new[] { "TenantCode", "DispatchedAt" });
        }
    }
}
