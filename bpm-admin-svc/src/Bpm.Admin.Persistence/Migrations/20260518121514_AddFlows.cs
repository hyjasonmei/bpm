using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Admin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Flows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    FlowCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SpecJson = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flows", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Flows_LineageId",
                table: "Flows",
                column: "LineageId");

            migrationBuilder.CreateIndex(
                name: "IX_Flows_LineageId_Version",
                table: "Flows",
                columns: new[] { "LineageId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Flows_State",
                table: "Flows",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_Flows_UpdatedAt",
                table: "Flows",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Flows");
        }
    }
}
