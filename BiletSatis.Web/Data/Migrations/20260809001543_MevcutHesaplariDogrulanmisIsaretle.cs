using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiletSatis.Web.Data.Migrations
{
    /// <summary>
    /// E-posta doğrulama zorunlu hâle getirildi. Bu özellikten önce açılmış hesaplar
    /// adreslerini doğrulama fırsatı bulamadı; işaretlenmezlerse mevcut kullanıcıların
    /// tamamı bir anda giriş yapamaz olurdu. Bundan sonra açılan hesaplar normal
    /// doğrulama akışından geçer.
    /// </summary>
    public partial class MevcutHesaplariDogrulanmisIsaretle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE AspNetUsers SET EmailConfirmed = 1 WHERE EmailConfirmed = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Hangi hesabın önceden doğrulanmış olduğu bilgisi saklanmadığı için
            // bu işlem geri alınamaz; geri alma bilinçli olarak boş bırakıldı.
        }
    }
}
