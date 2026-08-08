using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Web.Services;

public class BiletRezervasyonServisi : IBiletRezervasyonServisi
{
    private const int KilitDakikasi = 5;

    private readonly BiletSatisDbContext _db;
    private readonly ILogger<BiletRezervasyonServisi> _logger;

    public BiletRezervasyonServisi(BiletSatisDbContext db, ILogger<BiletRezervasyonServisi> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SepeteEklemeSonucu> TryAddToCartAsync(int biletId, string kullaniciId, CancellationToken ct = default)
    {
        var sonuc = await TryAddManyToCartAsync(new[] { biletId }, kullaniciId, ct);
        return sonuc.Basarili ? SepeteEklemeSonucu.Basarili : SepeteEklemeSonucu.ZatenAlinmis;
    }

    public async Task<CokluSepeteEklemeSonucu> TryAddManyToCartAsync(
        IReadOnlyCollection<int> biletIdleri, string kullaniciId, CancellationToken ct = default)
    {
        var idler = biletIdleri.Distinct().ToArray();
        if (idler.Length == 0) return CokluSepeteEklemeSonucu.Olumsuz(Array.Empty<string>());

        // Tek koltukta olduğu gibi burada da okuma-sonra-yazma yok: tek UPDATE hem
        // "hâlâ satışta mı" kontrolünü hem de kilidi atıyor. Tek koltuktan farkı,
        // sorgunun birden çok satırı etkileyebilmesi — bu yüzden sonucu ancak
        // etkilenen satır sayısına bakarak değerlendirebiliyoruz ve değerlendirme
        // bitene kadar satırların kilitli kalması için işlem (transaction) gerekiyor.
        await using var islem = await _db.Database.BeginTransactionAsync(ct);

        int etkilenen;
        try
        {
            etkilenen = await KilitleAsync(idler, kullaniciId, ct);
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            // Kilitlenme (deadlock): iki kullanıcı kesişen koltuk kümelerini aynı anda
            // istedi ve SQL Server bizi kurban seçti. İşlem zaten geri alındı.
            _logger.LogWarning(ex, "Çoklu rezervasyon kilitlenmeye takıldı: KullaniciId={KullaniciId}", kullaniciId);
            return CokluSepeteEklemeSonucu.Olumsuz(await AlinamayanKoltuklarAsync(idler, ct));
        }

        if (etkilenen != idler.Length)
        {
            // Koltuklardan en az biri araya girildi. Yarım rezervasyon bırakmak yerine
            // tamamını geri alıyoruz; kullanıcı ya istediği koltukların hepsini alır ya hiçbirini.
            await islem.RollbackAsync(ct);

            // Geri alma bittikten sonra sorguluyoruz, aksi halde kendi yazdığımız
            // satırları "sepette" görürdük.
            var alinamayanlar = await AlinamayanKoltuklarAsync(idler, ct);

            _logger.LogInformation(
                "Çoklu sepete ekleme başarısız: KullaniciId={KullaniciId} İstenen={Istenen} Alinabilen={Alinabilen}",
                kullaniciId, idler.Length, etkilenen);

            return CokluSepeteEklemeSonucu.Olumsuz(alinamayanlar);
        }

        await islem.CommitAsync(ct);

        _logger.LogInformation(
            "Çoklu sepete ekleme başarılı: KullaniciId={KullaniciId} KoltukSayisi={KoltukSayisi}",
            kullaniciId, idler.Length);

        return CokluSepeteEklemeSonucu.Olumlu();
    }

    private Task<int> KilitleAsync(int[] idler, string kullaniciId, CancellationToken ct) =>
        _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE Biletler
            SET Durum = {BiletDurumMetni.Sepette},
                KilitBitisZamani = DATEADD(MINUTE, {KilitDakikasi}, GETUTCDATE()),
                RezerveEdenKullaniciId = {kullaniciId}
            WHERE Durum = {BiletDurumMetni.Satista}
              AND Id IN (SELECT CAST(value AS INT) FROM STRING_SPLIT({IdListesi(idler)}, ','))
            """, ct);

    private async Task<IReadOnlyList<string>> AlinamayanKoltuklarAsync(int[] idler, CancellationToken ct) =>
        await _db.Biletler
            .AsNoTracking()
            .Where(b => idler.Contains(b.Id) && b.Durum != BiletDurumu.Satista)
            .OrderBy(b => b.KoltukNo)
            .Select(b => b.KoltukNo)
            .ToListAsync(ct);

    public async Task<int> ReleaseExpiredCartHoldsAsync(CancellationToken ct = default)
    {
        var etkilenen = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE Biletler
            SET Durum = {BiletDurumMetni.Satista},
                RezerveEdenKullaniciId = NULL,
                KilitBitisZamani = NULL
            WHERE Durum = {BiletDurumMetni.Sepette} AND KilitBitisZamani < GETUTCDATE()
            """, ct);

