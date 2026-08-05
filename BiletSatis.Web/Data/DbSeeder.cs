using BiletSatis.Web.Domain;

namespace BiletSatis.Web.Data;

public static class DbSeeder
{
    public static void Seed(BiletSatisDbContext context)
    {
        if (context.Etkinlikler.Any()) return;

        var etkinlik = new Etkinlik
        {
            Ad = "Yaz Konseri 2026",
            Tarih = new DateTime(2026, 9, 15, 20, 0, 0, DateTimeKind.Utc)
        };

        for (var i = 1; i <= 20; i++)
        {
            etkinlik.Biletler.Add(new Bilet
            {
                KoltukNo = $"A-{i:00}",
                Fiyat = 250m,
                Durum = BiletDurumu.Satista
            });
        }

        context.Etkinlikler.Add(etkinlik);
        context.SaveChanges();
    }
}
