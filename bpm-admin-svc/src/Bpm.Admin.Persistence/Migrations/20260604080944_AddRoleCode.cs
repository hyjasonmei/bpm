using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Admin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Admin_Roles_Name",
                table: "Admin_Roles");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Admin_Roles",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Backfill Code (SCREAMING_SNAKE identifier) + a meaningful zh Name
            // from the legacy Name on existing rows, so the unique Code index
            // below doesn't collide on empty strings.
            void Map(string oldName, string code, string name) =>
                migrationBuilder.Sql($"UPDATE \"Admin_Roles\" SET \"Code\" = '{code}', \"Name\" = '{name}' WHERE \"Name\" = '{oldName}';");
            Map("Approver", "APPROVER", "簽核者");
            Map("Submitter", "SUBMITTER", "申請人");
            Map("Reviewer", "REVIEWER", "審查者");
            Map("Director", "DIRECTOR", "總監");
            Map("CEO", "CEO", "執行長");
            Map("CFO", "CFO", "財務長");
            Map("HR_Manager", "HR_MANAGER", "人資主管");
            Map("Procurement", "PROCUREMENT", "採購");
            Map("Finance", "FINANCE", "財務");
            Map("Auditor", "AUDITOR", "稽核");
            Map("FlowOwner", "FLOW_OWNER", "流程負責人");
            Map("SystemAdmin", "SYSTEM_ADMIN", "系統管理員");
            Map("Persona_Switch", "PERSONA_SWITCH", "Persona 切換權限");
            Map("Watcher", "WATCHER", "關注者");
            // Any role not in the map above (custom-created): seed Code from its
            // Name upper-cased so the unique index still holds.
            migrationBuilder.Sql("UPDATE \"Admin_Roles\" SET \"Code\" = UPPER(\"Name\") WHERE \"Code\" = '';");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Roles_Code",
                table: "Admin_Roles",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Admin_Roles_Code",
                table: "Admin_Roles");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Admin_Roles");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Roles_Name",
                table: "Admin_Roles",
                column: "Name",
                unique: true);
        }
    }
}
