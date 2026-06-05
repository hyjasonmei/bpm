using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Admin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlowBpmnXml : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BpmnXml",
                table: "Admin_Flows",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BpmnXml",
                table: "Admin_Flows");
        }
    }
}
