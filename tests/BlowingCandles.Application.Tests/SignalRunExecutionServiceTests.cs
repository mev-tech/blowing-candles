using System.Collections.Concurrent;
using BlowingCandles.Application.Services;
using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Models;
using BlowingCandles.Infrastructure.Config;
using BlowingCandles.Infrastructure.Persistence.Entities;
using BlowingCandles.Infrastructure.Persistence.Models;
using TradingAction = BlowingCandles.Domain.Enums.Action;
using NewsState = BlowingCandles.Domain.Enums.NewsState;

namespace BlowingCandles.Application.Tests;

public sealed class SignalRunExecutionServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SignalRunExecutionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"blowing-candles-execution-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void RunAsOf_PersistsSimulationRun()
    {
        var asOfDate = new DateOnly(2026, 1, 15);
        var config = CreateConfig(["AAPL"], maxBuysPerDay: 5);
        var persistRequests = new List<PersistSignalRunRequest>();
        var stateStore = new InMemoryTradeGovernorStateStore();
        var provider = new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>>(StringComparer.Ordinal)
        {
            ["AAPL"] = CreateBuyHistory(asOfDate)
        });
        var calendar = new FakeCalendar(new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal)
        {
            ["AAPL"] = new DateTimeOffset(2026, 1, 22, 0, 0, 0, TimeSpan.Zero)
        });
        var service = CreateService(
            config,
            request =>
            {
                persistRequests.Add(request);
                return new SignalRunPersistenceResult(41L, SignalRunStatus.Completed);
            },
            stateStore,
            _ => calendar,
            _ => provider);

        var before = DateTimeOffset.UtcNow;
        var result = service.RunAsOf("cli", asOfDate);
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(SignalRunStatus.Completed, result.Status);
        Assert.Equal(41L, result.RunId);
        Assert.True(result.IsSimulation);
        Assert.Equal(SignalRunType.AsOf, result.RunType);
        Assert.Equal(1, result.TickerCount);

        var request = Assert.Single(persistRequests);
        Assert.Equal(SignalRunType.AsOf, request.RunType);
        Assert.True(request.IsSimulation);
        Assert.Equal("simulation", request.GovernorStateMode);
        Assert.NotNull(request.GovernorState);
        Assert.Equal("2026-01-15", request.GovernorState!.Day);
        Assert.Equal(1, request.GovernorState.BuysToday);
        Assert.Equal([new PriceRequest("AAPL", asOfDate)], provider.PriceRequests);
        AssertWallClockTimestamps(result.StartedAtUtc, result.CompletedAtUtc, before, after);
        AssertWallClockTimestamps(request.StartedAtUtc, request.CompletedAtUtc, before, after);
    }

    [Fact]
    public void RunRealtime_PersistsLiveRun()
    {
        var now = new DateTimeOffset(2026, 3, 7, 14, 30, 0, TimeSpan.Zero);
        var asOfDate = DateOnly.FromDateTime(now.UtcDateTime);
        var config = CreateConfig(["AAPL"], maxBuysPerDay: 5);
        var persistRequests = new List<PersistSignalRunRequest>();
        var stateStore = new InMemoryTradeGovernorStateStore();
        var provider = new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>>(StringComparer.Ordinal)
        {
            ["AAPL"] = CreateBuyHistory(asOfDate)
        });
        var calendar = new FakeCalendar(new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal)
        {
            ["AAPL"] = now.AddDays(7)
        });
        var service = CreateService(
            config,
            request =>
            {
                persistRequests.Add(request);
                return new SignalRunPersistenceResult(7L, SignalRunStatus.Completed);
            },
            stateStore,
            _ => calendar,
            _ => provider,
            realtimeClockFactory: () => new FixedClock(now));

        var before = DateTimeOffset.UtcNow;
        var result = service.RunRealtime("worker");
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(SignalRunStatus.Completed, result.Status);
        Assert.Equal(7L, result.RunId);
        Assert.False(result.IsSimulation);
        Assert.Equal(asOfDate, result.AsOfDate);

        var request = Assert.Single(persistRequests);
        Assert.Equal(SignalRunType.Realtime, request.RunType);
        Assert.Equal("worker", request.Trigger);
        Assert.False(request.IsSimulation);
        Assert.Equal("live", request.GovernorStateMode);
        Assert.NotNull(request.GovernorState);
        Assert.Equal("2026-03-07", request.GovernorState!.Day);
        Assert.Equal(1, request.GovernorState.BuysToday);
        AssertWallClockTimestamps(result.StartedAtUtc, result.CompletedAtUtc, before, after);
        AssertWallClockTimestamps(request.StartedAtUtc, request.CompletedAtUtc, before, after);
    }

    [Fact]
    public void RunRange_UsesSharedSimulationStateAcrossDays()
    {
        var startDate = new DateOnly(2026, 1, 15);
        var endDate = new DateOnly(2026, 1, 16);
        var config = CreateConfig(["MSFT", "AAPL"], maxBuysPerDay: 1);
        var persistRequests = new List<PersistSignalRunRequest>();
        var stateStore = new InMemoryTradeGovernorStateStore();
        var provider = new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>>(StringComparer.Ordinal)
        {
            ["AAPL"] = CreateBuyHistory(startDate),
            ["MSFT"] = CreateBuyHistory(startDate)
        });
        var calendar = new FakeCalendar(new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal)
        {
            ["AAPL"] = new DateTimeOffset(2026, 1, 25, 0, 0, 0, TimeSpan.Zero),
            ["MSFT"] = new DateTimeOffset(2026, 1, 25, 0, 0, 0, TimeSpan.Zero)
        });
        var service = CreateService(
            config,
            request =>
            {
                persistRequests.Add(request);
                return new SignalRunPersistenceResult(persistRequests.Count, SignalRunStatus.Completed);
            },
            stateStore,
            _ => calendar,
            _ => provider);

        var before = DateTimeOffset.UtcNow;
        var results = service.RunRange("cli", startDate, endDate);
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.Equal(SignalRunStatus.Completed, result.Status));
        Assert.Equal(2, stateStore.SaveCount);
        Assert.Equal(["2026-01-15", "2026-01-16"], persistRequests.Select(x => x.GovernorState!.Day).ToArray());
        Assert.Equal(
            [
                new PriceRequest("MSFT", startDate),
                new PriceRequest("AAPL", startDate),
                new PriceRequest("MSFT", endDate),
                new PriceRequest("AAPL", endDate)
            ],
            provider.PriceRequests);
        Assert.All(results, result => AssertWallClockTimestamps(result.StartedAtUtc, result.CompletedAtUtc, before, after));
        Assert.All(persistRequests, request => AssertWallClockTimestamps(request.StartedAtUtc, request.CompletedAtUtc, before, after));
    }

    [Fact]
    public void RunRealtime_PipelineThrows_PersistsFailedRun()
    {
        var config = CreateConfig(["AAPL"], maxBuysPerDay: 5);
        var persistRequests = new List<PersistSignalRunRequest>();
        var service = CreateService(
            config,
            request =>
            {
                persistRequests.Add(request);
                return new SignalRunPersistenceResult(99L, SignalRunStatus.Failed);
            },
            new InMemoryTradeGovernorStateStore(),
            _ => throw new InvalidOperationException("calendar exploded"),
            _ => new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>>(StringComparer.Ordinal)));

        var before = DateTimeOffset.UtcNow;
        var result = service.RunRealtime("api");
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(SignalRunStatus.Failed, result.Status);
        Assert.Equal(99L, result.RunId);
        Assert.Equal("calendar exploded", result.ErrorMessage);
        Assert.Empty(result.Signals);

        var request = Assert.Single(persistRequests);
        Assert.Equal(SignalRunType.Realtime, request.RunType);
        Assert.Equal("api", request.Trigger);
        Assert.Empty(request.Signals);
        Assert.Equal("calendar exploded", request.ErrorMessage);
        AssertWallClockTimestamps(result.StartedAtUtc, result.CompletedAtUtc, before, after);
        AssertWallClockTimestamps(request.StartedAtUtc, request.CompletedAtUtc, before, after);
    }

    [Fact]
    public void RunRealtime_PersistenceFails_ReturnsFailedResult()
    {
        var now = new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.Zero);
        var config = CreateConfig(["AAPL"], maxBuysPerDay: 5);
        var service = CreateService(
            config,
            _ => throw new InvalidOperationException("db unavailable"),
            new InMemoryTradeGovernorStateStore(),
            _ => new FakeCalendar(new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal)
            {
                ["AAPL"] = now.AddDays(7)
            }),
            _ => new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>>(StringComparer.Ordinal)
            {
                ["AAPL"] = CreateBuyHistory(DateOnly.FromDateTime(now.UtcDateTime))
            }),
            realtimeClockFactory: () => new FixedClock(now));

        var before = DateTimeOffset.UtcNow;
        var result = service.RunRealtime("cli");
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(SignalRunStatus.Failed, result.Status);
        Assert.Equal(0L, result.RunId);
        Assert.Equal("db unavailable", result.ErrorMessage);
        Assert.Equal(1, result.TickerCount);
        AssertWallClockTimestamps(result.StartedAtUtc, result.CompletedAtUtc, before, after);
    }

    [Fact]
    public void RunRealtime_EmptyWatchlist_ReturnsCompletedWithZeroSignals()
    {
        var now = new DateTimeOffset(2026, 3, 9, 9, 0, 0, TimeSpan.Zero);
        var config = CreateConfig([], maxBuysPerDay: 5);
        var persistRequests = new List<PersistSignalRunRequest>();
        var stateStore = new InMemoryTradeGovernorStateStore();
        var provider = new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>>(StringComparer.Ordinal));
        var service = CreateService(
            config,
            request =>
            {
                persistRequests.Add(request);
                return new SignalRunPersistenceResult(12L, SignalRunStatus.Completed);
            },
            stateStore,
            _ => new FakeCalendar(new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal)),
            _ => provider,
            realtimeClockFactory: () => new FixedClock(now));

        var before = DateTimeOffset.UtcNow;
        var result = service.RunRealtime("cli");
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(SignalRunStatus.Completed, result.Status);
        Assert.Equal(12L, result.RunId);
        Assert.Equal(0, result.TickerCount);
        Assert.Empty(result.Signals);
        Assert.Equal(0, stateStore.SaveCount);
        Assert.Empty(provider.PriceRequests);

        var request = Assert.Single(persistRequests);
        Assert.Empty(request.Signals);
        Assert.Equal("2026-03-09", request.GovernorState!.Day);
        Assert.Equal(0, request.GovernorState.BuysToday);
        AssertWallClockTimestamps(result.StartedAtUtc, result.CompletedAtUtc, before, after);
        AssertWallClockTimestamps(request.StartedAtUtc, request.CompletedAtUtc, before, after);
    }

    [Fact]
    public async Task RunRealtime_ConcurrentCalls_AreSerialized()
    {
        var now = new DateTimeOffset(2026, 3, 9, 10, 0, 0, TimeSpan.Zero);
        var asOfDate = DateOnly.FromDateTime(now.UtcDateTime);
        var config = CreateConfig(["AAPL"], maxBuysPerDay: 5);
        var persistRequests = new ConcurrentQueue<PersistSignalRunRequest>();
        using var firstRunEntered = new ManualResetEventSlim();
        using var releaseFirstRun = new ManualResetEventSlim();

        var providerFactoryInvocationCount = 0;
        var provider = new BlockingMarketDataProvider(
            new Dictionary<string, IReadOnlyList<PriceBar>>(StringComparer.Ordinal)
            {
                ["AAPL"] = CreateBuyHistory(asOfDate)
            },
            blockedAsOfDate: asOfDate,
            onBlockedRequestEntered: firstRunEntered.Set,
            blockedRequestGate: releaseFirstRun);
        var service = CreateService(
            config,
            request =>
            {
                persistRequests.Enqueue(request);
                return new SignalRunPersistenceResult(persistRequests.Count, SignalRunStatus.Completed);
            },
            new InMemoryTradeGovernorStateStore(),
            _ => new FakeCalendar(new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal)
            {
                ["AAPL"] = now.AddDays(7)
            }),
            _ =>
            {
                Interlocked.Increment(ref providerFactoryInvocationCount);
                return provider;
            },
            realtimeClockFactory: () => new FixedClock(now));

        var firstTask = Task.Run(() => service.RunRealtime("worker"));
        Task<SignalRunExecutionResult>? secondTask = null;

        try
        {
            Assert.True(firstRunEntered.Wait(TimeSpan.FromSeconds(5)));

            secondTask = Task.Run(() => service.RunRealtime("worker"));

            var completedTask = await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromMilliseconds(200)));
            Assert.NotSame(secondTask, completedTask);
            Assert.Equal(1, Volatile.Read(ref providerFactoryInvocationCount));
            Assert.Equal(1, provider.TotalRequestCount);
        }
        finally
        {
            releaseFirstRun.Set();
        }

        var results = await Task.WhenAll(firstTask, secondTask!);

        Assert.All(results, result => Assert.Equal(SignalRunStatus.Completed, result.Status));
        Assert.Equal(2, Volatile.Read(ref providerFactoryInvocationCount));
        Assert.Equal(2, provider.TotalRequestCount);
        Assert.Equal(2, persistRequests.Count);
    }

    [Fact]
    public async Task RunRealtime_SimulationDoesNotBlockLive()
    {
        var realtimeNow = new DateTimeOffset(2026, 3, 9, 11, 0, 0, TimeSpan.Zero);
        var realtimeAsOfDate = DateOnly.FromDateTime(realtimeNow.UtcDateTime);
        var simulationAsOfDate = new DateOnly(2026, 1, 15);
        var config = CreateConfig(["AAPL"], maxBuysPerDay: 5);
        var persistRequests = new ConcurrentQueue<PersistSignalRunRequest>();
        using var simulationEntered = new ManualResetEventSlim();
        using var liveEntered = new ManualResetEventSlim();
        using var releaseSimulation = new ManualResetEventSlim();

        var provider = new BlockingMarketDataProvider(
            new Dictionary<string, IReadOnlyList<PriceBar>>(StringComparer.Ordinal)
            {
                ["AAPL"] = CreateBuyHistory(realtimeAsOfDate)
            },
            blockedAsOfDate: simulationAsOfDate,
            onBlockedRequestEntered: simulationEntered.Set,
            blockedRequestGate: releaseSimulation,
            onNonBlockedRequestEntered: liveEntered.Set);

        var liveStateStore = new InMemoryTradeGovernorStateStore();
        var simulationStateStore = new InMemoryTradeGovernorStateStore();
        var service = CreateService(
            config,
            request =>
            {
                persistRequests.Enqueue(request);
                return new SignalRunPersistenceResult(persistRequests.Count, SignalRunStatus.Completed);
            },
            mode => string.Equals(mode, "live", StringComparison.Ordinal)
                ? liveStateStore
                : simulationStateStore,
            _ => new FakeCalendar(new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal)
            {
                ["AAPL"] = realtimeNow.AddDays(7)
            }),
            _ => provider,
            realtimeClockFactory: () => new FixedClock(realtimeNow));

        var simulationTask = Task.Run(() => service.RunAsOf("cli", simulationAsOfDate));

        try
        {
            Assert.True(simulationEntered.Wait(TimeSpan.FromSeconds(5)));

            var liveTask = Task.Run(() => service.RunRealtime("worker"));

            Assert.True(liveEntered.Wait(TimeSpan.FromSeconds(5)));
            var liveResult = await liveTask;
            Assert.Equal(SignalRunStatus.Completed, liveResult.Status);
            Assert.Single(persistRequests, request => request.RunType == SignalRunType.Realtime);
        }
        finally
        {
            releaseSimulation.Set();
        }

        var simulationResult = await simulationTask;
        Assert.Equal(SignalRunStatus.Completed, simulationResult.Status);
        Assert.Single(persistRequests, request => request.RunType == SignalRunType.AsOf);
        Assert.Equal(2, persistRequests.Count);
    }

    private AppConfig CreateConfig(string[] watchlist, int maxBuysPerDay)
    {
        return new AppConfig
        {
            Watchlist = watchlist,
            News = new NewsConfig
            {
                LocalEarningsCalendar = "earnings_calendar.json",
                ResolvedLocalEarningsCalendar = GetPath("earnings_calendar.json"),
                BlockWindowHours = 48
            },
            Policy = new PolicyConfig
            {
                MaxBuysPerDay = maxBuysPerDay,
                CooldownMinutes = 0
            }
        };
    }

    private SignalRunExecutionService CreateService(
        AppConfig config,
        Func<PersistSignalRunRequest, SignalRunPersistenceResult> persistRun,
        ITradeGovernorStateStore stateStore,
        Func<AppConfig, IEarningsCalendar> earningsCalendarFactory,
        Func<AppConfig, IMarketDataProvider> marketDataProviderFactory,
        Func<IClock>? realtimeClockFactory = null)
    {
        return CreateService(
            config,
            persistRun,
            _ => stateStore,
            earningsCalendarFactory,
            marketDataProviderFactory,
            realtimeClockFactory);
    }

    private SignalRunExecutionService CreateService(
        AppConfig config,
        Func<PersistSignalRunRequest, SignalRunPersistenceResult> persistRun,
        Func<string, ITradeGovernorStateStore> governorStateStoreFactory,
        Func<AppConfig, IEarningsCalendar> earningsCalendarFactory,
        Func<AppConfig, IMarketDataProvider> marketDataProviderFactory,
        Func<IClock>? realtimeClockFactory = null,
        Func<DateOnly, IClock>? simulationClockFactory = null)
    {
        return new SignalRunExecutionService(
            config,
            persistRun,
            governorStateStoreFactory,
            earningsCalendarFactory,
            marketDataProviderFactory,
            realtimeClockFactory: realtimeClockFactory,
            simulationClockFactory: simulationClockFactory);
    }

    private string GetPath(string relativePath)
    {
        return Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static IReadOnlyList<PriceBar> CreateBuyHistory(DateOnly asOfDate)
    {
        var bars = new List<PriceBar>(capacity: 200);
        var startDate = asOfDate.AddDays(-199);

        for (var index = 0; index < 186; index++)
        {
            bars.Add(CreateBar(startDate.AddDays(index), 100m + index));
        }

        for (var index = 186; index < 200; index++)
        {
            bars.Add(CreateBar(startDate.AddDays(index), 470m - index));
        }

        return bars;
    }

    private static PriceBar CreateBar(DateOnly day, decimal close)
    {
        var timestamp = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        return new PriceBar(timestamp, close, close, close, close, 1_000L);
    }

    private static void AssertWallClockTimestamps(
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        DateTimeOffset before,
        DateTimeOffset after)
    {
        Assert.InRange(startedAtUtc, before, after);
        Assert.InRange(completedAtUtc, startedAtUtc, after);
    }

    private sealed class InMemoryTradeGovernorStateStore : ITradeGovernorStateStore
    {
        private TradeGovernorState? _state;

        public int SaveCount { get; private set; }

        public TradeGovernorState Load(IClock clock)
        {
            var currentDay = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).ToString("yyyy-MM-dd");

            if (_state is null || !string.Equals(_state.Day, currentDay, StringComparison.Ordinal))
            {
                _state = new TradeGovernorState(currentDay, 0, null);
            }

            return _state;
        }

        public void Save(TradeGovernorState state)
        {
            _state = state;
            SaveCount++;
        }
    }

    private sealed class FakeCalendar : IEarningsCalendar
    {
        private readonly IReadOnlyDictionary<string, DateTimeOffset?> _datesByTicker;

        public FakeCalendar(IReadOnlyDictionary<string, DateTimeOffset?> datesByTicker)
        {
            _datesByTicker = datesByTicker;
        }

        public IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> Load()
        {
            return _datesByTicker.ToDictionary(
                entry => entry.Key,
                entry => entry.Value is { } date
                    ? (IReadOnlyList<DateTimeOffset>)[date]
                    : Array.Empty<DateTimeOffset>(),
                StringComparer.Ordinal);
        }

        public DateTimeOffset? GetNextFutureEarningsDate(string ticker, DateTimeOffset referenceTimeUtc)
        {
            return _datesByTicker.TryGetValue(ticker, out var date) && date > referenceTimeUtc
                ? date
                : null;
        }
    }

    private sealed class FakeMarketDataProvider : IMarketDataProvider
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<PriceBar>> _priceHistoryByTicker;

        public FakeMarketDataProvider(IReadOnlyDictionary<string, IReadOnlyList<PriceBar>> priceHistoryByTicker)
        {
            _priceHistoryByTicker = priceHistoryByTicker;
        }

        public List<PriceRequest> PriceRequests { get; } = [];

        public IReadOnlyList<PriceBar> GetDailyPriceHistory(string ticker, DateOnly asOfDate)
        {
            PriceRequests.Add(new PriceRequest(ticker, asOfDate));
            return _priceHistoryByTicker[ticker];
        }

        public DateTimeOffset? GetNextEarningsDate(string ticker, DateTimeOffset asOfUtc)
        {
            _ = ticker;
            _ = asOfUtc;
            return null;
        }
    }

    private sealed class BlockingMarketDataProvider : IMarketDataProvider
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<PriceBar>> _priceHistoryByTicker;
        private readonly DateOnly _blockedAsOfDate;
        private readonly Action? _onBlockedRequestEntered;
        private readonly ManualResetEventSlim _blockedRequestGate;
        private readonly Action? _onNonBlockedRequestEntered;
        private int _blockedRequestEntered;
        private int _totalRequestCount;

        public BlockingMarketDataProvider(
            IReadOnlyDictionary<string, IReadOnlyList<PriceBar>> priceHistoryByTicker,
            DateOnly blockedAsOfDate,
            Action? onBlockedRequestEntered,
            ManualResetEventSlim blockedRequestGate,
            Action? onNonBlockedRequestEntered = null)
        {
            _priceHistoryByTicker = priceHistoryByTicker;
            _blockedAsOfDate = blockedAsOfDate;
            _onBlockedRequestEntered = onBlockedRequestEntered;
            _blockedRequestGate = blockedRequestGate;
            _onNonBlockedRequestEntered = onNonBlockedRequestEntered;
        }

        public int TotalRequestCount => Volatile.Read(ref _totalRequestCount);

        public IReadOnlyList<PriceBar> GetDailyPriceHistory(string ticker, DateOnly asOfDate)
        {
            Interlocked.Increment(ref _totalRequestCount);

            if (asOfDate == _blockedAsOfDate && Interlocked.CompareExchange(ref _blockedRequestEntered, 1, 0) == 0)
            {
                _onBlockedRequestEntered?.Invoke();
                Assert.True(_blockedRequestGate.Wait(TimeSpan.FromSeconds(5)));
            }
            else
            {
                _onNonBlockedRequestEntered?.Invoke();
            }

            return _priceHistoryByTicker[ticker];
        }

        public DateTimeOffset? GetNextEarningsDate(string ticker, DateTimeOffset asOfUtc)
        {
            _ = ticker;
            _ = asOfUtc;
            return null;
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed record PriceRequest(string Ticker, DateOnly AsOfDate);
}
