using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHrFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HrFlowInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SpecCode = table.Column<int>(type: "INTEGER", nullable: false),
                    InitiatorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResolvedManagerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentStep = table.Column<int>(type: "INTEGER", nullable: false),
                    FormDataJson = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrFlowInstances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrFlowActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    FromStep = table.Column<int>(type: "INTEGER", nullable: false),
                    ToStep = table.Column<int>(type: "INTEGER", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrFlowActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HrFlowActions_HrFlowInstances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "HrFlowInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HrFlowActions_InstanceId_CreatedAt",
                table: "HrFlowActions",
                columns: new[] { "InstanceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HrFlowInstances_InitiatorUserId_LastActivityAt",
                table: "HrFlowInstances",
                columns: new[] { "InitiatorUserId", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HrFlowInstances_ResolvedManagerUserId_Status",
                table: "HrFlowInstances",
                columns: new[] { "ResolvedManagerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_HrFlowInstances_Status",
                table: "HrFlowInstances",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HrFlowActions");

            migrationBuilder.DropTable(
                name: "HrFlowInstances");
        }
    }
}
