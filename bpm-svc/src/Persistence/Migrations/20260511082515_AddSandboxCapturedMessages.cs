using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSandboxCapturedMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SandboxCapturedMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProcessInstanceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Channel = table.Column<int>(type: "INTEGER", nullable: false),
                    IntendedRecipientsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    BodyHtml = table.Column<string>(type: "TEXT", nullable: true),
                    BodyText = table.Column<string>(type: "TEXT", nullable: true),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    HeadersJson = table.Column<string>(type: "TEXT", nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadByUserIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    OriginatingNotificationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    OriginatingWebhookSubscriptionId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SandboxCapturedMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxCapturedMessages_EventType_CapturedAt",
                table: "SandboxCapturedMessages",
                columns: new[] { "EventType", "CapturedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxCapturedMessages_ProcessInstanceId_CapturedAt",
                table: "SandboxCapturedMessages",
                columns: new[] { "ProcessInstanceId", "CapturedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxCapturedMessages_TenantCode_CapturedAt",
                table: "SandboxCapturedMessages",
                columns: new[] { "TenantCode", "CapturedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxCapturedMessages_TenantCode_Channel_CapturedAt",
                table: "SandboxCapturedMessages",
                columns: new[] { "TenantCode", "Channel", "CapturedAt" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SandboxCapturedMessages");
        }
    }
}
