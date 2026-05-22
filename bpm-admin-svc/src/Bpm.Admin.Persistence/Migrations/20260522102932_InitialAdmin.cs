using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Admin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Admin_AuditEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorPrincipalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    TargetId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: true),
                    SourceSystem = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_AuditEvents", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "Admin_Delegations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DelegatorPrincipalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DelegateToUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_Delegations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Admin_DeptParents",
                columns: table => new
                {
                    DeptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentDeptId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_DeptParents", x => x.DeptId);
                });

            migrationBuilder.CreateTable(
                name: "Admin_Flows",
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
                    table.PrimaryKey("PK_Admin_Flows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Admin_GroupMembers",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberPrincipalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberType = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_GroupMembers", x => new { x.GroupId, x.MemberPrincipalId });
                });

            migrationBuilder.CreateTable(
                name: "Admin_PrincipalRoles",
                columns: table => new
                {
                    PrincipalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InheritToMembers = table.Column<bool>(type: "INTEGER", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_PrincipalRoles", x => new { x.PrincipalId, x.RoleId });
                });

            migrationBuilder.CreateTable(
                name: "Admin_Principals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_Principals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Admin_Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Admin_UserCredentials",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PasswordChangedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_UserCredentials", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "Admin_UserDepts",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_UserDepts", x => new { x.UserId, x.DeptId });
                });

            migrationBuilder.CreateTable(
                name: "Admin_UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_UserSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Admin_AuditEvents_ActionType",
                table: "Admin_AuditEvents",
                column: "ActionType");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_AuditEvents_SourceSystem",
                table: "Admin_AuditEvents",
                column: "SourceSystem");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_AuditEvents_TargetType_TargetId",
                table: "Admin_AuditEvents",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_Admin_AuditEvents_Timestamp",
                table: "Admin_AuditEvents",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Delegations_DelegateToUserId",
                table: "Admin_Delegations",
                column: "DelegateToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Delegations_DelegatorPrincipalId",
                table: "Admin_Delegations",
                column: "DelegatorPrincipalId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Delegations_DelegatorPrincipalId_Active_StartAt_EndAt",
                table: "Admin_Delegations",
                columns: new[] { "DelegatorPrincipalId", "Active", "StartAt", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Admin_DeptParents_ParentDeptId",
                table: "Admin_DeptParents",
                column: "ParentDeptId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Flows_LineageId",
                table: "Admin_Flows",
                column: "LineageId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Flows_LineageId_Version",
                table: "Admin_Flows",
                columns: new[] { "LineageId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Flows_State",
                table: "Admin_Flows",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Flows_UpdatedAt",
                table: "Admin_Flows",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_GroupMembers_GroupId",
                table: "Admin_GroupMembers",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_GroupMembers_MemberPrincipalId",
                table: "Admin_GroupMembers",
                column: "MemberPrincipalId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_PrincipalRoles_PrincipalId",
                table: "Admin_PrincipalRoles",
                column: "PrincipalId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_PrincipalRoles_RoleId",
                table: "Admin_PrincipalRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Principals_Email",
                table: "Admin_Principals",
                column: "Email",
                filter: "\"Email\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Principals_Type",
                table: "Admin_Principals",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Roles_Name",
                table: "Admin_Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Admin_UserDepts_DeptId",
                table: "Admin_UserDepts",
                column: "DeptId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_UserDepts_UserId",
                table: "Admin_UserDepts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_UserSessions_ExpiresAt",
                table: "Admin_UserSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_UserSessions_UserId",
                table: "Admin_UserSessions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Admin_AuditEvents");

            migrationBuilder.DropTable(
                name: "Admin_Delegations");

            migrationBuilder.DropTable(
                name: "Admin_DeptParents");

            migrationBuilder.DropTable(
                name: "Admin_Flows");

            migrationBuilder.DropTable(
                name: "Admin_GroupMembers");

            migrationBuilder.DropTable(
                name: "Admin_PrincipalRoles");

            migrationBuilder.DropTable(
                name: "Admin_Principals");

            migrationBuilder.DropTable(
                name: "Admin_Roles");

            migrationBuilder.DropTable(
                name: "Admin_UserCredentials");

            migrationBuilder.DropTable(
                name: "Admin_UserDepts");

            migrationBuilder.DropTable(
                name: "Admin_UserSessions");
        }
    }
}
