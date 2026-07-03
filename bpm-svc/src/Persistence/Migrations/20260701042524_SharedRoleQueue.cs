using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SharedRoleQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentAssigneeRoleCode",
                table: "VENDOR_EXPENSE_V1_case",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentAssigneeRoleCode",
                table: "TEO_V1_case",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentAssigneeRoleCode",
                table: "PURCHASE_REQUEST_V1_case",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentAssigneeRoleCode",
                table: "LEAVE_V1_leave_case",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VENDOR_EXPENSE_V1_case_CurrentAssigneeRoleCode_LastActivity~",
                table: "VENDOR_EXPENSE_V1_case",
                columns: new[] { "CurrentAssigneeRoleCode", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TEO_V1_case_CurrentAssigneeRoleCode_LastActivityAt",
                table: "TEO_V1_case",
                columns: new[] { "CurrentAssigneeRoleCode", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_REQUEST_V1_case_CurrentAssigneeRoleCode_LastActivi~",
                table: "PURCHASE_REQUEST_V1_case",
                columns: new[] { "CurrentAssigneeRoleCode", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LEAVE_V1_leave_case_CurrentAssigneeRoleCode_LastActivityAt",
                table: "LEAVE_V1_leave_case",
                columns: new[] { "CurrentAssigneeRoleCode", "LastActivityAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VENDOR_EXPENSE_V1_case_CurrentAssigneeRoleCode_LastActivity~",
                table: "VENDOR_EXPENSE_V1_case");

            migrationBuilder.DropIndex(
                name: "IX_TEO_V1_case_CurrentAssigneeRoleCode_LastActivityAt",
                table: "TEO_V1_case");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_REQUEST_V1_case_CurrentAssigneeRoleCode_LastActivi~",
                table: "PURCHASE_REQUEST_V1_case");

            migrationBuilder.DropIndex(
                name: "IX_LEAVE_V1_leave_case_CurrentAssigneeRoleCode_LastActivityAt",
                table: "LEAVE_V1_leave_case");

            migrationBuilder.DropColumn(
                name: "CurrentAssigneeRoleCode",
                table: "VENDOR_EXPENSE_V1_case");

            migrationBuilder.DropColumn(
                name: "CurrentAssigneeRoleCode",
                table: "TEO_V1_case");

            migrationBuilder.DropColumn(
                name: "CurrentAssigneeRoleCode",
                table: "PURCHASE_REQUEST_V1_case");

            migrationBuilder.DropColumn(
                name: "CurrentAssigneeRoleCode",
                table: "LEAVE_V1_leave_case");
        }
    }
}