        return etkilenen;
    }

    public async Task<int> ExtendCartHoldsAsync(
        IReadOnlyCollection<int> biletIdleri, string kullaniciId, int dakika, CancellationToken ct = default)
    {
        var idler = biletIdleri.Distinct().ToArray();
        if (idler.Length == 0) return 0;

        var etkilenen = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE Biletler
            SET KilitBitisZamani = DATEADD(MINUTE, {dakika}, GETUTCDATE())
            WHERE Durum = {BiletDurumMetni.Sepette}
              AND RezerveEdenKullaniciId = {kullaniciId}
              AND Id IN (SELECT CAST(value AS INT) FROM STRING_SPLIT({IdListesi(idler)}, ','))
            """, ct);

        _logger.LogInformation(
            "Sepet kilidi uzatıldı: KullaniciId={KullaniciId} Dakika={Dakika} BiletSayisi={BiletSayisi}",
            kullaniciId, dakika, etkilenen);

        return etkilenen;
    }

    public async Task<bool> CompletePaymentAsync(int biletId, string kullaniciId, CancellationToken ct = default) =>
        (await CompletePaymentManyAsync(new[] { biletId }, kullaniciId, null, ct)).SahipOlunan == 1;

    public async Task<OdemeTamamlamaSonucu> CompletePaymentManyAsync(
        IReadOnlyCollection<int> biletIdleri, string kullaniciId, string? odemeReferansi, CancellationToken ct = default)
    {
        var idler = biletIdleri.Distinct().ToArray();
        if (idler.Length == 0) return new OdemeTamamlamaSonucu(0, Array.Empty<string>());

        // İki durumu birden karşılıyoruz:
        //  1) Normal akış — bilet hâlâ bu kullanıcının sepetinde.
        //  2) Kurtarma — kullanıcı Stripe sayfasındayken kilit süresi dolmuş ve
        //     CartExpiryWorker koltuğu serbest bırakmış olabilir. Koltuğu bu arada
        //     kimse almadıysa (Satışta ve sahipsiz) geri alıyoruz; parası ödenmiş
        //     bir koltuğu yalnızca gerçekten başkasına gittiyse kaybediyoruz.
        var etkilenen = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE Biletler
            SET Durum = {BiletDurumMetni.Satildi},
                KilitBitisZamani = NULL,
                BildirimGonderildi = 0,
                RezerveEdenKullaniciId = {kullaniciId},
                OdemeReferansi = {odemeReferansi}
            WHERE Id IN (SELECT CAST(value AS INT) FROM STRING_SPLIT({IdListesi(idler)}, ','))
              AND (
                    (Durum = {BiletDurumMetni.Sepette} AND RezerveEdenKullaniciId = {kullaniciId})
                 OR (Durum = {BiletDurumMetni.Satista} AND RezerveEdenKullaniciId IS NULL)
                  )
            """, ct);

        // Sonucu etkilenen satır sayısından değil, kullanıcının gerçekten sahip olduğu
        // bilet sayısından okuyoruz. Böylece işlem tekrar çalıştırıldığında (kullanıcı
        // başarı sayfasını yenilerse) hiçbir satır güncellenmese bile doğru cevap döner
        // ve sahte "biletiniz kayboldu" uyarısı çıkmaz.
        var sahipOlunanlar = await _db.Biletler
            .AsNoTracking()
            .Where(b => idler.Contains(b.Id)
                     && b.Durum == BiletDurumu.Satildi
                     && b.RezerveEdenKullaniciId == kullaniciId)
            .Select(b => new { b.KoltukNo, b.OdemeReferansi })
            .ToListAsync(ct);

        // Bilet başka bir ödeme oturumuna ait görünüyorsa, kullanıcı aynı koltuklar
        // için iki kez ödeme yapmış demektir (ör. iki sekmede ödemeye geçip ikisini de
        // tamamlamak). Aynı oturumun tekrar çalışması bu listeye girmez.
        var cifteOdenen = odemeReferansi == null
            ? Array.Empty<string>()
            : sahipOlunanlar
                .Where(b => b.OdemeReferansi != null && b.OdemeReferansi != odemeReferansi)
                .Select(b => b.KoltukNo)
                .OrderBy(k => k)
                .ToArray();

        _logger.LogInformation(
            "Ödeme sonucu: KullaniciId={KullaniciId} İstenen={Istenen} Yazilan={Yazilan} SahipOlunan={SahipOlunan}",
            kullaniciId, idler.Length, etkilenen, sahipOlunanlar.Count);

        return new OdemeTamamlamaSonucu(sahipOlunanlar.Count, cifteOdenen);
    }

    public async Task<bool> CancelReservationAsync(int biletId, string kullaniciId, CancellationToken ct = default)
    {
        var etkilenen = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE Biletler
            SET Durum = {BiletDurumMetni.Satista},
                RezerveEdenKullaniciId = NULL,
                KilitBitisZamani = NULL
            WHERE Id = {biletId} AND Durum = {BiletDurumMetni.Sepette} AND RezerveEdenKullaniciId = {kullaniciId}
            """, ct);

        return etkilenen == 1;
    }

    // Id listesi tek bir metin parametresi olarak gider, SQL tarafında STRING_SPLIT ile
    // tabloya çevrilir. Böylece koltuk sayısına göre değişen bir SQL metni üretmeden,
    // tamamen parametreli tek bir sorgu kullanılabiliyor.
    private static string IdListesi(int[] idler) => string.Join(',', idler);
}
