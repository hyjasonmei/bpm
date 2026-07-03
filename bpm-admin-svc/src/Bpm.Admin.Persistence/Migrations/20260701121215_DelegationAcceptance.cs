using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Admin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DelegationAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RespondedAt",
                table: "Admin_Delegations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Admin_Delegations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Delegations_DelegateToUserId_Active_Status",
                table: "Admin_Delegations",
                columns: new[] { "DelegateToUserId", "Active", "Status" });

            // Backfill: delegations that already existed were implicitly in effect,
            // so mark active ones Accepted (1) — don't silently break them by leaving
            // them Pending. New delegations (post-deploy) start Pending as designed.
            migrationBuilder.Sql(
                "UPDATE \"Admin_Delegations\" SET \"Status\" = 1 WHERE \"Active\" = TRUE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Admin_Delegations_DelegateToUserId_Active_Status",
                table: "Admin_Delegations");

            migrationBuilder.DropColumn(
                name: "RespondedAt",
                table: "Admin_Delegations");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Admin_Delegations");
        }
    }
}
