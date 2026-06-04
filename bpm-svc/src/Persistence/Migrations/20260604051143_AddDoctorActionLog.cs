using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorActionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DoctorActionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FlowCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FromUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ToUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Affected = table.Column<int>(type: "INTEGER", nullable: false),
                    OperatorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorActionLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorActionLogs_CreatedAt",
                table: "DoctorActionLogs",
                column: "CreatedAt",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoctorActionLogs");
        }
    }
}
