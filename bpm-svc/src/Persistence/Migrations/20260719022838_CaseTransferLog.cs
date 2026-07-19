using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CaseTransferLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaseTransferLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FlowVersion = table.Column<int>(type: "integer", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseTransferLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseTransferLogs_CaseId",
                table: "CaseTransferLogs",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseTransferLogs_CreatedAt",
                table: "CaseTransferLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CaseTransferLogs_OperatorUserId",
                table: "CaseTransferLogs",
                column: "OperatorUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseTransferLogs");
        }
    }
}
