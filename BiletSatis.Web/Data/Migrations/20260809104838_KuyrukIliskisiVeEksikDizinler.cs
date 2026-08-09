using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiletSatis.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class KuyrukIliskisiVeEksikDizinler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "KullaniciId",
                table: "RezervasyonKuyrugu",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "RezerveEdenKullaniciId",
                table: "Biletler",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RezervasyonKuyrugu_EtkinlikId_KullaniciId",
                table: "RezervasyonKuyrugu",
                columns: new[] { "EtkinlikId", "KullaniciId" });

            migrationBuilder.CreateIndex(
                name: "IX_Biletler_RezerveEdenKullaniciId_Durum",
                table: "Biletler",
                columns: new[] { "RezerveEdenKullaniciId", "Durum" });

            migrationBuilder.AddForeignKey(
                name: "FK_RezervasyonKuyrugu_Etkinlikler_EtkinlikId",
                table: "RezervasyonKuyrugu",
                column: "EtkinlikId",
                principalTable: "Etkinlikler",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RezervasyonKuyrugu_Etkinlikler_EtkinlikId",
                table: "RezervasyonKuyrugu");

            migrationBuilder.DropIndex(
                name: "IX_RezervasyonKuyrugu_EtkinlikId_KullaniciId",
                table: "RezervasyonKuyrugu");

            migrationBuilder.DropIndex(
                name: "IX_Biletler_RezerveEdenKullaniciId_Durum",
                table: "Biletler");

            migrationBuilder.AlterColumn<string>(
                name: "KullaniciId",
                table: "RezervasyonKuyrugu",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.AlterColumn<string>(
                name: "RezerveEdenKullaniciId",
                table: "Biletler",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);
        }
    }
}
