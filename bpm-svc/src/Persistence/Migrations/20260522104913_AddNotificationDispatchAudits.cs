using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDispatchAudits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationDispatchAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SpecCode = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    NotificationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Recipient = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DispatchedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDispatchAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatchAudits_InstanceId_DispatchedAt",
                table: "NotificationDispatchAudits",
                columns: new[] { "InstanceId", "DispatchedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatchAudits_SpecCode_DispatchedAt",
                table: "NotificationDispatchAudits",
                columns: new[] { "SpecCode", "DispatchedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatchAudits_Status_DispatchedAt",
                table: "NotificationDispatchAudits",
                columns: new[] { "Status", "DispatchedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationDispatchAudits");
        }
    }
}
