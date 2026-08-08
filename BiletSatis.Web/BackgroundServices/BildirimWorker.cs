using BiletSatis.Web.Services.Eposta;

namespace BiletSatis.Web.BackgroundServices;

/// <summary>
/// Bekleyen e-posta bildirimlerini gönderir: kuyrukta sırası gelenlere
/// "sıran geldi", bilet satın alanlara "biletin hazır".
/// Gönderim, kuyruk ve ödeme işlemlerinden ayrı tutulur: atomik SQL
/// güncellemeleri e-posta sunucusunu beklemez ve gönderim hatası
/// hak tanımayı ya da ödemeyi geri almaz.
/// </summary>
public class BildirimWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BildirimWorker> _logger;

    public BildirimWorker(IServiceScopeFactory scopeFactory, ILogger<BildirimWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var kuyrukBildirimi = scope.ServiceProvider.GetRequiredService<IKuyrukBildirimServisi>();
                await kuyrukBildirimi.BekleyenBildirimleriGonderAsync(stoppingToken);

                var biletBildirimi = scope.ServiceProvider.GetRequiredService<IBiletBildirimServisi>();
                await biletBildirimi.BekleyenBildirimleriGonderAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Arka plan görevi hata verdi: {WorkerName}", nameof(BildirimWorker));
            }
        }
    }
}
