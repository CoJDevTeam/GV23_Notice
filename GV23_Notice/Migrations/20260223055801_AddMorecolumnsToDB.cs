using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GV23_Notice.Migrations
{
    /// <inheritdoc />
    public partial class AddMorecolumnsToDB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsInvalidOmission",
                table: "NoticeSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSection52Review",
                table: "NoticeSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AppealNo",
                table: "NoticePreviewSnapshots",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CopyRole",
                table: "NoticePreviewSnapshots",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NoticeBatchId",
                table: "NoticePreviewSnapshots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NoticeRunLogId",
                table: "NoticePreviewSnapshots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObjectionNo",
                table: "NoticePreviewSnapshots",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObjectorType",
                table: "NoticePreviewSnapshots",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PremiseId",
                table: "NoticePreviewSnapshots",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PropertyDesc",
                table: "NoticePreviewSnapshots",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsInvalidOmission",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "IsSection52Review",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "AppealNo",
                table: "NoticePreviewSnapshots");

            migrationBuilder.DropColumn(
                name: "CopyRole",
                table: "NoticePreviewSnapshots");

            migrationBuilder.DropColumn(
                name: "NoticeBatchId",
                table: "NoticePreviewSnapshots");

            migrationBuilder.DropColumn(
                name: "NoticeRunLogId",
                table: "NoticePreviewSnapshots");

            migrationBuilder.DropColumn(
                name: "ObjectionNo",
                table: "NoticePreviewSnapshots");

            migrationBuilder.DropColumn(
                name: "ObjectorType",
                table: "NoticePreviewSnapshots");

            migrationBuilder.DropColumn(
                name: "PremiseId",
                table: "NoticePreviewSnapshots");

            migrationBuilder.DropColumn(
                name: "PropertyDesc",
                table: "NoticePreviewSnapshots");
        }
    }
}
