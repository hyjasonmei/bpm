using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ParallelApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ParallelApprovalGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FlowVersion = table.Column<int>(type: "integer", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    GatewayNodeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Threshold = table.Column<int>(type: "integer", nullable: false),
                    TotalSlots = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParallelApprovalGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ParallelApprovalSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AssigneeRoleCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DecisionByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParallelApprovalSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParallelApprovalSlots_ParallelApprovalGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "ParallelApprovalGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParallelApprovalGroups_FlowCode_CaseId_GatewayNodeId",
                table: "ParallelApprovalGroups",
                columns: new[] { "FlowCode", "CaseId", "GatewayNodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ParallelApprovalSlots_AssigneeRoleCode_Decision",
                table: "ParallelApprovalSlots",
                columns: new[] { "AssigneeRoleCode", "Decision" });

            migrationBuilder.CreateIndex(
                name: "IX_ParallelApprovalSlots_AssigneeUserId_Decision",
                table: "ParallelApprovalSlots",
                columns: new[] { "AssigneeUserId", "Decision" });

            migrationBuilder.CreateIndex(
                name: "IX_ParallelApprovalSlots_GroupId",
                table: "ParallelApprovalSlots",
                column: "GroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ParallelApprovalSlots");

            migrationBuilder.DropTable(
                name: "ParallelApprovalGroups");
        }
    }
}
