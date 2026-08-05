using BiletSatis.Web.Data;
using BiletSatis.Web.Services;
using Microsoft.EntityFrameworkCore;

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
                var db = scope.ServiceProvider.GetRequiredService<BiletSatisDbContext>();
                var kuyruk = scope.ServiceProvider.GetRequiredService<IKuyrukServisi>();

                var etkinlikIdler = await db.Etkinlikler.Select(e => e.Id).ToListAsync(stoppingToken);
                foreach (var etkinlikId in etkinlikIdler)
                {
                    await kuyruk.PromoteExpiredAndFillAsync(etkinlikId, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Arka plan görevi hata verdi: {WorkerName}", nameof(WaitlistWorker));
            }
        }
    }
}
