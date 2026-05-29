using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PURCHASE_REQUEST_V1_InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PURCHASE_REQUEST_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmitterComment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    invoices_json = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RoundCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DeptHeadUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeptHeadApproved = table.Column<bool>(type: "INTEGER", nullable: true),
                    DeptHeadComment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DeptHeadDecisionAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinanceUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FinanceApproved = table.Column<bool>(type: "INTEGER", nullable: true),
                    FinanceComment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    FinanceDecisionAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_REQUEST_V1_case", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_REQUEST_V1_case_CurrentAssigneeUserId",
                table: "PURCHASE_REQUEST_V1_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_REQUEST_V1_case_Status_LastActivityAt",
                table: "PURCHASE_REQUEST_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_REQUEST_V1_case_SubmitterUserId",
                table: "PURCHASE_REQUEST_V1_case",
                column: "SubmitterUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_REQUEST_V1_case");
        }
    }
}
