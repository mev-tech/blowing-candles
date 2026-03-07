using BlowingCandles.Application;
using BlowingCandles.Domain.Services;
using BlowingCandles.Infrastructure.Audit;
using BlowingCandles.Infrastructure.Calendar;
using BlowingCandles.Infrastructure.Clock;
using BlowingCandles.Infrastructure.Config;
using BlowingCandles.Infrastructure.MarketData;

namespace BlowingCandles.Cli.Handlers;

public sealed class RunRealtimeHandler
{
    private readonly YamlConfigLoader _configLoader;
    private readonly OutputRenderer _outputRenderer;

    public RunRealtimeHandler(YamlConfigLoader configLoader, OutputRenderer outputRenderer)
    {
        _configLoader = configLoader;
        _outputRenderer = outputRenderer;
    }

    public int Handle(string configPath)
    {
        var config = _configLoader.Load(configPath);
        var clock = new SystemClock();
        var pipeline = CreatePipeline(config);
        var signals = pipeline.Run(config.Watchlist, clock);
        var auditPath = ResolveAuditPath(config, configPath);

        _outputRenderer.WriteSignals(config.Output.TextFile, config.Output.JsonFile, signals);
        new JsonlAuditWriter(auditPath).Append(signals);

        Console.WriteLine($"Generated {signals.Count} scaffold signals.");
        Console.WriteLine($"Text output: {config.Output.TextFile}");
        Console.WriteLine($"JSON output: {config.Output.JsonFile}");
        return 0;
    }

    private static SignalPipeline CreatePipeline(AppConfig config)
    {
        var marketDataProvider = new YahooFinanceAdapter();
        var earningsGate = new EarningsGate(
            new EarningsCalendarFile(config.News.ResolvedLocalEarningsCalendar ?? config.News.LocalEarningsCalendar ?? "earnings_calendar.json"),
            marketDataProvider);
        var technicalScorer = new TechnicalScorer(marketDataProvider);
        var tradeGovernor = new TradeGovernor();
        return new SignalPipeline(earningsGate, technicalScorer, tradeGovernor);
    }

    private static string ResolveAuditPath(AppConfig config, string configPath)
    {
        if (!string.IsNullOrWhiteSpace(config.Audit.ResolvedJsonlPath))
        {
            return config.Audit.ResolvedJsonlPath;
        }

        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(baseDirectory, "logs/decisions.jsonl"));
    }
}
