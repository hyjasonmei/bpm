using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropModelAProcessRuntimeTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskHistory");

            migrationBuilder.DropTable(
                name: "ProcessTasks");

            migrationBuilder.DropTable(
                name: "ProcessInstances");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CancelReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentFormDataJson = table.Column<string>(type: "TEXT", nullable: false),
                    InitiatorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SpecCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SpecSnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    SpecVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    TenantCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessInstances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActualAssigneeUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CandidateSetJson = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    Decision = table.Column<int>(type: "INTEGER", nullable: true),
                    DueAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FormDataPatchJson = table.Column<string>(type: "TEXT", nullable: true),
                    ImpersonatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NodeId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NodeKind = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalAssigneeUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SandboxActualActor = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    TenantCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessTasks_ProcessInstances_ProcessInstanceId",
                        column: x => x.ProcessInstanceId,
                        principalTable: "ProcessInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    ImpersonatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    SandboxActualActor = table.Column<Guid>(type: "TEXT", nullable: true),
                    TenantCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskHistory_ProcessInstances_ProcessInstanceId",
                        column: x => x.ProcessInstanceId,
                        principalTable: "ProcessInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskHistory_ProcessTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "ProcessTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessInstances_InitiatorUserId_StartedAt",
                table: "ProcessInstances",
                columns: new[] { "InitiatorUserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessInstances_SpecCode_SpecVersion",
                table: "ProcessInstances",
                columns: new[] { "SpecCode", "SpecVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessInstances_TenantCode_Status_LastActivityAt",
                table: "ProcessInstances",
                columns: new[] { "TenantCode", "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessTasks_ActualAssigneeUserId_Status",
                table: "ProcessTasks",
                columns: new[] { "ActualAssigneeUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessTasks_ProcessInstanceId_NodeId",
                table: "ProcessTasks",
                columns: new[] { "ProcessInstanceId", "NodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessTasks_Status_DueAt",
                table: "ProcessTasks",
                columns: new[] { "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskHistory_EventType_CreatedAt",
                table: "TaskHistory",
                columns: new[] { "EventType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskHistory_ProcessInstanceId_CreatedAt",
                table: "TaskHistory",
                columns: new[] { "ProcessInstanceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskHistory_TaskId",
                table: "TaskHistory",
                column: "TaskId");
        }
    }
}
