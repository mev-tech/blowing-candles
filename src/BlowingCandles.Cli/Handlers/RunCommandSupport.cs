using BlowingCandles.Application;
using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Models;
using BlowingCandles.Domain.Services;
using BlowingCandles.Infrastructure.Calendar;
using BlowingCandles.Infrastructure.Config;
using BlowingCandles.Infrastructure.MarketData;
using BlowingCandles.Infrastructure.State;

namespace BlowingCandles.Cli.Handlers;

public sealed class RunCommandSupport
{
    private readonly Func<AppConfig, IEarningsCalendar> _earningsCalendarFactory;
    private readonly Func<AppConfig, IMarketDataProvider> _marketDataProviderFactory;
    private readonly Func<AppConfig, string, IClock, IReadOnlyList<FinalSignal>> _signalRunner;

    public RunCommandSupport(
        Func<AppConfig, IEarningsCalendar>? earningsCalendarFactory = null,
        Func<AppConfig, IMarketDataProvider>? marketDataProviderFactory = null,
        Func<AppConfig, string, IClock, IReadOnlyList<FinalSignal>>? signalRunner = null)
    {
        _earningsCalendarFactory = earningsCalendarFactory ?? CreateEarningsCalendar;
        _marketDataProviderFactory = marketDataProviderFactory ?? CreateMarketDataProvider;
        _signalRunner = signalRunner ?? RunSignalsCore;
    }

    public IReadOnlyList<FinalSignal> RunSignals(AppConfig config, string statePath, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(statePath);
        ArgumentNullException.ThrowIfNull(clock);

        return _signalRunner(config, statePath, clock);
    }

    public static string ResolveAuditPath(AppConfig config, string configPath)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(configPath);

        if (!string.IsNullOrWhiteSpace(config.Audit.ResolvedJsonlPath))
        {
            return config.Audit.ResolvedJsonlPath;
        }

        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(baseDirectory, "logs/decisions.jsonl"));
    }

    public static string BuildSimulationAuditPath(string liveAuditPath)
    {
        return BuildSimulationSiblingPath(liveAuditPath, "sim_");
    }

    public static string BuildSimulationStatePath(string liveStatePath)
    {
        return BuildSimulationSiblingPath(liveStatePath, "sim_");
    }

    public static string BuildSimulationOutputPath(string liveOutputPath, DateOnly asOfDate, string extension)
    {
        var directory = Path.GetDirectoryName(liveOutputPath);
        var simulationFileName = $"asof_{asOfDate:yyyy-MM-dd}.signals{extension}";

        return string.IsNullOrWhiteSpace(directory)
            ? simulationFileName
            : Path.Combine(directory, simulationFileName);
    }

    private IReadOnlyList<FinalSignal> RunSignalsCore(AppConfig config, string statePath, IClock clock)
    {
        var marketDataProvider = _marketDataProviderFactory(config);
        var earningsGate = new EarningsGate(
            _earningsCalendarFactory(config),
            marketDataProvider,
            config.News.BlockWindowHours);
        var technicalScorer = new TechnicalScorer(marketDataProvider);
        var tradeGovernor = new TradeGovernor(
            config.Policy.MaxBuysPerDay,
            config.Policy.CooldownMinutes,
            new JsonStateStore(statePath));
        var pipeline = new SignalPipeline(earningsGate, technicalScorer, tradeGovernor);

        return pipeline.Run(config.Watchlist, clock);
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

    private static string BuildSimulationSiblingPath(string livePath, string prefix)
    {
        var directory = Path.GetDirectoryName(livePath);
        var fileName = Path.GetFileName(livePath);
        var simulationFileName = fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{prefix}{fileName}";

        return string.IsNullOrWhiteSpace(directory)
            ? simulationFileName
            : Path.Combine(directory, simulationFileName);
    }
}
