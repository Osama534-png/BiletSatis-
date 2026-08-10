using System.Threading.Channels;

namespace BiletSatis.Web.Services.Eposta;

/// <summary>Kuyruğa alınmış bir kimlik e-postası: hangi gönderim, kime, hangi adresle.</summary>
/// <param name="Tur">Doğrulama, şifre sıfırlama ya da adres değişikliği onayı.</param>
public record KimlikEpostaIsi(KimlikEpostaTuru Tur, string Alici, string Ad, string Adres);

public enum KimlikEpostaTuru { Dogrulama, SifreSifirlama, AdresDegisikligi }

public interface IKimlikEpostaKuyrugu
{
    /// <summary>İşi kuyruğa bırakır ve hemen döner; SMTP beklenmez.</summary>
    void Kuyruklat(KimlikEpostaIsi is_);

    IAsyncEnumerable<KimlikEpostaIsi> OkuAsync(CancellationToken ct);
}

/// <summary>
/// Kayıt, şifre sıfırlama ve adres değişikliği e-postalarını isteğin dışına taşıyan
/// süreç içi kuyruk.
///
/// Bu e-postalar önce doğrudan controller içinde, <c>await</c> ile gönderiliyordu:
/// kullanıcı "Kayıt Ol"a bastığında cevabı SMTP sunucusu dönene kadar bekliyordu.
/// Yük testinde ölçüldü — <c>POST /Account/KayitOl</c> 200 eşzamanlı kullanıcıda
/// 4-17 saniye sürüyordu ve yavaşlığın tamamı SMTP beklemesiydi.
///
/// Bu, projenin bildirimler için zaten uyguladığı ilkenin aynısı (bkz.
/// <see cref="BildirimWorker"/>): dış servis beklemesi kullanıcının isteğine
/// bindirilmez. Farkı, bunların veritabanına yazılmaması — kaybolursa kullanıcı
/// bağlantıyı yeniden isteyebiliyor ve akış zaten bunu destekliyor
/// (<c>DogrulamaTekrarGonder</c>). Eski hâlde de gönderim hatası yalnızca
/// loglanıyordu, yani kaybolabilen bir gönderimdi; burada zayıflayan bir garanti yok.
/// </summary>
public class KimlikEpostaKuyrugu : IKimlikEpostaKuyrugu
{
    // Sınırlı kapasite: bir anda çok sayıda kayıt gelirse kuyruk sonsuz büyüyüp
    // belleği tüketmesin. Dolduğunda en eski iş düşer — kullanıcı bağlantıyı
    // yeniden isteyebilir, ama uygulama ayakta kalır.
    private static readonly BoundedChannelOptions Secenekler = new(1000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true
    };

    private readonly Channel<KimlikEpostaIsi> _kanal = Channel.CreateBounded<KimlikEpostaIsi>(Secenekler);

    public void Kuyruklat(KimlikEpostaIsi is_) => _kanal.Writer.TryWrite(is_);

    public IAsyncEnumerable<KimlikEpostaIsi> OkuAsync(CancellationToken ct) =>
        _kanal.Reader.ReadAllAsync(ct);
}
