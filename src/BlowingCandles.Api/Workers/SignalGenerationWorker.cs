using System.Diagnostics;
using BlowingCandles.Application.Services;
using Microsoft.Extensions.Options;

namespace BlowingCandles.Api.Workers;

public sealed class SignalGenerationWorker : BackgroundService
{
    private const string WorkerTrigger = "worker";

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly WorkerOptions _options;
    private readonly ILogger<SignalGenerationWorker> _logger;

    public SignalGenerationWorker(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<WorkerOptions> options,
        ILogger<SignalGenerationWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _serviceScopeFactory = serviceScopeFactory;
        _options = options.Value ?? new WorkerOptions();
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (_options.IntervalMinutes < 1)
            {
                _logger.LogWarning(
                    "Signal generation worker interval must be at least 1 minute. Current value: {IntervalMinutes}. Worker will remain idle.",
                    _options.IntervalMinutes);
                await WaitForShutdownAsync(stoppingToken);
                return;
            }

            _logger.LogInformation(
                "Signal generation worker started with interval {IntervalMinutes} minutes.",
                _options.IntervalMinutes);

            var interval = TimeSpan.FromMinutes(_options.IntervalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                var stopwatch = Stopwatch.StartNew();
                ExecuteRun();
                stopwatch.Stop();

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                var remainingDelay = interval - stopwatch.Elapsed;
                if (remainingDelay <= TimeSpan.Zero)
                {
                    continue;
                }

                try
                {
                    await Task.Delay(remainingDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            _logger.LogInformation("Signal generation worker stopped.");
        }
    }

    private void ExecuteRun()
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var executionService = scope.ServiceProvider.GetRequiredService<ISignalRunExecutionService>();
            var result = executionService.RunRealtime(WorkerTrigger);

            _logger.LogInformation(
                "Signal generation worker completed run {RunId} with status {Status}.",
                result.RunId,
                result.Status);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Signal generation worker failed to execute a realtime run.");
        }
    }

    private static async Task WaitForShutdownAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
