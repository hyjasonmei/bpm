using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Remove_REVIEW_demo_flows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "COMMITTEE_REVIEW_V1_case");

            migrationBuilder.DropTable(
                name: "CONTRACT_REVIEW_V1_case");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "COMMITTEE_REVIEW_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COMMITTEE_REVIEW_V1_case", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CONTRACT_REVIEW_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ContractFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Counterparty = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CONTRACT_REVIEW_V1_case", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_COMMITTEE_REVIEW_V1_case_Status_LastActivityAt",
                table: "COMMITTEE_REVIEW_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_COMMITTEE_REVIEW_V1_case_SubmitterUserId",
                table: "COMMITTEE_REVIEW_V1_case",
                column: "SubmitterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_REVIEW_V1_case_Status_LastActivityAt",
                table: "CONTRACT_REVIEW_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_REVIEW_V1_case_SubmitterUserId",
                table: "CONTRACT_REVIEW_V1_case",
                column: "SubmitterUserId");
        }
    }
}
