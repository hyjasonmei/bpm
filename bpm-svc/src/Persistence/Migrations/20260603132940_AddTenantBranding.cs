using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FaviconDataUri",
                table: "TenantSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoDataUri",
                table: "TenantSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SystemName",
                table: "TenantSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FaviconDataUri",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "LogoDataUri",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "SystemName",
                table: "TenantSettings");
        }
    }
}
