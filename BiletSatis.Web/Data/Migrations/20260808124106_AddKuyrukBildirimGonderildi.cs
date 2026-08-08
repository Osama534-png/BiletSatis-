using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiletSatis.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddKuyrukBildirimGonderildi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BildirimGonderildi",
                table: "RezervasyonKuyrugu",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_RezervasyonKuyrugu_Durum_BildirimGonderildi",
                table: "RezervasyonKuyrugu",
                columns: new[] { "Durum", "BildirimGonderildi" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RezervasyonKuyrugu_Durum_BildirimGonderildi",
                table: "RezervasyonKuyrugu");

            migrationBuilder.DropColumn(
                name: "BildirimGonderildi",
                table: "RezervasyonKuyrugu");
        }
    }
}
