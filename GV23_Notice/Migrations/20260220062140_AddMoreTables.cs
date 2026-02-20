using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GV23_Notice.Migrations
{
    /// <inheritdoc />
    public partial class AddMoreTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Step2Approved",
                table: "NoticeSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "Step2ApprovedAt",
                table: "NoticeSettings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Step2ApprovedKeyVersion",
                table: "NoticeSettings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Step2CorrectionReason",
                table: "NoticeSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Step2CorrectionRequested",
                table: "NoticeSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "Step2CorrectionRequestedAt",
                table: "NoticeSettings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Step2CorrectionRequestedBy",
                table: "NoticeSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowKey",
                table: "NoticeSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchKind",
                table: "NoticeBatches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Mode",
                table: "NoticeBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RollId",
                table: "NoticeBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Version",
                table: "NoticeBatches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowKey",
                table: "NoticeBatches",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "NoticeStep2CorrectionRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NoticeSettingsId = table.Column<int>(type: "int", nullable: false),
                    RollId = table.Column<int>(type: "int", nullable: false),
                    Notice = table.Column<int>(type: "int", nullable: false),
                    Variant = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    EmailSubjectSnapshot = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EmailBodyHtmlSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PdfFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PdfSha256 = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SettingsJsonSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeStep2CorrectionRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NoticeStep2Snapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NoticeSettingsId = table.Column<int>(type: "int", nullable: false),
                    RollId = table.Column<int>(type: "int", nullable: false),
                    Notice = table.Column<int>(type: "int", nullable: false),
                    Variant = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EmailSubjectSnapshot = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EmailBodyHtmlSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PdfFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PdfSha256 = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SettingsJsonSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeStep2Snapshots", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoticeStep2CorrectionRequests");

            migrationBuilder.DropTable(
                name: "NoticeStep2Snapshots");

            migrationBuilder.DropColumn(
                name: "Step2Approved",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "Step2ApprovedAt",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "Step2ApprovedKeyVersion",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "Step2CorrectionReason",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "Step2CorrectionRequested",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "Step2CorrectionRequestedAt",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "Step2CorrectionRequestedBy",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "WorkflowKey",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "BatchKind",
                table: "NoticeBatches");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "NoticeBatches");

            migrationBuilder.DropColumn(
                name: "RollId",
                table: "NoticeBatches");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "NoticeBatches");

            migrationBuilder.DropColumn(
                name: "WorkflowKey",
                table: "NoticeBatches");
        }
    }
}
