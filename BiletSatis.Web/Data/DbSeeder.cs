using BiletSatis.Web.Domain;

namespace BiletSatis.Web.Data;

public static class DbSeeder
{
    /// <summary>Bir bilet kategorisi: blok kodu, koltuk adedi ve fiyatı.</summary>
    private record Kategori(string Blok, int Adet, decimal Fiyat);

    private record KonserTaslagi(
        string Ad,
        string Mekan,
        string AfisUrl,
        EtkinlikKategorisi Tur,
        DateTime Tarih,
        Kategori[] Kategoriler);

    public static void Seed(BiletSatisDbContext context)
    {
        if (!context.Etkinlikler.Any())
        {
            var ilk = new Etkinlik
            {
                Ad = "Yaz Konseri 2026",
                Mekan = "Harbiye Cemil Topuzlu Açıkhava Tiyatrosu",
                Tarih = new DateTime(2026, 9, 15, 20, 0, 0, DateTimeKind.Utc)
            };

            for (var i = 1; i <= 20; i++)
            {
                ilk.Biletler.Add(new Bilet
                {
                    KoltukNo = $"A-{i:00}",
                    Fiyat = 250m,
                    Durum = BiletDurumu.Satista
                });
            }

            context.Etkinlikler.Add(ilk);
            context.SaveChanges();
        }

        KonserleriEkle(context);
    }

    /// <summary>
    /// Katalogdaki konserleri adına göre kontrol edip eksik olanları ekler.
    /// Tekrar çalıştırılabilir: mevcut etkinliklere dokunmaz, kopya oluşturmaz.
    /// </summary>
    private static void KonserleriEkle(BiletSatisDbContext context)
    {
        var katalogAdlari = Katalog.Select(k => k.Ad).ToList();
        var mevcutlar = context.Etkinlikler
            .Where(e => katalogAdlari.Contains(e.Ad))
            .ToDictionary(e => e.Ad);

        // Katalog sonradan afiş/mekan kazandıysa, boş kalan alanları tamamla.
        var guncellendi = false;
        foreach (var taslak in Katalog)
        {
            if (!mevcutlar.TryGetValue(taslak.Ad, out var mevcut)) continue;

            if (string.IsNullOrWhiteSpace(mevcut.AfisUrl) && !string.IsNullOrWhiteSpace(taslak.AfisUrl))
            {
                mevcut.AfisUrl = taslak.AfisUrl;
                guncellendi = true;
            }

            if (string.IsNullOrWhiteSpace(mevcut.Mekan) && !string.IsNullOrWhiteSpace(taslak.Mekan))
            {
                mevcut.Mekan = taslak.Mekan;
                guncellendi = true;
            }
        }

        if (guncellendi) context.SaveChanges();

        var eklenecekler = new List<Etkinlik>();

        foreach (var taslak in Katalog)
        {
            if (mevcutlar.ContainsKey(taslak.Ad)) continue;

            var etkinlik = new Etkinlik
            {
                Ad = taslak.Ad,
                Mekan = taslak.Mekan,
                AfisUrl = taslak.AfisUrl,
                Kategori = taslak.Tur,
                Tarih = taslak.Tarih
            };

            foreach (var kategori in taslak.Kategoriler)
            {
                for (var i = 1; i <= kategori.Adet; i++)
                {
                    etkinlik.Biletler.Add(new Bilet
                    {
                        KoltukNo = $"{kategori.Blok}-{i:00}",
                        Fiyat = kategori.Fiyat,
                        Durum = BiletDurumu.Satista
                    });
                }
            }

            eklenecekler.Add(etkinlik);
        }

        if (eklenecekler.Count == 0) return;

        context.Etkinlikler.AddRange(eklenecekler);
        context.SaveChanges();
    }

