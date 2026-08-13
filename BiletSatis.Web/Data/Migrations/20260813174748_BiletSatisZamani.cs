using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiletSatis.Web.Data.Migrations
{
    /// <summary>
    /// Biletlere satış zamanı ekler (trend listesinin zaman boyutu).
    ///
    /// <para><b>Mevcut satışlar bilerek geriye doldurulmuyor.</b> O biletlerin ne
    /// zaman satıldığı hiçbir yerde kayıtlı değil; uydurulacak tek makul değer
    /// (migration anı) hepsini "bugün satıldı" gösterir ve trend listesini gerçekte
    /// olmayan bir satış patlamasıyla doldururdu. NULL kalmaları doğru cevaptır:
    /// "tüm zamanlar" sıralamasına girerler, dönem sıralamalarına girmezler.</para>
    ///
    /// <para>Karşılaştırma için: <c>KodSurumu</c> sütunu eklenirken varsayılan 0
    /// bırakılmış ve o güne kadarki bütün biletlerin QR'ı kapıda geçersiz hâle
    /// gelmişti. Sütun eklerken asıl soru "kod ne yapıyor" değil, "eski satırlarda
    /// bu değer ne anlama geliyor".</para>
    /// </summary>
    public partial class BiletSatisZamani : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SatisZamani",
                table: "Biletler",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Biletler_Durum_SatisZamani",
                table: "Biletler",
                columns: new[] { "Durum", "SatisZamani" })
                .Annotation("SqlServer:Include", new[] { "EtkinlikId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Biletler_Durum_SatisZamani",
                table: "Biletler");

            migrationBuilder.DropColumn(
                name: "SatisZamani",
                table: "Biletler");
        }
    }
}
