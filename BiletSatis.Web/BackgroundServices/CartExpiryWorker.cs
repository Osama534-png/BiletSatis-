using BiletSatis.Web.Services;

namespace BiletSatis.Web.BackgroundServices;

public class CartExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CartExpiryWorker> _logger;

    public CartExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<CartExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var rezervasyon = scope.ServiceProvider.GetRequiredService<IBiletRezervasyonServisi>();
                var serbestKalan = await rezervasyon.ReleaseExpiredCartHoldsAsync(stoppingToken);

                if (serbestKalan > 0)
                {
                    _logger.LogInformation("Süresi dolan {Count} sepet kaydı serbest bırakıldı", serbestKalan);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Arka plan görevi hata verdi: {WorkerName}", nameof(CartExpiryWorker));
            }
        }
    }
}
