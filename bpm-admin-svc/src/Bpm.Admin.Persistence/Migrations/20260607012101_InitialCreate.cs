using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Admin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Admin_AuditEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorPrincipalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActionType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BeforeJson = table.Column<string>(type: "text", nullable: true),
                    AfterJson = table.Column<string>(type: "text", nullable: true),
                    SourceSystem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_AuditEvents", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "Admin_Delegations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DelegatorPrincipalId = table.Column<Guid>(type: "uuid", nullable: false),
                    DelegateToUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_Delegations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Admin_DeptHeads",
                columns: table => new
                {
                    DeptId = table.Column<Guid>(type: "uuid", nullable: false),
                    HeadUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_DeptHeads", x => x.DeptId);
                });

            migrationBuilder.CreateTable(
                name: "Admin_DeptParents",
                columns: table => new
                {
                    DeptId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentDeptId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_DeptParents", x => x.DeptId);
                });

            migrationBuilder.CreateTable(
                name: "Admin_FeatureRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: true),
                    FlowCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    TableNamesJson = table.Column<string>(type: "text", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_FeatureRegistrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Admin_FlowChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sender = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ArtifactsJson = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_FlowChatMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Admin_FlowGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DisplayNameJson = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Icon = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_FlowGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Admin_Flows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LineageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    FlowCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SpecJson = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastChefHeartbeatAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    IconKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedTableNamesJson = table.Column<string>(type: "text", nullable: true),
                    ChefWorkContextJson = table.Column<string>(type: "text", nullable: true),
                    BundleBlob = table.Column<byte[]>(type: "bytea", nullable: true),
                    BundleBuiltAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BpmnXml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_Flows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Admin_GroupMembers",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberPrincipalId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberType = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_GroupMembers", x => new { x.GroupId, x.MemberPrincipalId });
                });

            migrationBuilder.CreateTable(
                name: "Admin_PrincipalRoles",
                columns: table => new
                {
                    PrincipalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    InheritToMembers = table.Column<bool>(type: "boolean", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_PrincipalRoles", x => new { x.PrincipalId, x.RoleId });
                });

            migrationBuilder.CreateTable(
                name: "Admin_Principals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_Principals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Admin_Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Admin_UserCredentials",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PasswordChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_UserCredentials", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "Admin_UserDepts",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeptId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_UserDepts", x => new { x.UserId, x.DeptId });
                });

            migrationBuilder.CreateTable(
                name: "Admin_UserManagers",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admin_UserManagers", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "Admin_UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true)
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
                name: "IX_Admin_DeptHeads_HeadUserId",
                table: "Admin_DeptHeads",
                column: "HeadUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_DeptParents_ParentDeptId",
                table: "Admin_DeptParents",
                column: "ParentDeptId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_FeatureRegistrations_FlowCode_Version",
                table: "Admin_FeatureRegistrations",
                columns: new[] { "FlowCode", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_Admin_FeatureRegistrations_FlowId",
                table: "Admin_FeatureRegistrations",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_FlowChatMessages_FlowId",
                table: "Admin_FlowChatMessages",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_FlowChatMessages_FlowId_CreatedAt",
                table: "Admin_FlowChatMessages",
                columns: new[] { "FlowId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Admin_FlowGroups_Code",
                table: "Admin_FlowGroups",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Admin_FlowGroups_SortOrder",
                table: "Admin_FlowGroups",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_Admin_Flows_GroupId",
                table: "Admin_Flows",
                column: "GroupId");

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
                name: "IX_Admin_Roles_Code",
                table: "Admin_Roles",
                column: "Code",
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
                name: "IX_Admin_UserManagers_ManagerUserId",
                table: "Admin_UserManagers",
                column: "ManagerUserId");

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
                name: "Admin_DeptHeads");

            migrationBuilder.DropTable(
                name: "Admin_DeptParents");

            migrationBuilder.DropTable(
                name: "Admin_FeatureRegistrations");

            migrationBuilder.DropTable(
                name: "Admin_FlowChatMessages");

            migrationBuilder.DropTable(
                name: "Admin_FlowGroups");

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
                name: "Admin_UserManagers");

            migrationBuilder.DropTable(
                name: "Admin_UserSessions");
        }
    }
}
