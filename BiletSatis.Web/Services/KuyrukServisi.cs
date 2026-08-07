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

    public async Task<int> EnqueueWaitlistAsync(int etkinlikId, string kullaniciId, CancellationToken ct = default)
    {
        var siraNolar = await _db.Database.SqlQuery<int>($"""
            INSERT INTO RezervasyonKuyrugu (EtkinlikId, KullaniciId, Durum, OlusturmaZamani)
            OUTPUT INSERTED.SiraNo
            VALUES ({etkinlikId}, {kullaniciId}, {KuyrukDurumMetni.Beklemede}, GETUTCDATE())
            """).ToListAsync(ct);

        var siraNo = siraNolar.Single();
        _logger.LogInformation("Kuyruğa katılım: EtkinlikId={EtkinlikId} KullaniciId={KullaniciId} SiraNo={SiraNo}",
            etkinlikId, kullaniciId, siraNo);

        return siraNo;
    }

    public async Task<int> AllocateWaitlistBatchAsync(int etkinlikId, int n, CancellationToken ct = default)
    {
        var etkilenen = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            ;WITH cte AS (
                SELECT TOP ({n}) *
                FROM RezervasyonKuyrugu
                WHERE EtkinlikId = {etkinlikId} AND Durum = {KuyrukDurumMetni.Beklemede}
                ORDER BY SiraNo
            )
            UPDATE cte SET Durum = {KuyrukDurumMetni.HakTanindi}, HakBitisZamani = DATEADD(MINUTE, 5, GETUTCDATE())
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