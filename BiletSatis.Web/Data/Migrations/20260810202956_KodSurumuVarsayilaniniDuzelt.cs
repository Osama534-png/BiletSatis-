using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiletSatis.Web.Data.Migrations
{
    /// <summary>
    /// Kod sürümü sütunu eklenirken varsayılanı 0 bırakılmıştı ve o ana kadarki bütün
    /// biletler sıfırla kaldı. Kod çözücü sıfır sürümü geçersiz saydığı için bu
    /// biletlerin QR kodları kapıda "sahte bilet" olarak reddediliyordu — sistem kendi
    /// ürettiği kodu kendisi tanımıyordu.
    ///
    /// Migration iki işi birden yapıyor: kalan kayıtları 1'e çekiyor ve sütunun
    /// varsayılanını 1 yapıyor ki EF dışından eklenen satırlar da geçerli olsun.
    /// </summary>
    public partial class KodSurumuVarsayilaniniDuzelt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "KodSurumu",
                table: "Biletler",
                type: "int",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "int");

            // Sıfır sürümlü biletleri geçerli hâle getir. Sürüm yalnızca devir
            // sırasında artıyor; bu biletler hiç devredilmediği için doğru değer 1.
            migrationBuilder.Sql("UPDATE Biletler SET KodSurumu = 1 WHERE KodSurumu < 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "KodSurumu",
                table: "Biletler",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 1);
        }
    }
}
