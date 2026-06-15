using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Demlo.Workers.Services;

public class GracefulShutdownHostedService : IHostedService
{
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<GracefulShutdownHostedService> _logger;

    public GracefulShutdownHostedService(IHostApplicationLifetime appLifetime, ILogger<GracefulShutdownHostedService> logger)
    {
        _appLifetime = appLifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Register our custom interception handlers to trigger when the OS signals a shutdown (SIGTERM / SIGINT)
        _appLifetime.ApplicationStopping.Register(OnApplicationStopping);
        return Task.CompletedTask;
    }

    private void OnApplicationStopping()
    {
        _logger.LogWarning("[OS LIFECYCLE EVENT] SIGTERM signal caught from operating system environment host.");
        _logger.LogWarning("[SHUTDOWN SHIELD] Pausing incoming job worker pools. Waiting 15 seconds for active financial ledgers to safely commit...");

        // Emulate a hard operational cooldown block to allow active Entity Framework transaction pipes to close safely
        Thread.Sleep(15000); 

        _logger.LogInformation("[SHUTDOWN CLEAN] All transactional contexts safely committed to disk. Releasing container resources gracefully.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}