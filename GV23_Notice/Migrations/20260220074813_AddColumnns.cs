using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GV23_Notice.Migrations
{
    /// <inheritdoc />
    public partial class AddColumnns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "NoticeBatches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "NoticeBatches",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BatchDate",
                table: "NoticeBatches",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsApproved",
                table: "NoticeBatches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "NumberOfRecords",
                table: "NoticeBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "NoticeBatches");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "NoticeBatches");

            migrationBuilder.DropColumn(
                name: "BatchDate",
                table: "NoticeBatches");

            migrationBuilder.DropColumn(
                name: "IsApproved",
                table: "NoticeBatches");

            migrationBuilder.DropColumn(
                name: "NumberOfRecords",
                table: "NoticeBatches");
        }
    }
}
