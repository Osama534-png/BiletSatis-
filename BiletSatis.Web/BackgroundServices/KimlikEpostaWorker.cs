using BiletSatis.Web.Services.Eposta;

namespace BiletSatis.Web.BackgroundServices;

/// <summary>
/// Kuyruğa bırakılan kimlik e-postalarını (doğrulama, şifre sıfırlama, adres
/// değişikliği onayı) gönderir. Amaç, SMTP beklemesini kullanıcının isteğinden
/// çıkarmak: kayıt olan kişi cevabı e-posta sunucusu dönene kadar beklemesin.
///
/// Gönderim hatası akışı etkilemez, yalnızca loglanır — kullanıcı bağlantıyı
/// arayüzden yeniden isteyebilir.
/// </summary>
public class KimlikEpostaWorker : BackgroundService
{
    private readonly IKimlikEpostaKuyrugu _kuyruk;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KimlikEpostaWorker> _logger;

    public KimlikEpostaWorker(
        IKimlikEpostaKuyrugu kuyruk,
        IServiceScopeFactory scopeFactory,
        ILogger<KimlikEpostaWorker> logger)
    {
        _kuyruk = kuyruk;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var is_ in _kuyruk.OkuAsync(stoppingToken))
        {
            try
            {
                using var kapsam = _scopeFactory.CreateScope();
                var servis = kapsam.ServiceProvider.GetRequiredService<IKimlikEpostaServisi>();

                var gorev = is_.Tur switch
                {
                    KimlikEpostaTuru.Dogrulama => servis.DogrulamaGonderAsync(is_.Alici, is_.Ad, is_.Adres, stoppingToken),
                    KimlikEpostaTuru.SifreSifirlama => servis.SifirlamaGonderAsync(is_.Alici, is_.Ad, is_.Adres, stoppingToken),
                    _ => servis.DegisiklikGonderAsync(is_.Alici, is_.Ad, is_.Adres, stoppingToken)
                };

                await gorev;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Kimlik e-postası gönderilemedi: Tur={Tur} Alici={Alici}", is_.Tur, is_.Alici);
            }
        }
    }
}
