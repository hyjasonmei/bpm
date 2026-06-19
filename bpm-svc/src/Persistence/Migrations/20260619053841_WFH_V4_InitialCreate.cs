using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WFH_V4_InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WFH_V4_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplyDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Days = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AttachmentFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoundCount = table.Column<int>(type: "integer", nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerApproved = table.Column<bool>(type: "boolean", nullable: true),
                    ManagerComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ManagerDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SeniorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SeniorApproved = table.Column<bool>(type: "boolean", nullable: true),
                    SeniorComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SeniorDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WFH_V4_case", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WFH_V4_case_CurrentAssigneeUserId",
                table: "WFH_V4_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WFH_V4_case_Status_LastActivityAt",
                table: "WFH_V4_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WFH_V4_case_SubmitterUserId",
                table: "WFH_V4_case",
                column: "SubmitterUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WFH_V4_case");
        }
    }
}
