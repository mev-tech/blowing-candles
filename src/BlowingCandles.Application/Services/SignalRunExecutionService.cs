using System.Threading;
using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Models;
using BlowingCandles.Domain.Services;
using BlowingCandles.Infrastructure.Calendar;
using BlowingCandles.Infrastructure.Clock;
using BlowingCandles.Infrastructure.Config;
using BlowingCandles.Infrastructure.MarketData;
using BlowingCandles.Infrastructure.Persistence.Entities;
using BlowingCandles.Infrastructure.Persistence.Models;

namespace BlowingCandles.Application.Services;

public sealed class SignalRunExecutionService : ISignalRunExecutionService
{
    private const string LiveMode = "live";
    private const string SimulationMode = "simulation";

    private static readonly SemaphoreSlim LiveSemaphore = new(1, 1);
    private static readonly SemaphoreSlim SimulationSemaphore = new(1, 1);

    private readonly AppConfig _config;
    private readonly Func<PersistSignalRunRequest, SignalRunPersistenceResult> _persistRun;
    private readonly Func<string, ITradeGovernorStateStore> _governorStateStoreFactory;
    private readonly Func<AppConfig, IEarningsCalendar> _earningsCalendarFactory;
    private readonly Func<AppConfig, IMarketDataProvider> _marketDataProviderFactory;
    private readonly Func<IClock> _realtimeClockFactory;
    private readonly Func<DateOnly, IClock> _simulationClockFactory;
    private readonly Action<string>? _diagnosticWriter;

