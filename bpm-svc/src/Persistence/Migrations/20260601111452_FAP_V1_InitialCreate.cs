using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FAP_V1_InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FAP_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    purchase_items_json = table.Column<string>(type: "TEXT", nullable: false),
                    ShippingLocation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ChargeTo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ExpectedDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RoundCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ManagerApproved = table.Column<bool>(type: "INTEGER", nullable: true),
                    ManagerComment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ManagerDecisionAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PurchaseOrderNo = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    PoIssuedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VerifiedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Received = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    VerificationRemark = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FAP_V1_case", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FAP_V1_case_CurrentAssigneeUserId",
                table: "FAP_V1_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FAP_V1_case_Status_LastActivityAt",
                table: "FAP_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FAP_V1_case_SubmitterUserId",
                table: "FAP_V1_case",
                column: "SubmitterUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FAP_V1_case");
        }
    }
}
