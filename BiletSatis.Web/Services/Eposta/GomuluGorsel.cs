namespace BiletSatis.Web.Services.Eposta;

/// <summary>
/// E-posta gövdesine gömülen görsel. HTML içinde "cid:{ContentId}" ile referans verilir.
/// Gmail gibi istemciler data: URI'leri engellediğinden QR kodu bu yolla eklenir.
/// </summary>
/// <param name="ContentId">HTML'deki cid referansı.</param>
/// <param name="DosyaAdi">İstemcide görünen dosya adı.</param>
/// <param name="Icerik">Görselin ham baytları.</param>
/// <param name="MimeTuru">Örn. "image/png".</param>
public record GomuluGorsel(string ContentId, string DosyaAdi, byte[] Icerik, string MimeTuru);
