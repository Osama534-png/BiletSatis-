namespace BiletSatis.Web.Services.Eposta;

public interface IBiletBildirimServisi
{
    /// <summary>
    /// Satılmış ama bildirimi gönderilmemiş biletler için satın alma
    /// e-postası gönderir. Gönderilen bildirim sayısını döner.
    /// </summary>
    Task<int> BekleyenBildirimleriGonderAsync(CancellationToken ct = default);
}
