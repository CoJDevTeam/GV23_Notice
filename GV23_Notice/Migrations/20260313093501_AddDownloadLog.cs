using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GV23_Notice.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NoticeDownloadLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DownloadedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DownloadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SearchMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SearchTerm = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ObjectionNo = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    AppealNo = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    PropertyDesc = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    FileCount = table.Column<int>(type: "int", nullable: false),
                    ZipFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeDownloadLogs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoticeDownloadLogs");
        }
    }
}
