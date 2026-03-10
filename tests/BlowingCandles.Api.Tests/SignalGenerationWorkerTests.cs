using BlowingCandles.Api.Workers;
using BlowingCandles.Application.Services;
using BlowingCandles.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlowingCandles.Api.Tests;

public sealed class SignalGenerationWorkerTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public async Task ApiHost_RegistersWorkerOnlyWhenEnabled(string? enabledValue, bool expectWorker)
    {
        using var workspace = new TestWorkspace();
        var configurationOverrides = CreateConfigurationOverrides(enabledValue);

        await using var app = ApiHost.Build(
            [],
            workspace.GetPath("config.yaml"),
            configurationOverrides: configurationOverrides);

        var workers = app.Services.GetServices<IHostedService>()
            .OfType<SignalGenerationWorker>()
            .ToArray();

        Assert.Equal(expectWorker, workers.Length == 1);
    }

    [Fact]
    public async Task StartAsync_RunsRealtimeImmediatelyWithWorkerTrigger()
    {
        var executionService = new RecordingExecutionService();
        var scopeFactory = new RecordingServiceScopeFactory(executionService);
        var logger = new ListLogger<SignalGenerationWorker>();
        var worker = new SignalGenerationWorker(
            scopeFactory,
            Options.Create(new WorkerOptions { Enabled = true, IntervalMinutes = 1 }),
            logger);

        await worker.StartAsync(CancellationToken.None);
        await executionService.RealtimeInvocation.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(["worker"], executionService.RealtimeTriggers);
        Assert.Equal(1, scopeFactory.CreatedCount);
        Assert.Equal(1, scopeFactory.DisposedCount);
        Assert.Contains(
            logger.Entries,
            entry => entry.LogLevel == LogLevel.Information
                && entry.Message.Contains("started with interval 1 minutes", StringComparison.Ordinal));
        Assert.Contains(
            logger.Entries,
            entry => entry.LogLevel == LogLevel.Information
                && entry.Message.Contains("completed run 42 with status Completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_WithInvalidInterval_RemainsIdleUntilStopped()
    {
        var executionService = new RecordingExecutionService();
        var scopeFactory = new RecordingServiceScopeFactory(executionService);
        var logger = new ListLogger<SignalGenerationWorker>();
        var worker = new SignalGenerationWorker(
            scopeFactory,
            Options.Create(new WorkerOptions { Enabled = true, IntervalMinutes = 0 }),
            logger);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(executionService.RealtimeTriggers);
        Assert.Equal(0, scopeFactory.CreatedCount);
        Assert.Contains(
            logger.Entries,
            entry => entry.LogLevel == LogLevel.Warning
                && entry.Message.Contains("at least 1 minute", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_WhenExecutionThrows_LogsErrorAndStopsCleanly()
    {
        var executionService = new RecordingExecutionService(new InvalidOperationException("boom"));
        var scopeFactory = new RecordingServiceScopeFactory(executionService);
        var logger = new ListLogger<SignalGenerationWorker>();
        var worker = new SignalGenerationWorker(
            scopeFactory,
            Options.Create(new WorkerOptions { Enabled = true, IntervalMinutes = 1 }),
            logger);

        await worker.StartAsync(CancellationToken.None);
        await executionService.RealtimeInvocation.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(["worker"], executionService.RealtimeTriggers);
        Assert.Equal(1, scopeFactory.CreatedCount);
        Assert.Equal(1, scopeFactory.DisposedCount);
        Assert.Contains(
            logger.Entries,
            entry => entry.LogLevel == LogLevel.Error
                && entry.Exception is InvalidOperationException exception
                && exception.Message == "boom");
        Assert.Contains(
            logger.Entries,
            entry => entry.LogLevel == LogLevel.Information
                && entry.Message.Contains("worker stopped", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string?> CreateConfigurationOverrides(string? enabledValue)
    {
        var overrides = new Dictionary<string, string?>
        {
            ["ConnectionStrings:AppDb"] = "Host=127.0.0.1;Port=5432;Database=blowing_candles;Username=postgres;Password=postgres"
        };

        if (enabledValue is not null)
        {
            overrides["Worker:Enabled"] = enabledValue;
        }

        return overrides;
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"blowing-candles-worker-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
            WriteFile("config.yaml",
                """
                watchlist: []
                news:
                  local_earnings_calendar: earnings_calendar.json
                """);
            WriteFile("earnings_calendar.json", "{}" + Environment.NewLine);
        }

        public string RootPath { get; }

        public string GetPath(string relativePath)
        {
            return Path.Combine(RootPath, relativePath);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private void WriteFile(string relativePath, string contents)
        {
            var path = GetPath(relativePath);
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, contents);
        }
    }

    private sealed class RecordingExecutionService : ISignalRunExecutionService
    {
        private readonly Exception? _realtimeException;

        public RecordingExecutionService(Exception? realtimeException = null)
        {
            _realtimeException = realtimeException;
        }

        public TaskCompletionSource<object?> RealtimeInvocation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> RealtimeTriggers { get; } = [];

        public SignalRunExecutionResult RunRealtime(string trigger)
        {
            RealtimeTriggers.Add(trigger);
            RealtimeInvocation.TrySetResult(null);

            if (_realtimeException is not null)
            {
                throw _realtimeException;
            }

            var timestamp = new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);
            return new SignalRunExecutionResult(
                42,
                SignalRunType.Realtime,
                trigger,
                SignalRunStatus.Completed,
                new DateOnly(2026, 3, 9),
                false,
                timestamp,
                timestamp,
                0,
                [],
                null);
        }

        public SignalRunExecutionResult RunAsOf(string trigger, DateOnly asOfDate)
        {
            throw new NotSupportedException();
        }

        public IReadOnlyList<SignalRunExecutionResult> RunRange(string trigger, DateOnly startDate, DateOnly endDate)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingServiceScopeFactory : IServiceScopeFactory
    {
        private readonly RecordingServiceProvider _serviceProvider;

        public RecordingServiceScopeFactory(ISignalRunExecutionService executionService)
        {
            _serviceProvider = new RecordingServiceProvider(executionService);
        }

        public int CreatedCount { get; private set; }

        public int DisposedCount { get; private set; }

        public IServiceScope CreateScope()
        {
            CreatedCount++;
            return new RecordingServiceScope(_serviceProvider, () => DisposedCount++);
        }
    }

    private sealed class RecordingServiceScope : IServiceScope
    {
        private readonly Action _onDispose;

        public RecordingServiceScope(IServiceProvider serviceProvider, Action onDispose)
        {
            ServiceProvider = serviceProvider;
            _onDispose = onDispose;
        }

        public IServiceProvider ServiceProvider { get; }

        public void Dispose()
        {
            _onDispose();
        }
    }

    private sealed class RecordingServiceProvider : IServiceProvider
    {
        private readonly ISignalRunExecutionService _executionService;

        public RecordingServiceProvider(ISignalRunExecutionService executionService)
        {
            _executionService = executionService;
        }

        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(ISignalRunExecutionService)
                ? _executionService
                : null;
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_entries)
                {
                    return _entries.ToArray();
                }
            }
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NoOpDisposable.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
            }
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message, Exception? Exception);

    private sealed class NoOpDisposable : IDisposable
    {
        public static NoOpDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
