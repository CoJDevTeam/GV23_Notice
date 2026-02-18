using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GV23_Notice.Migrations
{
    /// <inheritdoc />
    public partial class addMoreColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApprovalKey",
                table: "NoticeSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedEmailSavedPath",
                table: "NoticeSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedEmailSentAtUtc",
                table: "NoticeSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionEmailSavedPath",
                table: "NoticeSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionEmailSentAtUtc",
                table: "NoticeSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinancialYearsText",
                table: "NoticeSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RollName",
                table: "NoticeSettings",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovalKey",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "ApprovedEmailSavedPath",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "ApprovedEmailSentAtUtc",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "CorrectionEmailSavedPath",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "CorrectionEmailSentAtUtc",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "FinancialYearsText",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "RollName",
                table: "NoticeSettings");
        }
    }
}
