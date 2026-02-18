using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GV23_Notice.Migrations
{
    /// <inheritdoc />
    public partial class addColumnsOnNoticeTablesetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FinancialYearEnd",
                table: "NoticeSettings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinancialYearStart",
                table: "NoticeSettings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValuationPeriodCode",
                table: "NoticeSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValuationPeriodEnd",
                table: "NoticeSettings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValuationPeriodStart",
                table: "NoticeSettings",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinancialYearEnd",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "FinancialYearStart",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "ValuationPeriodCode",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "ValuationPeriodEnd",
                table: "NoticeSettings");

            migrationBuilder.DropColumn(
                name: "ValuationPeriodStart",
                table: "NoticeSettings");
        }
    }
}
