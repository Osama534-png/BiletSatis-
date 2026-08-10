namespace BiletSatis.Web.Services;

public interface IKuyrukServisi
{
    /// <summary>
    /// Kullanıcıyı kuyruğa alır ve sıra numarasını döner. Kullanıcının bu etkinlikte
    /// zaten aktif bir kaydı varsa yeni kayıt açılmaz ve <c>null</c> dönülür.
    /// </summary>
    Task<int?> EnqueueWaitlistAsync(int etkinlikId, string kullaniciId, CancellationToken ct = default);
    Task<int> AllocateWaitlistBatchAsync(int etkinlikId, int n, CancellationToken ct = default);
    Task<int> PromoteExpiredAndFillAsync(int etkinlikId, CancellationToken ct = default);

    /// <summary>
    /// Süresi dolan hakları tüm etkinliklerde tek sorguda kapatır ve boşalan yerleri
    /// sıradakilere devreder.
    ///
    /// Arka plan görevi önce etkinlikleri listeleyip her biri için ayrı ayrı
    /// <see cref="PromoteExpiredAndFillAsync"/> çağırıyordu: 2000 etkinlikte her turda
    /// 4000'den fazla sorgu, üstelik neredeyse her zaman yapacak iş olmadan. Süresi
    /// dolan hak nadir olduğu için tarama tek sorguya iniyor; devretme yalnızca
    /// gerçekten boşalan etkinlikler için çalışıyor.
    /// </summary>
    Task<int> PromoteExpiredAndFillAllAsync(CancellationToken ct = default);
    Task<bool> CompleteQueueEntryAsync(int siraNo, string kullaniciId, CancellationToken ct = default);
}
