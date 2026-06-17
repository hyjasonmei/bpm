using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Admin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeployConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Admin_DeployEnvConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResourceGroup = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BpmSvcApp = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AdminSvcApp = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BpmUiSwa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AdminUiSwa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_DeployEnvConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Admin_DeployEnvConfigs_EnvName",
                table: "Admin_DeployEnvConfigs",
                column: "EnvName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Admin_DeployEnvConfigs");
        }
    }
}
