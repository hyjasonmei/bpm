using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class COMMITTEE_REVIEW_V1_InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "COMMITTEE_REVIEW_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ReviewCategory = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApplyAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    BenefitDescription = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ExecStart = table.Column<DateOnly>(type: "date", nullable: false),
                    ExecEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    AttachmentFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Remarks = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RevisionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentRound = table.Column<int>(type: "integer", nullable: false),
                    CeoUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CeoApproved = table.Column<bool>(type: "boolean", nullable: true),
                    CeoComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CeoDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COMMITTEE_REVIEW_V1_case", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_COMMITTEE_REVIEW_V1_case_Status_LastActivityAt",
                table: "COMMITTEE_REVIEW_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_COMMITTEE_REVIEW_V1_case_SubmitterUserId",
                table: "COMMITTEE_REVIEW_V1_case",
                column: "SubmitterUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "COMMITTEE_REVIEW_V1_case");
        }
    }
}
