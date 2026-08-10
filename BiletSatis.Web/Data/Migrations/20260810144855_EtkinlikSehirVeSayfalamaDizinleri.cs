using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiletSatis.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class EtkinlikSehirVeSayfalamaDizinleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Etkinlikler_Kategori",
                table: "Etkinlikler");

            migrationBuilder.AddColumn<string>(
                name: "Sehir",
                table: "Etkinlikler",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // Mevcut etkinliklerin şehri, Mekan alanının son virgülünden sonraki
            // kısımdan doldurulur ("Volkswagen Arena, İstanbul" → "İstanbul").
            // Bu yapılmazsa şehir seçici boş kalır ve şehir filtresi hiçbir sonuç
            // döndürmez. Yeni kayıtlarda değeri SaveChanges üretir.
            migrationBuilder.Sql("""
                UPDATE Etkinlikler
                SET Sehir = LTRIM(RTRIM(RIGHT(Mekan, CHARINDEX(',', REVERSE(Mekan)) - 1)))
                WHERE CHARINDEX(',', Mekan) > 0
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Etkinlikler_Kategori_Tarih",
                table: "Etkinlikler",
                columns: new[] { "Kategori", "Tarih" });

            migrationBuilder.CreateIndex(
                name: "IX_Etkinlikler_Sehir_Tarih",
                table: "Etkinlikler",
                columns: new[] { "Sehir", "Tarih" });

            migrationBuilder.CreateIndex(
                name: "IX_Etkinlikler_Tarih",
                table: "Etkinlikler",
                column: "Tarih");

            migrationBuilder.CreateIndex(
                name: "IX_Biletler_EtkinlikId_Durum",
                table: "Biletler",
                columns: new[] { "EtkinlikId", "Durum" })
                .Annotation("SqlServer:Include", new[] { "Fiyat" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Etkinlikler_Kategori_Tarih",
                table: "Etkinlikler");

            migrationBuilder.DropIndex(
                name: "IX_Etkinlikler_Sehir_Tarih",
                table: "Etkinlikler");

            migrationBuilder.DropIndex(
                name: "IX_Etkinlikler_Tarih",
                table: "Etkinlikler");

            migrationBuilder.DropIndex(
                name: "IX_Biletler_EtkinlikId_Durum",
                table: "Biletler");

            migrationBuilder.DropColumn(
                name: "Sehir",
                table: "Etkinlikler");

            migrationBuilder.CreateIndex(
                name: "IX_Etkinlikler_Kategori",
                table: "Etkinlikler",
                column: "Kategori");
        }
    }
}
