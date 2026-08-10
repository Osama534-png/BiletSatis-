using BiletSatis.Web.Services;

namespace BiletSatis.Web.BackgroundServices;

public class WaitlistWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WaitlistWorker> _logger;

    public WaitlistWorker(IServiceScopeFactory scopeFactory, ILogger<WaitlistWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var kuyruk = scope.ServiceProvider.GetRequiredService<IKuyrukServisi>();

                // Önce bütün etkinlikler listelenip her biri için ayrı sorgu
                // çalıştırılıyordu; 2000 etkinlikte bu, her 15 saniyede 4000'den fazla
                // sorgu demekti ve turların neredeyse tamamında yapacak iş yoktu.
                await kuyruk.PromoteExpiredAndFillAllAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Arka plan görevi hata verdi: {WorkerName}", nameof(WaitlistWorker));
            }
        }
    }
}
