using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Web.Services;

public class KuyrukServisi : IKuyrukServisi
{
    private readonly BiletSatisDbContext _db;
    private readonly ILogger<KuyrukServisi> _logger;

    public KuyrukServisi(BiletSatisDbContext db, ILogger<KuyrukServisi> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int?> EnqueueWaitlistAsync(int etkinlikId, string kullaniciId, CancellationToken ct = default)
    {
        // "Zaten sıradaysa ekleme" kontrolü ayrı bir SELECT ile yapılsaydı, aynı
        // kullanıcının iki isteği aynı anda geldiğinde ikisi de "sırada değil" görüp
        // iki kayıt açardı. Kontrol ile ekleme tek deyimde: NOT EXISTS içindeki
        // UPDLOCK/HOLDLOCK, aralığı kilitleyerek ikinci isteğin araya girmesini önler.
        var siraNolar = await _db.Database.SqlQuery<int>($"""
            INSERT INTO RezervasyonKuyrugu (EtkinlikId, KullaniciId, Durum, OlusturmaZamani)
            OUTPUT INSERTED.SiraNo
            SELECT {etkinlikId}, {kullaniciId}, {KuyrukDurumMetni.Beklemede}, GETUTCDATE()
            WHERE NOT EXISTS (
                SELECT 1 FROM RezervasyonKuyrugu WITH (UPDLOCK, HOLDLOCK)
                WHERE EtkinlikId = {etkinlikId}
                  AND KullaniciId = {kullaniciId}
                  AND Durum <> {KuyrukDurumMetni.SuresiDoldu}
            )
            """).ToListAsync(ct);

        var siraNo = siraNolar.Count == 1 ? siraNolar[0] : (int?)null;

        _logger.LogInformation(
            "Kuyruğa katılım: EtkinlikId={EtkinlikId} KullaniciId={KullaniciId} SiraNo={SiraNo}",
            etkinlikId, kullaniciId, siraNo);

        return siraNo;
    }

    public async Task<int> AllocateWaitlistBatchAsync(int etkinlikId, int n, CancellationToken ct = default)
    {
        if (n <= 0) return 0;

        // UPDLOCK: satırlar daha okunurken güncelleme niyetiyle kilitlenir. Hint olmadan
        // da çift hak tanıma gözlenmedi (bkz. AllocateWaitlistBatchAsync_EsZamanliCagrilar
        // testi); buradaki amaç, paylaşılan kilidin güncelleme kilidine yükseltilmesi
        // sırasında iki eşzamanlı çağrının birbirini kilitlemesini (deadlock) önlemek.
        var etkilenen = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            ;WITH cte AS (
                SELECT TOP ({n}) *
                FROM RezervasyonKuyrugu WITH (UPDLOCK)
                WHERE EtkinlikId = {etkinlikId} AND Durum = {KuyrukDurumMetni.Beklemede}
                ORDER BY SiraNo
            )
            UPDATE cte SET Durum = {KuyrukDurumMetni.HakTanindi},
                           HakBitisZamani = DATEADD(MINUTE, 5, GETUTCDATE()),
                           BildirimGonderildi = 0
            """, ct);

        if (etkilenen > 0)
        {
            _logger.LogInformation("Sıradaki kullanıcılara hak tanındı: EtkinlikId={EtkinlikId} Sayi={Sayi}", etkinlikId, etkilenen);
        }

        return etkilenen;
    }

    public async Task<int> PromoteExpiredAndFillAsync(int etkinlikId, CancellationToken ct = default)
    {
        var suresiDolan = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE RezervasyonKuyrugu
            SET Durum = {KuyrukDurumMetni.SuresiDoldu}
            WHERE EtkinlikId = {etkinlikId} AND Durum = {KuyrukDurumMetni.HakTanindi} AND HakBitisZamani < GETUTCDATE()
            """, ct);

        if (suresiDolan > 0)
        {
            _logger.LogInformation("Süresi dolan {Sayi} kuyruk hakkı sıradakilere devredildi: EtkinlikId={EtkinlikId}", suresiDolan, etkinlikId);
            await AllocateWaitlistBatchAsync(etkinlikId, suresiDolan, ct);// AllocateWaitlistBatchAsync kac kisinin suresi dolduysa o kadar kisiye hak tanir
             
		}

        return suresiDolan;
    }

    public async Task<bool> CompleteQueueEntryAsync(int siraNo, string kullaniciId, CancellationToken ct = default)
    {
        var etkilenen = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE RezervasyonKuyrugu
            SET Durum = {KuyrukDurumMetni.Tamamlandi}
            WHERE SiraNo = {siraNo} AND KullaniciId = {kullaniciId} AND Durum = {KuyrukDurumMetni.HakTanindi}
            """, ct);

        return etkilenen == 1;
    }
}
//Genel Mimari Özeti: Bu yapı, kuyruk sistemlerindeki en büyük problem olan
//"Ölü Kilitlenmeleri" (Deadlocks) ve bekleyen hakları çözer
//. Kullanıcılara süreli hak verir
//(1. metot süreyi denetler), başarılı olanları kaydeder
//(2. metot), başarısız olanların yerine ise sıradakileri alarak etkinliğin boş kalmasını engeller.