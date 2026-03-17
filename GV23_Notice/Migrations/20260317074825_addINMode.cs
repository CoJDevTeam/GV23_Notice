using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GV23_Notice.Migrations
{
    /// <inheritdoc />
    public partial class addINMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "INSendMode",
                table: "NoticeSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "INSendMode",
                table: "NoticeSettings");
        }
    }
}
