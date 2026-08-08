using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiletSatis.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBiletGirisTakibi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Biletler_EtkinlikId",
                table: "Biletler");

            migrationBuilder.AddColumn<bool>(
                name: "GirisYapildi",
                table: "Biletler",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "GirisZamani",
                table: "Biletler",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Biletler_EtkinlikId_GirisYapildi",
                table: "Biletler",
                columns: new[] { "EtkinlikId", "GirisYapildi" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Biletler_EtkinlikId_GirisYapildi",
                table: "Biletler");

            migrationBuilder.DropColumn(
                name: "GirisYapildi",
                table: "Biletler");

            migrationBuilder.DropColumn(
                name: "GirisZamani",
                table: "Biletler");

            migrationBuilder.CreateIndex(
                name: "IX_Biletler_EtkinlikId",
                table: "Biletler",
                column: "EtkinlikId");
        }
    }
}
