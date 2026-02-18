using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GV23_Notice.Migrations
{
    /// <inheritdoc />
    public partial class AddRollRegistryTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RollId",
                table: "NoticeSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RollRegistry",
                columns: table => new
                {
                    RollId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyRollId = table.Column<int>(type: "int", nullable: true),
                    RollType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ShortCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceDb = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RollRegistry", x => x.RollId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RollRegistry_ShortCode",
                table: "RollRegistry",
                column: "ShortCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RollRegistry_SourceDb_IsActive",
                table: "RollRegistry",
                columns: new[] { "SourceDb", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RollRegistry");

            migrationBuilder.DropColumn(
                name: "RollId",
                table: "NoticeSettings");
        }
    }
}
