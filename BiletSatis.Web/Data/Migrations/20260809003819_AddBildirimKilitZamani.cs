using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiletSatis.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBildirimKilitZamani : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BildirimKilitZamani",
                table: "RezervasyonKuyrugu",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BildirimKilitZamani",
                table: "Biletler",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BildirimKilitZamani",
                table: "RezervasyonKuyrugu");

            migrationBuilder.DropColumn(
                name: "BildirimKilitZamani",
                table: "Biletler");
        }
    }
}
