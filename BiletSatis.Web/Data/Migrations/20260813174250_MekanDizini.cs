using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiletSatis.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MekanDizini : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Etkinlikler_Mekan_Tarih",
                table: "Etkinlikler",
                columns: new[] { "Mekan", "Tarih" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Etkinlikler_Mekan_Tarih",
                table: "Etkinlikler");
        }
    }
}
