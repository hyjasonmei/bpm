using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTravel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TravelCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FlowCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicantUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DestinationType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Destination = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DepartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ReturnDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TicketRef = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    HotelRef = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    BookNote = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CurrentApproverUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ManagerApproverUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ManagerApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VpApproverUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    VpApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AdminBookerUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    AdminBookedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RejectedByUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RejectionReason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TravelCases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TravelCases_ApplicantUserId",
                table: "TravelCases",
                column: "ApplicantUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelCases_CurrentApproverUserId",
                table: "TravelCases",
                column: "CurrentApproverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelCases_TenantCode_State",
                table: "TravelCases",
                columns: new[] { "TenantCode", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TravelCases");
        }
    }
}
