using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bpm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActorResolutionAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorRefJson = table.Column<string>(type: "text", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StepCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResultKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ResolvedUserIdsJson = table.Column<string>(type: "text", nullable: true),
                    ErrorKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorReason = table.Column<string>(type: "text", nullable: true),
                    ImpersonatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActorResolutionAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "APE_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectReceiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DeductReturnDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ChargeDepartment = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RechargeOutside = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoundCount = table.Column<int>(type: "integer", nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerApproved = table.Column<bool>(type: "boolean", nullable: true),
                    ManagerComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ManagerDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_APE_V1_case", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendancePunches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PunchType = table.Column<int>(type: "integer", nullable: false),
                    PunchAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendancePunches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DoctorActionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FlowCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Affected = table.Column<int>(type: "integer", nullable: false),
                    OperatorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorActionLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EOB_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BusinessTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EmployeeLocation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OnboardDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequireMailbox = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    CostCenter = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContractNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ContractEffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ContractExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    setup_tasks_json = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoundCount = table.Column<int>(type: "integer", nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerApproved = table.Column<bool>(type: "boolean", nullable: true),
                    ManagerComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ManagerDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SetupByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SetupAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EOB_V1_case", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ETM_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EmployeeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastWorkingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProvideCertificate = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    OutstandingPayment = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    return_items_json = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoundCount = table.Column<int>(type: "integer", nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerApproved = table.Column<bool>(type: "boolean", nullable: true),
                    ManagerComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ManagerDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HandoverByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    HandoverAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ETM_V1_case", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FAD_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisposalReason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AssetId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AssetName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PhotoFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoundCount = table.Column<int>(type: "integer", nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerApproved = table.Column<bool>(type: "boolean", nullable: true),
                    ManagerComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ManagerDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    HandlingResult = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ConfirmRemark = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FAD_V1_case", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FAP_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_items_json = table.Column<string>(type: "text", nullable: false),
                    ShippingLocation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ChargeTo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExpectedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoundCount = table.Column<int>(type: "integer", nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerApproved = table.Column<bool>(type: "boolean", nullable: true),
                    ManagerComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ManagerDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PurchaseOrderNo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PoIssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Received = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    VerificationRemark = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FAP_V1_case", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileBlobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UploadedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileBlobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FlowSandboxConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FlowCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CaptureEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowSandboxConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrFlowInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SpecCode = table.Column<int>(type: "integer", nullable: false),
                    InitiatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResolvedManagerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentStep = table.Column<int>(type: "integer", nullable: false),
                    FormDataJson = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrFlowInstances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImpersonationSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImpersonatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndReason = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImpersonationSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LEAVE_V1_leave_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaveType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Days = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CertFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    ArchiveNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerApproved = table.Column<bool>(type: "boolean", nullable: true),
                    ManagerComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ManagerDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VpUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    VpApproved = table.Column<bool>(type: "boolean", nullable: true),
                    VpComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    VpDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HrUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    HrArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LEAVE_V1_leave_case", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDispatchAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    SpecCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    NotificationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Recipient = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Body = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDispatchAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PURCHASE_REQUEST_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    invoices_json = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoundCount = table.Column<int>(type: "integer", nullable: false),
                    DeptHeadUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeptHeadApproved = table.Column<bool>(type: "boolean", nullable: true),
                    DeptHeadComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DeptHeadDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinanceUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FinanceApproved = table.Column<bool>(type: "boolean", nullable: true),
                    FinanceComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FinanceDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_REQUEST_V1_case", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoleAssignmentChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    ScopeRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ImpersonatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleAssignmentChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SandboxCapturedMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProcessInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    FlowCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    IntendedRecipientsJson = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BodyHtml = table.Column<string>(type: "text", nullable: true),
                    BodyText = table.Column<string>(type: "text", nullable: true),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    HeadersJson = table.Column<string>(type: "text", nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: true),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "text", nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadByUserIdsJson = table.Column<string>(type: "text", nullable: false),
                    OriginatingNotificationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OriginatingWebhookSubscriptionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SandboxCapturedMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpecBundles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FlowCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FlowVersion = table.Column<int>(type: "integer", nullable: false),
                    ManifestChecksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ParentManifestChecksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ManifestJson = table.Column<string>(type: "text", nullable: false),
                    ZipBlob = table.Column<byte[]>(type: "bytea", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastReproCheckAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastReproCheckResultJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecBundles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SandboxMode = table.Column<bool>(type: "boolean", nullable: false),
                    SandboxConfigJson = table.Column<string>(type: "text", nullable: true),
                    SandboxLastToggledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SandboxLastToggledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SandboxClockOffsetSeconds = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    SystemName = table.Column<string>(type: "text", nullable: true),
                    LogoDataUri = table.Column<string>(type: "text", nullable: true),
                    FaviconDataUri = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TEO_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TravelRequestNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    expense_items_json = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoundCount = table.Column<int>(type: "integer", nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerApproved = table.Column<bool>(type: "boolean", nullable: true),
                    ManagerComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ManagerDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinanceUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FinanceApproved = table.Column<bool>(type: "boolean", nullable: true),
                    FinanceComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FinanceDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TEO_V1_case", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TRQ_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TravelType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DepartureCity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DestinationCity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DepartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReturnDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ChargeTo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TravelPurpose = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PassportName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SeatPreference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PickupRequired = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoundCount = table.Column<int>(type: "integer", nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerApproved = table.Column<bool>(type: "boolean", nullable: true),
                    ManagerComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ManagerDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRQ_V1_case", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FlowCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VENDOR_EXPENSE_V1_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Vendor = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    SubmitterComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    invoices_json = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentAssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoundCount = table.Column<int>(type: "integer", nullable: false),
                    SupervisorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupervisorApproved = table.Column<bool>(type: "boolean", nullable: true),
                    SupervisorComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SupervisorDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcurementUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcurementApproved = table.Column<bool>(type: "boolean", nullable: true),
                    ProcurementComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ProcurementDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SignUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SignApproved = table.Column<bool>(type: "boolean", nullable: true),
                    SignComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SignDecisionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VENDOR_EXPENSE_V1_case", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrFlowActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    FromStep = table.Column<int>(type: "integer", nullable: false),
                    ToStep = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ImpersonatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
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
                name: "IX_ActorResolutionAudits_SubmitterUserId",
                table: "ActorResolutionAudits",
                column: "SubmitterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActorResolutionAudits_Timestamp",
                table: "ActorResolutionAudits",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_APE_V1_case_CurrentAssigneeUserId",
                table: "APE_V1_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_APE_V1_case_Status_LastActivityAt",
                table: "APE_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_APE_V1_case_SubmitterUserId",
                table: "APE_V1_case",
                column: "SubmitterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePunches_UserId_LocalDate",
                table: "AttendancePunches",
                columns: new[] { "UserId", "LocalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePunches_UserId_PunchAt",
                table: "AttendancePunches",
                columns: new[] { "UserId", "PunchAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorActionLogs_CreatedAt",
                table: "DoctorActionLogs",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_EOB_V1_case_CurrentAssigneeUserId",
                table: "EOB_V1_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EOB_V1_case_Status_LastActivityAt",
                table: "EOB_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EOB_V1_case_SubmitterUserId",
                table: "EOB_V1_case",
                column: "SubmitterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ETM_V1_case_CurrentAssigneeUserId",
                table: "ETM_V1_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ETM_V1_case_Status_LastActivityAt",
                table: "ETM_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ETM_V1_case_SubmitterUserId",
                table: "ETM_V1_case",
                column: "SubmitterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FAD_V1_case_CurrentAssigneeUserId",
                table: "FAD_V1_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FAD_V1_case_Status_LastActivityAt",
                table: "FAD_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FAD_V1_case_SubmitterUserId",
                table: "FAD_V1_case",
                column: "SubmitterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FAP_V1_case_CurrentAssigneeUserId",
                table: "FAP_V1_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FAP_V1_case_Status_LastActivityAt",
                table: "FAP_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FAP_V1_case_SubmitterUserId",
                table: "FAP_V1_case",
                column: "SubmitterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FileBlobs_Sha256",
                table: "FileBlobs",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_FileBlobs_UploadedAt",
                table: "FileBlobs",
                column: "UploadedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_FlowSandboxConfigs_TenantCode_FlowCode",
                table: "FlowSandboxConfigs",
                columns: new[] { "TenantCode", "FlowCode" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationSessions_EndedAt",
                table: "ImpersonationSessions",
                column: "EndedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationSessions_ImpersonatorUserId_StartedAt",
                table: "ImpersonationSessions",
                columns: new[] { "ImpersonatorUserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LEAVE_V1_leave_case_CurrentAssigneeUserId",
                table: "LEAVE_V1_leave_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LEAVE_V1_leave_case_Status_LastActivityAt",
                table: "LEAVE_V1_leave_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LEAVE_V1_leave_case_SubmitterUserId",
                table: "LEAVE_V1_leave_case",
                column: "SubmitterUserId");

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

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_REQUEST_V1_case_CurrentAssigneeUserId",
                table: "PURCHASE_REQUEST_V1_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_REQUEST_V1_case_Status_LastActivityAt",
                table: "PURCHASE_REQUEST_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_REQUEST_V1_case_SubmitterUserId",
                table: "PURCHASE_REQUEST_V1_case",
                column: "SubmitterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignmentChanges_ActorUserId_CreatedAt",
                table: "RoleAssignmentChanges",
                columns: new[] { "ActorUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignmentChanges_TargetUserId_CreatedAt",
                table: "RoleAssignmentChanges",
                columns: new[] { "TargetUserId", "CreatedAt" });

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

            migrationBuilder.CreateIndex(
                name: "IX_SandboxCapturedMessages_TenantCode_FlowCode_CapturedAt",
                table: "SandboxCapturedMessages",
                columns: new[] { "TenantCode", "FlowCode", "CapturedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SpecBundles_ManifestChecksum",
                table: "SpecBundles",
                column: "ManifestChecksum",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpecBundles_Status",
                table: "SpecBundles",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SpecBundles_TenantCode_FlowCode_FlowVersion",
                table: "SpecBundles",
                columns: new[] { "TenantCode", "FlowCode", "FlowVersion" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSettings_TenantCode",
                table: "TenantSettings",
                column: "TenantCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TEO_V1_case_CurrentAssigneeUserId",
                table: "TEO_V1_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TEO_V1_case_Status_LastActivityAt",
                table: "TEO_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TEO_V1_case_SubmitterUserId",
                table: "TEO_V1_case",
                column: "SubmitterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TRQ_V1_case_CurrentAssigneeUserId",
                table: "TRQ_V1_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TRQ_V1_case_Status_LastActivityAt",
                table: "TRQ_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TRQ_V1_case_SubmitterUserId",
                table: "TRQ_V1_case",
                column: "SubmitterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_UserId_IsRead_CreatedAt",
                table: "UserNotifications",
                columns: new[] { "UserId", "IsRead", "CreatedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_VENDOR_EXPENSE_V1_case_CurrentAssigneeUserId",
                table: "VENDOR_EXPENSE_V1_case",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VENDOR_EXPENSE_V1_case_Status_LastActivityAt",
                table: "VENDOR_EXPENSE_V1_case",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VENDOR_EXPENSE_V1_case_SubmitterUserId",
                table: "VENDOR_EXPENSE_V1_case",
                column: "SubmitterUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActorResolutionAudits");

            migrationBuilder.DropTable(
                name: "APE_V1_case");

            migrationBuilder.DropTable(
                name: "AttendancePunches");

            migrationBuilder.DropTable(
                name: "DoctorActionLogs");

            migrationBuilder.DropTable(
                name: "EOB_V1_case");

            migrationBuilder.DropTable(
                name: "ETM_V1_case");

            migrationBuilder.DropTable(
                name: "FAD_V1_case");

            migrationBuilder.DropTable(
                name: "FAP_V1_case");

            migrationBuilder.DropTable(
                name: "FileBlobs");

            migrationBuilder.DropTable(
                name: "FlowSandboxConfigs");

            migrationBuilder.DropTable(
                name: "HrFlowActions");

            migrationBuilder.DropTable(
                name: "ImpersonationSessions");

            migrationBuilder.DropTable(
                name: "LEAVE_V1_leave_case");

            migrationBuilder.DropTable(
                name: "NotificationDispatchAudits");

            migrationBuilder.DropTable(
                name: "PURCHASE_REQUEST_V1_case");

            migrationBuilder.DropTable(
                name: "RoleAssignmentChanges");

            migrationBuilder.DropTable(
                name: "SandboxCapturedMessages");

            migrationBuilder.DropTable(
                name: "SpecBundles");

            migrationBuilder.DropTable(
                name: "TenantSettings");

            migrationBuilder.DropTable(
                name: "TEO_V1_case");

            migrationBuilder.DropTable(
                name: "TRQ_V1_case");

            migrationBuilder.DropTable(
                name: "UserNotifications");

            migrationBuilder.DropTable(
                name: "VENDOR_EXPENSE_V1_case");

            migrationBuilder.DropTable(
                name: "HrFlowInstances");
        }
    }
}
