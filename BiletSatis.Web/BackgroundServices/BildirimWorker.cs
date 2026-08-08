using BiletSatis.Web.Services.Eposta;

namespace BiletSatis.Web.BackgroundServices;

/// <summary>
/// Hakkı tanınmış kullanıcılara "sıran geldi" e-postasını gönderir.
/// Gönderim, hak tanıma işleminden ayrı tutulur: atomik SQL güncellemesi
/// e-posta sunucusunu beklemez ve gönderim hatası hak tanımayı geri almaz.
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
                var bildirim = scope.ServiceProvider.GetRequiredService<IKuyrukBildirimServisi>();
                await bildirim.BekleyenBildirimleriGonderAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Arka plan görevi hata verdi: {WorkerName}", nameof(BildirimWorker));
            }
        }
    }
}
