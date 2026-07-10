using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OVERTIME_V1_InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OVERTIME_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OvertimeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    EndTime = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    EstimatedHours = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    OvertimeReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentAssigneeRoleCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    MonthlyHours = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerApproved = table.Column<bool>(type: "boolean", nullable: true),
                    ManagerComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ManagerDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HrUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    HrApproved = table.Column<bool>(type: "boolean", nullable: true),
                    HrComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    HrDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OVERTIME_V1_case", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OVERTIME_V1_case_CurrentAssigneeRoleCode_LastActivityAt",
                table: "OVERTIME_V1_case",
                columns: new[] { "CurrentAssigneeRoleCode", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OVERTIME_V1_case_CurrentAssigneeUserId",
                table: "OVERTIME_V1_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OVERTIME_V1_case_Status_LastActivityAt",
                table: "OVERTIME_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OVERTIME_V1_case_SubmitterUserId",
                table: "OVERTIME_V1_case",
                column: "SubmitterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OVERTIME_V1_case_SubmitterUserId_Status_OvertimeDate",
                table: "OVERTIME_V1_case",
                columns: new[] { "SubmitterUserId", "Status", "OvertimeDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OVERTIME_V1_case");
        }
    }
}
