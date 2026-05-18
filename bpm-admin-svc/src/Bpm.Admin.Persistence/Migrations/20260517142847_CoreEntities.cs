using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Admin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoreEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Delegations",
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
                    table.PrimaryKey("PK_Delegations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeptParents",
                columns: table => new
                {
                    DeptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentDeptId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeptParents", x => x.DeptId);
                });

            migrationBuilder.CreateTable(
                name: "GroupMembers",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberPrincipalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberType = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupMembers", x => new { x.GroupId, x.MemberPrincipalId });
                });

            migrationBuilder.CreateTable(
                name: "PrincipalRoles",
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
                    table.PrimaryKey("PK_PrincipalRoles", x => new { x.PrincipalId, x.RoleId });
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserDepts",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDepts", x => new { x.UserId, x.DeptId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_DelegateToUserId",
                table: "Delegations",
                column: "DelegateToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_DelegatorPrincipalId",
                table: "Delegations",
                column: "DelegatorPrincipalId");

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_DelegatorPrincipalId_Active_StartAt_EndAt",
                table: "Delegations",
                columns: new[] { "DelegatorPrincipalId", "Active", "StartAt", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeptParents_ParentDeptId",
                table: "DeptParents",
                column: "ParentDeptId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_GroupId",
                table: "GroupMembers",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_MemberPrincipalId",
                table: "GroupMembers",
                column: "MemberPrincipalId");

            migrationBuilder.CreateIndex(
                name: "IX_PrincipalRoles_PrincipalId",
                table: "PrincipalRoles",
                column: "PrincipalId");

            migrationBuilder.CreateIndex(
                name: "IX_PrincipalRoles_RoleId",
                table: "PrincipalRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDepts_DeptId",
                table: "UserDepts",
                column: "DeptId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDepts_UserId",
                table: "UserDepts",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Delegations");

            migrationBuilder.DropTable(
                name: "DeptParents");

            migrationBuilder.DropTable(
                name: "GroupMembers");

            migrationBuilder.DropTable(
                name: "PrincipalRoles");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "UserDepts");
        }
    }
}
