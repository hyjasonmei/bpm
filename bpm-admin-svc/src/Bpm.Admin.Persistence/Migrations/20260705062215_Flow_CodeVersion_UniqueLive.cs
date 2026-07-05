using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Admin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Flow_CodeVersion_UniqueLive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Admin_Flows_FlowCode_Version",
                table: "Admin_Flows",
                columns: new[] { "FlowCode", "Version" },
                unique: true,
                filter: "\"ArchivedAt\" IS NULL AND \"DeletedAt\" IS NULL AND \"State\" <> 7");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Admin_Flows_FlowCode_Version",
                table: "Admin_Flows");
        }
    }
}