    /// <summary>
    /// Demo konser kataloğu — farklı şehir, mekan, tarih ve saatler.
    /// Her konserde A (sahneye en yakın, en pahalı) → E (en uzak, en ucuz) kategorileri var.
    /// </summary>
    private static readonly KonserTaslagi[] Katalog =
    [
        new("Mabel Matiz — Yaz Turnesi",
            "Harbiye Cemil Topuzlu Açıkhava Tiyatrosu, İstanbul",
            "/img/afis/mabel-matiz.svg",
            EtkinlikKategorisi.Konser,
            new DateTime(2026, 9, 12, 21, 0, 0, DateTimeKind.Utc),
            [
                new("A", 24, 4200m),
                new("B", 32, 3200m),
                new("C", 40, 2400m),
                new("D", 48, 1500m),
                new("E", 56, 850m)
            ]),

        new("Sezen Aksu — Bir Gece Vakti",
            "Volkswagen Arena, İstanbul",
            "/img/afis/sezen-aksu.svg",
            EtkinlikKategorisi.Konser,
            new DateTime(2026, 10, 3, 20, 30, 0, DateTimeKind.Utc),
            [
                new("A", 20, 5000m),
                new("B", 30, 3800m),
                new("C", 44, 2800m),
                new("D", 52, 1800m),
                new("E", 64, 950m)
            ]),

        new("Duman — Akustik Gece",
            "KüçükÇiftlik Park, İstanbul",
            "/img/afis/duman.svg",
            EtkinlikKategorisi.Konser,
            new DateTime(2026, 8, 22, 22, 0, 0, DateTimeKind.Utc),
            [
                new("A", 28, 2600m),
                new("B", 36, 2000m),
                new("C", 42, 1500m),
                new("D", 50, 1000m),
                new("E", 60, 600m)
            ]),

        new("Sertab Erener — Senfonik",
            "Oran Açıkhava Sahnesi, Ankara",
            "/img/afis/sertab-erener.svg",
            EtkinlikKategorisi.Konser,
            new DateTime(2026, 9, 18, 20, 0, 0, DateTimeKind.Utc),
            [
                new("A", 22, 4500m),
                new("B", 30, 3400m),
                new("C", 38, 2500m),
                new("D", 46, 1600m),
                new("E", 54, 900m)
            ]),

        new("Yalın — Bir Büyülü Gece",
            "Bornova Aşık Veysel Açıkhava Tiyatrosu, İzmir",
            "/img/afis/yalin.svg",
            EtkinlikKategorisi.Konser,
            new DateTime(2026, 8, 29, 21, 30, 0, DateTimeKind.Utc),
            [
                new("A", 26, 3600m),
                new("B", 34, 2800m),
                new("C", 40, 2100m),
                new("D", 48, 1300m),
                new("E", 58, 750m)
            ]),

        new("mor ve ötesi — 30. Yıl",
            "Antalya Açıkhava Tiyatrosu, Antalya",
            "/img/afis/mor-ve-otesi.svg",
            EtkinlikKategorisi.Konser,
            new DateTime(2026, 10, 10, 19, 45, 0, DateTimeKind.Utc),
            [
                new("A", 30, 3000m),
                new("B", 38, 2300m),
                new("C", 44, 1700m),
                new("D", 52, 1100m),
                new("E", 62, 500m)
            ]),

        new("Bir Yaz Gecesi Rüyası",
            "Zorlu PSM Turkcell Sahnesi, İstanbul",
            "/img/afis/tiyatro.svg",
            EtkinlikKategorisi.Tiyatro,
            new DateTime(2026, 9, 25, 20, 0, 0, DateTimeKind.Utc),
            [
                new("A", 24, 1800m),
                new("B", 32, 1200m),
                new("C", 40, 800m),
                new("D", 48, 600m)
            ]),

        new("Açıkhava Sinema Gecesi",
            "Yoğurtçu Parkı, İstanbul",
            "/img/afis/sinema.svg",
            EtkinlikKategorisi.Sinema,
            new DateTime(2026, 9, 5, 21, 0, 0, DateTimeKind.Utc),
            [
                new("A", 40, 700m),
                new("B", 60, 500m)
            ]),

        new("Kahkaha Kulübü — Stand Up Gecesi",
            "Jolly Joker, Ankara",
            "/img/afis/stand-up.svg",
            EtkinlikKategorisi.StandUp,
            new DateTime(2026, 9, 19, 21, 30, 0, DateTimeKind.Utc),
            [
                new("A", 26, 1500m),
                new("B", 34, 1000m),
                new("C", 44, 700m)
            ]),

        new("Yaz Sonu Müzik Festivali",
            "Kilyos Sahil, İstanbul",
            "/img/afis/festival.svg",
            EtkinlikKategorisi.Festival,
            new DateTime(2026, 9, 12, 14, 0, 0, DateTimeKind.Utc),
            [
                new("A", 30, 3500m),
                new("B", 40, 2400m),
                new("C", 50, 1500m),
                new("D", 60, 900m)
            ]),

        new("Warehouse Techno Night",
            "Klein Phönix, İstanbul",
            "/img/afis/elektronik.svg",
            EtkinlikKategorisi.ElektronikMuzik,
            new DateTime(2026, 9, 26, 23, 0, 0, DateTimeKind.Utc),
            [
                new("A", 28, 2200m),
                new("B", 38, 1600m),
                new("C", 50, 1100m)
            ]),

        new("Uçan Balonlar — Çocuk Tiyatrosu",
            "CRR Konser Salonu, İstanbul",
            "/img/afis/cocuk.svg",
            EtkinlikKategorisi.CocukAktiviteleri,
            new DateTime(2026, 9, 20, 13, 0, 0, DateTimeKind.Utc),
            [
                new("A", 30, 800m),
                new("B", 40, 600m),
                new("C", 50, 500m)
            ])
    ];
}
