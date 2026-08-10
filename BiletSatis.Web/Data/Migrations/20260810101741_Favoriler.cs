using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiletSatis.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Favoriler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Favoriler",
                columns: table => new
                {
                    KullaniciId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    EtkinlikId = table.Column<int>(type: "int", nullable: false),
                    EklenmeZamani = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favoriler", x => new { x.KullaniciId, x.EtkinlikId });
                    table.ForeignKey(
                        name: "FK_Favoriler_Etkinlikler_EtkinlikId",
                        column: x => x.EtkinlikId,
                        principalTable: "Etkinlikler",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Favoriler_EtkinlikId",
                table: "Favoriler",
                column: "EtkinlikId");

            migrationBuilder.CreateIndex(
                name: "IX_Favoriler_KullaniciId_EklenmeZamani",
                table: "Favoriler",
                columns: new[] { "KullaniciId", "EklenmeZamani" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Favoriler");
        }
    }
}
