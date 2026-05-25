using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LEAVE_V1_InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LEAVE_V1_leave_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeaveType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Days = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CertFileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ArchiveNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ManagerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ManagerApproved = table.Column<bool>(type: "INTEGER", nullable: true),
                    ManagerComment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ManagerDecisionAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VpUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VpApproved = table.Column<bool>(type: "INTEGER", nullable: true),
                    VpComment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    VpDecisionAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HrUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    HrArchivedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LEAVE_V1_leave_case", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LEAVE_V1_leave_case_CurrentAssigneeUserId",
                table: "LEAVE_V1_leave_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LEAVE_V1_leave_case_Status_LastActivityAt",
                table: "LEAVE_V1_leave_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LEAVE_V1_leave_case_SubmitterUserId",
                table: "LEAVE_V1_leave_case",
                column: "SubmitterUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LEAVE_V1_leave_case");
        }
    }
}
