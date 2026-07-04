using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CONTRACT_REVIEW_V1_InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CONTRACT_REVIEW_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterpartyName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContractSubject = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    DraftFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Remarks = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RevisionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentRound = table.Column<int>(type: "integer", nullable: false),
                    LegalManagerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LegalManagerApproved = table.Column<bool>(type: "boolean", nullable: true),
                    LegalManagerComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LegalManagerDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CONTRACT_REVIEW_V1_case", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_REVIEW_V1_case_Status_LastActivityAt",
                table: "CONTRACT_REVIEW_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_REVIEW_V1_case_SubmitterUserId",
                table: "CONTRACT_REVIEW_V1_case",
                column: "SubmitterUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CONTRACT_REVIEW_V1_case");
        }
    }
}