    public SignalRunExecutionService(
        AppConfig config,
        Func<PersistSignalRunRequest, SignalRunPersistenceResult> persistRun,
        Func<string, ITradeGovernorStateStore> governorStateStoreFactory,
        Func<AppConfig, IEarningsCalendar>? earningsCalendarFactory = null,
        Func<AppConfig, IMarketDataProvider>? marketDataProviderFactory = null,
        Func<IClock>? realtimeClockFactory = null,
        Func<DateOnly, IClock>? simulationClockFactory = null,
        Action<string>? diagnosticWriter = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(persistRun);
        ArgumentNullException.ThrowIfNull(governorStateStoreFactory);

        _config = config;
        _persistRun = persistRun;
        _governorStateStoreFactory = governorStateStoreFactory;
        _earningsCalendarFactory = earningsCalendarFactory ?? CreateEarningsCalendar;
        _marketDataProviderFactory = marketDataProviderFactory ?? CreateMarketDataProvider;
        _realtimeClockFactory = realtimeClockFactory ?? (() => new SystemClock());
        _simulationClockFactory = simulationClockFactory
            ?? (asOfDate => new FixedClock(new DateTimeOffset(asOfDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))));
        _diagnosticWriter = diagnosticWriter;
    }

    public SignalRunExecutionResult RunRealtime(string trigger)
    {
        var normalizedTrigger = NormalizeTrigger(trigger);
        return ExecuteSerialized(
            LiveSemaphore,
            () =>
            {
                var clock = _realtimeClockFactory();
                var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
                var stateStore = _governorStateStoreFactory(LiveMode);
                return ExecuteSingleRun(
                    SignalRunType.Realtime,
                    normalizedTrigger,
                    asOfDate,
                    isSimulation: false,
                    clock,
                    stateStore,
                    LiveMode);
            });
    }

    public SignalRunExecutionResult RunAsOf(string trigger, DateOnly asOfDate)
    {
        var normalizedTrigger = NormalizeTrigger(trigger);
        return ExecuteSerialized(
            SimulationSemaphore,
            () =>
            {
                var clock = _simulationClockFactory(asOfDate);
                var stateStore = _governorStateStoreFactory(SimulationMode);
                return ExecuteSingleRun(
                    SignalRunType.AsOf,
                    normalizedTrigger,
                    asOfDate,
                    isSimulation: true,
                    clock,
                    stateStore,
                    SimulationMode);
            });
    }

    public IReadOnlyList<SignalRunExecutionResult> RunRange(string trigger, DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
        {
            throw new ArgumentOutOfRangeException(nameof(endDate), "End date must be on or after the start date.");
        }

        var normalizedTrigger = NormalizeTrigger(trigger);

        return ExecuteSerialized(
            SimulationSemaphore,
            () =>
            {
                var stateStore = _governorStateStoreFactory(SimulationMode);
                var results = new List<SignalRunExecutionResult>();

                for (var currentDate = startDate; currentDate <= endDate; currentDate = currentDate.AddDays(1))
                {
                    var clock = _simulationClockFactory(currentDate);
                    results.Add(
                        ExecuteSingleRun(
                            SignalRunType.Range,
                            normalizedTrigger,
                            currentDate,
                            isSimulation: true,
                            clock,
                            stateStore,
                            SimulationMode));
                }

                return results;
            });
    }

    private SignalRunExecutionResult ExecuteSingleRun(
        SignalRunType runType,
        string trigger,
        DateOnly asOfDate,
        bool isSimulation,
        IClock clock,
        ITradeGovernorStateStore governorStateStore,
        string governorStateMode)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;

        IReadOnlyList<FinalSignal> signals;
        try
        {
            signals = RunPipeline(clock, governorStateStore);
        }
        catch (Exception exception)
        {
            var failedAtUtc = DateTimeOffset.UtcNow;
            return PersistFailedRun(
                runType,
                trigger,
                asOfDate,
                isSimulation,
                startedAtUtc,
                failedAtUtc,
                exception.Message);
        }

        var completedAtUtc = DateTimeOffset.UtcNow;
        var governorState = TryLoadGovernorState(governorStateStore, clock);

        SignalRunPersistenceResult? persistenceResult = null;
        string? errorMessage = null;

        try
        {
            persistenceResult = _persistRun(
                new PersistSignalRunRequest(
                    runType,
                    trigger,
                    asOfDate,
                    isSimulation,
                    startedAtUtc,
                    completedAtUtc,
                    signals,
                    governorState,
                    governorState is null ? null : governorStateMode));
        }
        catch (Exception exception)
        {
            errorMessage = exception.Message;
            WriteDiagnostic($"Persisting {runType} run for {asOfDate:yyyy-MM-dd} failed: {exception.Message}");
        }

        return CreateExecutionResult(
            persistenceResult?.RunId ?? 0L,
            runType,
            trigger,
            persistenceResult?.Status ?? SignalRunStatus.Failed,
            asOfDate,
            isSimulation,
            startedAtUtc,
            completedAtUtc,
            signals,
            errorMessage);
    }

    private IReadOnlyList<FinalSignal> RunPipeline(IClock clock, ITradeGovernorStateStore governorStateStore)
    {
        var marketDataProvider = _marketDataProviderFactory(_config);
        var earningsGate = new EarningsGate(
            _earningsCalendarFactory(_config),
            marketDataProvider,
            _config.News.BlockWindowHours,
            _diagnosticWriter);
        var technicalScorer = new TechnicalScorer(marketDataProvider, _diagnosticWriter);
        var tradeGovernor = new TradeGovernor(
            _config.Policy.MaxBuysPerDay,
            _config.Policy.CooldownMinutes,
            governorStateStore);
        var pipeline = new SignalPipeline(earningsGate, technicalScorer, tradeGovernor);

        return pipeline.Run(_config.Watchlist, clock);
    }

    private SignalRunExecutionResult PersistFailedRun(
        SignalRunType runType,
        string trigger,
        DateOnly asOfDate,
        bool isSimulation,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        string errorMessage)
    {
        long runId = 0L;

        try
        {
            runId = _persistRun(
                new PersistSignalRunRequest(
                    runType,
                    trigger,
                    asOfDate,
                    isSimulation,
                    startedAtUtc,
                    completedAtUtc,
                    [],
                    ErrorMessage: errorMessage)).RunId;
        }
        catch (Exception exception)
        {
            WriteDiagnostic($"Persisting failed {runType} run for {asOfDate:yyyy-MM-dd} failed: {exception.Message}");
        }

        return new SignalRunExecutionResult(
            runId,
            runType,
            trigger,
            SignalRunStatus.Failed,
            asOfDate,
            isSimulation,
            startedAtUtc,
            completedAtUtc,
            0,
            [],
            errorMessage);
    }

    private TradeGovernorState? TryLoadGovernorState(ITradeGovernorStateStore governorStateStore, IClock clock)
    {
        try
        {
            return governorStateStore.Load(clock);
        }
        catch (Exception exception)
        {
            WriteDiagnostic($"Loading trade governor state failed: {exception.Message}");
            return null;
        }
    }

    private static SignalRunExecutionResult CreateExecutionResult(
        long runId,
        SignalRunType runType,
        string trigger,
        SignalRunStatus status,
        DateOnly asOfDate,
        bool isSimulation,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        IReadOnlyList<FinalSignal> signals,
        string? errorMessage)
    {
        return new SignalRunExecutionResult(
            runId,
            runType,
            trigger,
            status,
            asOfDate,
            isSimulation,
            startedAtUtc,
            completedAtUtc,
            signals.Count,
            signals.Select(ToSignalRunSignalResult).ToArray(),
            errorMessage);
    }

    private static SignalRunSignalResult ToSignalRunSignalResult(FinalSignal signal)
    {
        return new SignalRunSignalResult(
            signal.Ticker,
            signal.Action,
            signal.NewsState,
            signal.MarketAction,
            signal.Reason,
            signal.Timestamp);
    }

    private static T ExecuteSerialized<T>(SemaphoreSlim semaphore, Func<T> action)
    {
        semaphore.Wait();

        try
        {
            return action();
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static IEarningsCalendar CreateEarningsCalendar(AppConfig config)
    {
        return new EarningsCalendarFile(config.News.ResolvedLocalEarningsCalendar
            ?? config.News.LocalEarningsCalendar
            ?? "earnings_calendar.json");
    }

    private static IMarketDataProvider CreateMarketDataProvider(AppConfig config)
    {
        _ = config;
        return new YahooFinanceAdapter();
    }

    private static string NormalizeTrigger(string trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger))
        {
            throw new ArgumentException("Trigger is required.", nameof(trigger));
        }

        return trigger.Trim();
    }

    private void WriteDiagnostic(string message)
    {
        _diagnosticWriter?.Invoke($"[SignalRunExecutionService] {message}");
    }
}
