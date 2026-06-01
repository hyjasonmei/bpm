using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VENDOR_EXPENSE_V1_InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VENDOR_EXPENSE_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Vendor = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    SubmitterComment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    invoices_json = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RoundCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SupervisorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SupervisorApproved = table.Column<bool>(type: "INTEGER", nullable: true),
                    SupervisorComment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SupervisorDecisionAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProcurementUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProcurementApproved = table.Column<bool>(type: "INTEGER", nullable: true),
                    ProcurementComment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ProcurementDecisionAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SignUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SignApproved = table.Column<bool>(type: "INTEGER", nullable: true),
                    SignComment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SignDecisionAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VENDOR_EXPENSE_V1_case", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VENDOR_EXPENSE_V1_case_CurrentAssigneeUserId",
                table: "VENDOR_EXPENSE_V1_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VENDOR_EXPENSE_V1_case_Status_LastActivityAt",
                table: "VENDOR_EXPENSE_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VENDOR_EXPENSE_V1_case_SubmitterUserId",
                table: "VENDOR_EXPENSE_V1_case",
                column: "SubmitterUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VENDOR_EXPENSE_V1_case");
        }
    }
}
