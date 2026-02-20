using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GV23_Notice.Migrations
{
    /// <inheritdoc />
    public partial class AddTablesToLocal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NoticePreviewSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SettingsId = table.Column<int>(type: "int", nullable: false),
                    Notice = table.Column<int>(type: "int", nullable: false),
                    Variant = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EmailSubject = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmailBodyHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PdfBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    IsApprovedSnapshot = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UiMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PdfFileName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticePreviewSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticePreviewSnapshots_NoticeSettings_SettingsId",
                        column: x => x.SettingsId,
                        principalTable: "NoticeSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NoticeSettingsAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SettingsId = table.Column<int>(type: "int", nullable: false),
                    Step = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PerformedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PerformedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeSettingsAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeSettingsAudits_NoticeSettings_SettingsId",
                        column: x => x.SettingsId,
                        principalTable: "NoticeSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "S49BatchRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SettingsId = table.Column<int>(type: "int", nullable: false),
                    RollId = table.Column<int>(type: "int", nullable: false),
                    BatchName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TargetSize = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PickedPremiseCount = table.Column<int>(type: "int", nullable: false),
                    SentCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_S49BatchRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "S49BatchItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchRunId = table.Column<int>(type: "int", nullable: false),
                    PremiseId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsSplit = table.Column<bool>(type: "bit", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_S49BatchItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_S49BatchItems_S49BatchRuns_BatchRunId",
                        column: x => x.BatchRunId,
                        principalTable: "S49BatchRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoticePreviewSnapshots_SettingsId",
                table: "NoticePreviewSnapshots",
                column: "SettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeSettingsAudits_SettingsId",
                table: "NoticeSettingsAudits",
                column: "SettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_S49BatchItems_BatchRunId_PremiseId",
                table: "S49BatchItems",
                columns: new[] { "BatchRunId", "PremiseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_S49BatchRuns_SettingsId_BatchName",
                table: "S49BatchRuns",
                columns: new[] { "SettingsId", "BatchName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoticePreviewSnapshots");

            migrationBuilder.DropTable(
                name: "NoticeSettingsAudits");

            migrationBuilder.DropTable(
                name: "S49BatchItems");

            migrationBuilder.DropTable(
                name: "S49BatchRuns");
        }
    }
}
