using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ETM_V1_InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ETM_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EmployeeId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LastWorkingDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ProvideCertificate = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    OutstandingPayment = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    return_items_json = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RoundCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ManagerApproved = table.Column<bool>(type: "INTEGER", nullable: true),
                    ManagerComment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ManagerDecisionAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HandoverByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    HandoverAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ETM_V1_case", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ETM_V1_case_CurrentAssigneeUserId",
                table: "ETM_V1_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ETM_V1_case_Status_LastActivityAt",
                table: "ETM_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ETM_V1_case_SubmitterUserId",
                table: "ETM_V1_case",
                column: "SubmitterUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ETM_V1_case");
        }
    }
}
