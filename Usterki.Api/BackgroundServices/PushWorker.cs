using Usterki.Infrastructure.Notifications;

namespace Usterki.Api.BackgroundServices;

public sealed class PushWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PushWorker> _logger;

    public PushWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<PushWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue<bool>("Push:Enabled"))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;

            try
            {
                using var scope = _scopeFactory.CreateScope();

                var processor = scope.ServiceProvider
                    .GetRequiredService<PushOutboxProcessor>();

                processed = await processor.ProcessNextAsync(
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    "Błąd procesu push: {ErrorType}.",
                    exception.GetType().Name);
            }

            try
            {
                await Task.Delay(
                    processed
                        ? TimeSpan.FromMilliseconds(100)
                        : TimeSpan.FromSeconds(5),
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}