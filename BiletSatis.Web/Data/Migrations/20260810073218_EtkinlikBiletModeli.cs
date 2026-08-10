using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiletSatis.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class EtkinlikBiletModeli : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BiletModeli",
                table: "Etkinlikler",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "KoltukSecmeli");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BiletModeli",
                table: "Etkinlikler");
        }
    }
}
