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
    Task<bool> CompleteQueueEntryAsync(int siraNo, string kullaniciId, CancellationToken ct = default);
}
