using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EOB_V1_InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EOB_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BusinessTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EmployeeLocation = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    OnboardDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RequireMailbox = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    CostCenter = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContractNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ContractEffectiveDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ContractExpirationDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    setup_tasks_json = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RoundCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ManagerApproved = table.Column<bool>(type: "INTEGER", nullable: true),
                    ManagerComment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ManagerDecisionAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SetupByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SetupAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EOB_V1_case", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EOB_V1_case_CurrentAssigneeUserId",
                table: "EOB_V1_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EOB_V1_case_Status_LastActivityAt",
                table: "EOB_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EOB_V1_case_SubmitterUserId",
                table: "EOB_V1_case",
                column: "SubmitterUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EOB_V1_case");
        }
    }
}
