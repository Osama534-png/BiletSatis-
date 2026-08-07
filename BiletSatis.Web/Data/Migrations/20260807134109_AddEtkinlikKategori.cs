using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiletSatis.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEtkinlikKategori : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kategori",
                table: "Etkinlikler",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Konser");

            migrationBuilder.CreateIndex(
                name: "IX_Etkinlikler_Kategori",
                table: "Etkinlikler",
                column: "Kategori");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Etkinlikler_Kategori",
                table: "Etkinlikler");

            migrationBuilder.DropColumn(
                name: "Kategori",
                table: "Etkinlikler");
        }
    }
}
