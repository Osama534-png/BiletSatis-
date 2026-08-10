using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BiletSatis.Web.Services.Etkinlikler;

/// <summary>
/// Ana sayfanın etkinlik sorguları. Filtreleme, sıralama ve sayfalama tamamen
/// veritabanında yapılır: sunucu yalnızca gösterilecek sayfayı okur.
///
/// Öncesinde tüm etkinlikler çekilip filtreleme tarayıcıda yapılıyordu. 19
/// etkinlikte fark edilmiyordu ama 2000 etkinlikte sayfa 5 MB'a çıkıyor ve yanıt
/// süresi 22 ms'den 759 ms'ye tırmanıyordu (ölçüm: loadtests/k6/README.md).
/// </summary>
public class EtkinlikSorguServisi : IEtkinlikSorguServisi
{
    // Sayaçlar ve şehir listesi her sayfa görüntülemesinde tüm tabloyu tarardı;
    // saniyede binlerce istekte bu gereksiz yük. İkisi de nadiren değiştiği için
    // kısa süreli önbellekten okunuyor — bayatlığın görünür bir zararı yok.
    private static readonly TimeSpan OnbellekSuresi = TimeSpan.FromSeconds(30);

    private const string IstatistikAnahtari = "anasayfa:istatistik";
    private const string SehirlerAnahtari = "anasayfa:sehirler";
    private const string FiyatTavaniAnahtari = "anasayfa:fiyat-tavani";

    private readonly BiletSatisDbContext _db;
    private readonly IMemoryCache _onbellek;

    public EtkinlikSorguServisi(BiletSatisDbContext db, IMemoryCache onbellek)
    {
        _db = db;
        _onbellek = onbellek;
    }

    public async Task<SayfaliListe<EtkinlikKartVm>> AraAsync(EtkinlikFiltresi filtre, CancellationToken ct = default)
    {
        var sorgu = _db.Etkinlikler.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtre.Arama))
        {
            // LIKE '%...%' dizin kullanamaz; etkinlik sayısı çok büyürse
            // tam metin arama (full-text index) gerekir.
            var arama = filtre.Arama.Trim();
            sorgu = sorgu.Where(e => EF.Functions.Like(e.Ad, $"%{arama}%"));
        }

        if (filtre.Kategori.HasValue)
        {
            sorgu = sorgu.Where(e => e.Kategori == filtre.Kategori.Value);
        }

        if (!string.IsNullOrWhiteSpace(filtre.Sehir))
        {
            sorgu = sorgu.Where(e => e.Sehir == filtre.Sehir);
        }

        var simdi = DateTime.Now;
        if (filtre.Tarih == "hafta")
        {
            var bitis = simdi.AddDays(7);
            sorgu = sorgu.Where(e => e.Tarih >= simdi && e.Tarih <= bitis);
        }
        else if (filtre.Tarih == "ay")
        {
            var bitis = simdi.AddMonths(1);
            sorgu = sorgu.Where(e => e.Tarih >= simdi && e.Tarih <= bitis);
        }

        if (!filtre.TukenenleriGoster)
        {
            sorgu = sorgu.Where(e => e.Biletler.Any(b => b.Durum == BiletDurumu.Satista));
        }

        if (filtre.EnYuksekFiyat.HasValue)
        {
            // "En düşük fiyatı tavanın altında" demek, satıştaki biletlerden en az
            // birinin tavanın altında olması demektir — Min hesaplamadan sorulabilir.
            var tavan = filtre.EnYuksekFiyat.Value;
            sorgu = sorgu.Where(e => e.Biletler.Any(b => b.Durum == BiletDurumu.Satista && b.Fiyat <= tavan));
        }

        var toplam = await sorgu.CountAsync(ct);

        sorgu = filtre.Siralama switch
        {
            "fiyat-artan" => sorgu.OrderBy(e => e.Biletler
                .Where(b => b.Durum == BiletDurumu.Satista)
                .Min(b => (decimal?)b.Fiyat)).ThenBy(e => e.Tarih),

            "fiyat-azalan" => sorgu.OrderByDescending(e => e.Biletler
                .Where(b => b.Durum == BiletDurumu.Satista)
                .Min(b => (decimal?)b.Fiyat)).ThenBy(e => e.Tarih),

            "isim" => sorgu.OrderBy(e => e.Ad).ThenBy(e => e.Tarih),

            _ => sorgu.OrderBy(e => e.Tarih).ThenBy(e => e.Id)
        };

        var sayfa = filtre.GecerliSayfa;

        var ogeler = await sorgu
            .Skip((sayfa - 1) * filtre.SayfaBoyutu)
            .Take(filtre.SayfaBoyutu)
            .Select(e => new EtkinlikKartVm
            {
                Id = e.Id,
                Ad = e.Ad,
                Mekan = e.Mekan,
                AfisUrl = e.AfisUrl,
                Kategori = e.Kategori,
                Tarih = e.Tarih,
                MusaitKoltukSayisi = e.Biletler.Count(b => b.Durum == BiletDurumu.Satista),
                EnDusukFiyat = e.Biletler
                    .Where(b => b.Durum == BiletDurumu.Satista)
                    .Select(b => (decimal?)b.Fiyat)
                    .Min()
            })
            .ToListAsync(ct);

        return new SayfaliListe<EtkinlikKartVm>
        {
            Ogeler = ogeler,
            Sayfa = sayfa,
            SayfaBoyutu = filtre.SayfaBoyutu,
            ToplamKayit = toplam
        };
    }

    public Task<List<string>> SehirlerAsync(CancellationToken ct = default) =>
        _onbellek.GetOrCreateAsync(SehirlerAnahtari, async giris =>
        {
            giris.AbsoluteExpirationRelativeToNow = OnbellekSuresi;

            return await _db.Etkinlikler
                .AsNoTracking()
                .Where(e => e.Sehir != "")
                .Select(e => e.Sehir)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync(ct);
        })!;

    public Task<AnaSayfaIstatistigi> IstatistikAsync(CancellationToken ct = default) =>
        _onbellek.GetOrCreateAsync(IstatistikAnahtari, async giris =>
        {
            giris.AbsoluteExpirationRelativeToNow = OnbellekSuresi;

            var etkinlik = await _db.Etkinlikler.AsNoTracking().CountAsync(ct);
            var satista = await _db.Biletler.AsNoTracking().CountAsync(b => b.Durum == BiletDurumu.Satista, ct);
            var kuyruk = await _db.RezervasyonKuyrugu.AsNoTracking()
                .CountAsync(k => k.Durum == KuyrukDurumu.Beklemede, ct);

            return new AnaSayfaIstatistigi(etkinlik, satista, kuyruk);
        })!;

    public Task<decimal> FiyatTavaniAsync(CancellationToken ct = default) =>
        _onbellek.GetOrCreateAsync(FiyatTavaniAnahtari, async giris =>
        {
            giris.AbsoluteExpirationRelativeToNow = OnbellekSuresi;

            var enYuksek = await _db.Biletler
                .AsNoTracking()
                .Where(b => b.Durum == BiletDurumu.Satista)
                .MaxAsync(b => (decimal?)b.Fiyat, ct);

            // Kaydırıcı yuvarlak bir üst sınırla başlasın.
            return enYuksek is null or <= 0 ? 1000m : Math.Ceiling(enYuksek.Value / 100m) * 100m;
        })!;
}
