using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecBundles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpecBundles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FlowCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    FlowVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ManifestChecksum = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ParentManifestChecksum = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ManifestJson = table.Column<string>(type: "TEXT", nullable: false),
                    ZipBlob = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LastReproCheckAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastReproCheckResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecBundles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpecBundles_ManifestChecksum",
                table: "SpecBundles",
                column: "ManifestChecksum",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpecBundles_Status",
                table: "SpecBundles",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SpecBundles_TenantCode_FlowCode_FlowVersion",
                table: "SpecBundles",
                columns: new[] { "TenantCode", "FlowCode", "FlowVersion" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpecBundles");
        }
    }
}
