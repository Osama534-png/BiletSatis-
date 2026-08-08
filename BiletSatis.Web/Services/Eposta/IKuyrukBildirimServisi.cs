namespace BiletSatis.Web.Services.Eposta;

public interface IKuyrukBildirimServisi
{
    /// <summary>
    /// Hakkı tanınmış ama bildirimi gönderilmemiş kuyruk kayıtlarına
    /// "sıran geldi" e-postası gönderir. Gönderilen bildirim sayısını döner.
    /// </summary>
    Task<int> BekleyenBildirimleriGonderAsync(CancellationToken ct = default);
}
