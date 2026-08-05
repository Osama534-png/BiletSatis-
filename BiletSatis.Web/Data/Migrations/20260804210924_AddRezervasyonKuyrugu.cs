using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiletSatis.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRezervasyonKuyrugu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RezervasyonKuyrugu",
                columns: table => new
                {
                    SiraNo = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EtkinlikId = table.Column<int>(type: "int", nullable: false),
                    KullaniciId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Durum = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HakBitisZamani = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OlusturmaZamani = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RezervasyonKuyrugu", x => x.SiraNo);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RezervasyonKuyrugu_EtkinlikId_Durum_SiraNo",
                table: "RezervasyonKuyrugu",
                columns: new[] { "EtkinlikId", "Durum", "SiraNo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RezervasyonKuyrugu");
        }
    }
}
