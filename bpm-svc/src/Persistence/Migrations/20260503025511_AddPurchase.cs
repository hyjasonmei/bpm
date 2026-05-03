using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FlowCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicantUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Vendor = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Items = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Justification = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    QuoteFileName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    PoNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ExpectedDelivery = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ExecNote = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CurrentApproverUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ManagerApproverUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ManagerApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinanceApproverUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FinanceApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CeoApproverUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CeoApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PurchaseExecUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PurchaseExecAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RejectedByUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RejectionReason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseCases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCases_ApplicantUserId",
                table: "PurchaseCases",
                column: "ApplicantUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCases_CurrentApproverUserId",
                table: "PurchaseCases",
                column: "CurrentApproverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCases_TenantCode_State",
                table: "PurchaseCases",
                columns: new[] { "TenantCode", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseCases");
        }
    }
}
