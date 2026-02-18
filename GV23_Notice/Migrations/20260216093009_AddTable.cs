using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GV23_Notice.Migrations
{
    /// <inheritdoc />
    public partial class AddTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NoticeSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Roll = table.Column<int>(type: "int", nullable: false),
                    Notice = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    LetterDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ObjectionStartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ObjectionEndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExtensionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EvidenceCloseDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BulkFromDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BulkToDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BatchDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AppealCloseDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AppealCloseOverrideReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AppealCloseOverrideEvidencePath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    AppealCloseOverrideBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AppealCloseOverrideAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SignaturePath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    PortalUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EnquiriesLine = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CityManagerSignDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    ConfirmedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExtractionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExtractPeriodDays = table.Column<int>(type: "int", nullable: true),
                    ReviewOpenDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewCloseDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LetterDateOverridden = table.Column<bool>(type: "bit", nullable: false),
                    LetterDateOverrideReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CorrectionTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NoticeSettingsId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NotifiedTo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    NotifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastEmailError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrectionTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CorrectionTickets_NoticeSettings_NoticeSettingsId",
                        column: x => x.NoticeSettingsId,
                        principalTable: "NoticeSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NoticeApprovalLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NoticeSettingsId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    PerformedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PerformedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeApprovalLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeApprovalLogs_NoticeSettings_NoticeSettingsId",
                        column: x => x.NoticeSettingsId,
                        principalTable: "NoticeSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NoticeBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NoticeSettingsId = table.Column<int>(type: "int", nullable: false),
                    BatchName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Roll = table.Column<int>(type: "int", nullable: false),
                    Notice = table.Column<int>(type: "int", nullable: false),
                    SettingsVersionUsed = table.Column<int>(type: "int", nullable: false),
                    BulkFromDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BulkToDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeBatches_NoticeSettings_NoticeSettingsId",
                        column: x => x.NoticeSettingsId,
                        principalTable: "NoticeSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NoticeRunLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NoticeBatchId = table.Column<int>(type: "int", nullable: false),
                    ObjectionNo = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    AppealNo = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    PremiseId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    PdfPath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    EmlPath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeRunLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeRunLogs_NoticeBatches_NoticeBatchId",
                        column: x => x.NoticeBatchId,
                        principalTable: "NoticeBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionTickets_NoticeSettingsId_Status",
                table: "CorrectionTickets",
                columns: new[] { "NoticeSettingsId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeApprovalLogs_NoticeSettingsId_PerformedAtUtc",
                table: "NoticeApprovalLogs",
                columns: new[] { "NoticeSettingsId", "PerformedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeBatches_BatchName",
                table: "NoticeBatches",
                column: "BatchName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NoticeBatches_NoticeSettingsId",
                table: "NoticeBatches",
                column: "NoticeSettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeBatches_Roll_Notice_CreatedAtUtc",
                table: "NoticeBatches",
                columns: new[] { "Roll", "Notice", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeRunLogs_NoticeBatchId_Status",
                table: "NoticeRunLogs",
                columns: new[] { "NoticeBatchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeSettings_Roll_Notice_Mode_IsApproved_ApprovedAtUtc",
                table: "NoticeSettings",
                columns: new[] { "Roll", "Notice", "Mode", "IsApproved", "ApprovedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeSettings_Roll_Notice_Mode_Version",
                table: "NoticeSettings",
                columns: new[] { "Roll", "Notice", "Mode", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorrectionTickets");

            migrationBuilder.DropTable(
                name: "NoticeApprovalLogs");

            migrationBuilder.DropTable(
                name: "NoticeRunLogs");

            migrationBuilder.DropTable(
                name: "NoticeBatches");

            migrationBuilder.DropTable(
                name: "NoticeSettings");
        }
    }
}
