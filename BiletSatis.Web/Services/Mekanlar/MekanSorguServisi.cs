using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Web.Services.Mekanlar;

/// <summary>
/// Mekan sayfasının sorguları.
///
/// <para><b>Mekanın kimliği neden metnin kendisi?</b> Projede ayrı bir mekan tablosu
/// yok; mekan, etkinlik satırındaki <c>"Salon Adı, Şehir"</c> metniyle temsil ediliyor.
/// Ayrı tablo açmak doğru modelleme olurdu ama admin panelini, seeder'ı ve etkinlik
/// ekleme/düzenleme akışlarını baştan yazmayı gerektirirdi. Bunun yerine gruplama
/// anahtarı olarak metnin <b>tamamı</b> kullanılıyor — yalnızca salon adı değil,
/// çünkü farklı şehirlerdeki aynı adlı salonlar ("Kültür Merkezi, Ankara" ve
/// "Kültür Merkezi, İzmir") tek mekan sayılırdı.</para>
///
/// <para>Bunun bilinçli sınırı şu: aynı mekan iki etkinlikte farklı yazılmışsa
/// ("Volkswagen Arena, İstanbul" / "Volkswagen Arena,İstanbul") sistem bunları iki
/// ayrı mekan görür. Aynı kısıt <c>Sehir</c> sütununda da var ve veri girişi tek
/// yerden (admin paneli) yapıldığı için pratikte sorun çıkarmıyor.</para>
///
/// <para>Zaman karşılaştırmaları <c>DateTime.Now</c> ile: etkinlik tarihi bir "an"
/// değil takvim saatidir (bkz. README → Zamanın iki türü).</para>
/// </summary>
public class MekanSorguServisi : IMekanSorguServisi
{
    private readonly BiletSatisDbContext _db;

    public MekanSorguServisi(BiletSatisDbContext db)
    {
        _db = db;
    }

    public async Task<MekanOzeti?> OzetAsync(string mekan, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(mekan)) return null;

        var simdi = DateTime.Now;
        var etkinlikler = _db.Etkinlikler.AsNoTracking().Where(e => e.Mekan == mekan);

        var yaklasan = await etkinlikler.CountAsync(e => e.Tarih > simdi, ct);
        var gecmis = await etkinlikler.CountAsync(e => e.Tarih <= simdi, ct);

        // Hiç etkinliği olmayan bir mekan yoktur: mekan zaten etkinlik satırından doğuyor.
        if (yaklasan + gecmis == 0) return null;

        // "Biletler ... ₺'den başlıyor" yalnızca hâlâ satın alınabilir biletler için
        // anlamlı; geçmiş etkinliklerin fiyatı kullanıcıya bir şey vaat etmez.
        var enDusukFiyat = await _db.Biletler
            .AsNoTracking()
            .Where(b => b.Durum == BiletDurumu.Satista
                     && b.Etkinlik!.Mekan == mekan
                     && b.Etkinlik.Tarih > simdi)
            .MinAsync(b => (decimal?)b.Fiyat, ct);

        // Puan, mekanda yapılmış bütün etkinliklerin değerlendirmelerinin ortalaması.
        // Ortalama ve adet tek sorguda alınır; bütün puanları belleğe çekip C# tarafında
        // ortalamak, değerlendirme sayısı büyüdükçe gereksiz veri taşırdı.
        var puan = await _db.Degerlendirmeler
            .AsNoTracking()
            .Where(d => d.Etkinlik!.Mekan == mekan)
            .GroupBy(d => 1)
            .Select(g => new { Ortalama = (double?)g.Average(x => x.Puan), Adet = g.Count() })
            .FirstOrDefaultAsync(ct);

        return new MekanOzeti(
            mekan,
            yaklasan,
            gecmis,
            enDusukFiyat,
            puan?.Ortalama,
            puan?.Adet ?? 0);
    }

    public async Task<SayfaliListe<EtkinlikKartVm>> EtkinliklerAsync(
        string mekan, bool gecmis, int sayfa, int sayfaBoyutu, CancellationToken ct = default)
    {
        // Sayfa ve boyut adres çubuğundan geliyor; ana sayfadaki sınırların aynısı
        // burada da geçerli olmalı (bkz. EtkinlikFiltresi: negatif OFFSET taşması).
        var gecerliSayfa = Math.Clamp(sayfa, 1, EtkinlikFiltresi.AzamiSayfa);
        var gecerliBoyut = sayfaBoyutu is > 0 and <= EtkinlikFiltresi.AzamiSayfaBoyutu
            ? sayfaBoyutu
            : EtkinlikFiltresi.VarsayilanSayfaBoyutu;

        if (string.IsNullOrWhiteSpace(mekan))
        {
            return new SayfaliListe<EtkinlikKartVm> { Sayfa = gecerliSayfa, SayfaBoyutu = gecerliBoyut };
        }

        var simdi = DateTime.Now;

        var sorgu = _db.Etkinlikler
            .AsNoTracking()
            .Where(e => e.Mekan == mekan);

        // Yaklaşanlar en yakın tarihten başlar; geçmişte ise en son yapılan etkinlik
        // en üstte olmalı — kullanıcı "burada en son ne oldu" diye bakar.
        sorgu = gecmis
            ? sorgu.Where(e => e.Tarih <= simdi).OrderByDescending(e => e.Tarih).ThenByDescending(e => e.Id)
            : sorgu.Where(e => e.Tarih > simdi).OrderBy(e => e.Tarih).ThenBy(e => e.Id);

        var toplam = await sorgu.CountAsync(ct);

        var ogeler = await sorgu
            .Skip((gecerliSayfa - 1) * gecerliBoyut)
            .Take(gecerliBoyut)
            .Select(EtkinlikKartVm.Projeksiyon)
            .ToListAsync(ct);

        return new SayfaliListe<EtkinlikKartVm>
        {
            Ogeler = ogeler,
            Sayfa = gecerliSayfa,
            SayfaBoyutu = gecerliBoyut,
            ToplamKayit = toplam
        };
    }

    public async Task<List<EtkinlikKartVm>> DigerEtkinliklerAsync(
        string mekan, int haricEtkinlikId, int adet, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(mekan) || adet <= 0) return new List<EtkinlikKartVm>();

        var simdi = DateTime.Now;

        return await _db.Etkinlikler
            .AsNoTracking()
            .Where(e => e.Mekan == mekan && e.Id != haricEtkinlikId && e.Tarih > simdi)
            .OrderBy(e => e.Tarih)
            .ThenBy(e => e.Id)
            .Take(adet)
            .Select(EtkinlikKartVm.Projeksiyon)
            .ToListAsync(ct);
    }
}
