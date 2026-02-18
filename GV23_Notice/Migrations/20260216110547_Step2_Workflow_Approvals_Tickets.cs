using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GV23_Notice.Migrations
{
    /// <inheritdoc />
    public partial class Step2_Workflow_Approvals_Tickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsStep2Approved",
                table: "NoticeSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "Step2ApprovedAtUtc",
                table: "NoticeSettings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Step2ApprovedBy",
                table: "NoticeSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailSent",
                table: "CorrectionTickets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailSentAtUtc",
                table: "CorrectionTickets",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailTo",
                table: "CorrectionTickets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidencePath",
                table: "CorrectionTickets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Notice",
                table: "CorrectionTickets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RequestComment",
                table: "CorrectionTickets",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "RequestedAtUtc",
                table: "CorrectionTickets",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "RequestedBy",
                table: "CorrectionTickets",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ResolutionNote",
                table: "CorrectionTickets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAtUtc",
                table: "CorrectionTickets",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedBy",
                table: "CorrectionTickets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RollId",
                table: "CorrectionTickets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "CorrectionTickets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "NoticeTemplateApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NoticeSettingsId = table.Column<int>(type: "int", nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    RevokedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeTemplateApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeTemplateApprovals_NoticeSettings_NoticeSettingsId",
                        column: x => x.NoticeSettingsId,
                        principalTable: "NoticeSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NoticeWorkflowAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NoticeSettingsId = table.Column<int>(type: "int", nullable: false),
                    RollId = table.Column<int>(type: "int", nullable: false),
                    Notice = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    PerformedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PerformedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    MetaJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeWorkflowAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeWorkflowAuditLogs_NoticeSettings_NoticeSettingsId",
                        column: x => x.NoticeSettingsId,
                        principalTable: "NoticeSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionTickets_NoticeSettingsId",
                table: "CorrectionTickets",
                column: "NoticeSettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionTickets_RollId_Notice_Status",
                table: "CorrectionTickets",
                columns: new[] { "RollId", "Notice", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeTemplateApprovals_NoticeSettingsId",
                table: "NoticeTemplateApprovals",
                column: "NoticeSettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeWorkflowAuditLogs_NoticeSettingsId_Action",
                table: "NoticeWorkflowAuditLogs",
                columns: new[] { "NoticeSettingsId", "Action" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoticeTemplateApprovals");

            migrationBuilder.DropTable(
                name: "NoticeWorkflowAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_CorrectionTickets_NoticeSettingsId",
                table: "CorrectionTickets");

            migrationBuilder.DropIndex(
                name: "IX_CorrectionTickets_RollId_Notice_Status",
                table: "CorrectionTickets");

            migrationBuilder.DropColumn(
                name: "IsStep2Approved",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "Step2ApprovedAtUtc",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "Step2ApprovedBy",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "EmailSent",
                table: "CorrectionTickets");

            migrationBuilder.DropColumn(
                name: "EmailSentAtUtc",
                table: "CorrectionTickets");

            migrationBuilder.DropColumn(
                name: "EmailTo",
                table: "CorrectionTickets");

            migrationBuilder.DropColumn(
                name: "EvidencePath",
                table: "CorrectionTickets");

            migrationBuilder.DropColumn(
                name: "Notice",
                table: "CorrectionTickets");

            migrationBuilder.DropColumn(
                name: "RequestComment",
                table: "CorrectionTickets");

            migrationBuilder.DropColumn(
                name: "RequestedAtUtc",
                table: "CorrectionTickets");

            migrationBuilder.DropColumn(
                name: "RequestedBy",
                table: "CorrectionTickets");

            migrationBuilder.DropColumn(
                name: "ResolutionNote",
                table: "CorrectionTickets");

            migrationBuilder.DropColumn(
                name: "ResolvedAtUtc",
                table: "CorrectionTickets");

            migrationBuilder.DropColumn(
                name: "ResolvedBy",
                table: "CorrectionTickets");

            migrationBuilder.DropColumn(
                name: "RollId",
                table: "CorrectionTickets");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "CorrectionTickets");
        }
    }
}
