using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GV23_Notice.Migrations
{
    /// <inheritdoc />
    public partial class addQATables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
          
            migrationBuilder.CreateTable(
                name: "NoticeQaRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NoticeSettingsId = table.Column<int>(type: "int", nullable: false),
                    RollId = table.Column<int>(type: "int", nullable: false),
                    Notice = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeQaRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeQaRuns_NoticeSettings_NoticeSettingsId",
                        column: x => x.NoticeSettingsId,
                        principalTable: "NoticeSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NoticeQaItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NoticeQaRunId = table.Column<int>(type: "int", nullable: false),
                    NoticeRunLogId = table.Column<int>(type: "int", nullable: false),
                    ObjectionNo = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    PremiseId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    PropertyType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PropertyDesc = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PdfPath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    NewCategoryMvd = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    New2CategoryMvd = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    New3CategoryMvd = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExpectedCategory = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsCategoryValid = table.Column<bool>(type: "bit", nullable: false),
                    QaStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    QaComment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeQaItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeQaItems_NoticeQaRuns_NoticeQaRunId",
                        column: x => x.NoticeQaRunId,
                        principalTable: "NoticeQaRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NoticeQaItems_NoticeRunLogs_NoticeRunLogId",
                        column: x => x.NoticeRunLogId,
                        principalTable: "NoticeRunLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeQaItems_NoticeQaRunId_PropertyType",
                table: "NoticeQaItems",
                columns: new[] { "NoticeQaRunId", "PropertyType" });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeQaItems_NoticeRunLogId",
                table: "NoticeQaItems",
                column: "NoticeRunLogId");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeQaRuns_NoticeSettingsId",
                table: "NoticeQaRuns",
                column: "NoticeSettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeQaRuns_WorkflowKey_Status",
                table: "NoticeQaRuns",
                columns: new[] { "WorkflowKey", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoticeQaItems");

            migrationBuilder.DropTable(
                name: "NoticeQaRuns");

            migrationBuilder.DropColumn(
                name: "PdfBytes",
                table: "NoticeStep2Snapshots");
        }
    }
}
