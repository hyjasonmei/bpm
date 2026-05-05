using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActorResolutionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActorResolutionAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RequestId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorRefJson = table.Column<string>(type: "TEXT", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FlowCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StepCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ResultKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ResolvedUserIdsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorKind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ErrorReason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActorResolutionAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActorResolutionAudits_SubmitterUserId",
                table: "ActorResolutionAudits",
                column: "SubmitterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActorResolutionAudits_Timestamp",
                table: "ActorResolutionAudits",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActorResolutionAudits");
        }
    }
}
