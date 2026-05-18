using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Admin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorPrincipalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    TargetId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: true),
                    SourceSystem = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.EventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActionType",
                table: "AuditEvents",
                column: "ActionType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_SourceSystem",
                table: "AuditEvents",
                column: "SourceSystem");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TargetType_TargetId",
                table: "AuditEvents",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Timestamp",
                table: "AuditEvents",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");
        }
    }
}
