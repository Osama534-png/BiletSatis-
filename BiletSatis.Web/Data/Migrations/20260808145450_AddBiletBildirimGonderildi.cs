using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiletSatis.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBiletBildirimGonderildi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BildirimGonderildi",
                table: "Biletler",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Biletler_Durum_BildirimGonderildi",
                table: "Biletler",
                columns: new[] { "Durum", "BildirimGonderildi" });

            // Bu özellikten önce satılmış biletler bildirilmiş sayılır; aksi halde
            // özellik açılır açılmaz tüm geçmiş satışlara toplu e-posta giderdi.
            migrationBuilder.Sql(
                "UPDATE Biletler SET BildirimGonderildi = 1 WHERE Durum = N'Satıldı'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Biletler_Durum_BildirimGonderildi",
                table: "Biletler");

            migrationBuilder.DropColumn(
                name: "BildirimGonderildi",
                table: "Biletler");
        }
    }
}
