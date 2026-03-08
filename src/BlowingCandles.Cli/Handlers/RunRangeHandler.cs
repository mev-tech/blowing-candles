using BlowingCandles.Application;
using BlowingCandles.Domain.Services;
using BlowingCandles.Infrastructure.Audit;
using BlowingCandles.Infrastructure.Calendar;
using BlowingCandles.Infrastructure.Clock;
using BlowingCandles.Infrastructure.Config;
using BlowingCandles.Infrastructure.MarketData;
using BlowingCandles.Infrastructure.State;

namespace BlowingCandles.Cli.Handlers;

public sealed class RunRangeHandler
{
    private readonly YamlConfigLoader _configLoader;
    private readonly OutputRenderer _outputRenderer;

    public RunRangeHandler(YamlConfigLoader configLoader, OutputRenderer outputRenderer)
    {
        _configLoader = configLoader;
        _outputRenderer = outputRenderer;
    }

    public int Handle(string configPath, DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
        {
            throw new ArgumentOutOfRangeException(nameof(endDate), "End date must be on or after the start date.");
        }

        var config = _configLoader.Load(configPath);
        var pipeline = CreatePipeline(config);
        var simulationAuditPath = BuildSimulationAuditPath(ResolveAuditPath(config, configPath));
        var daysProcessed = 0;

        for (var currentDate = startDate; currentDate <= endDate; currentDate = currentDate.AddDays(1))
        {
            var clock = new FixedClock(new DateTimeOffset(currentDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
            var signals = pipeline.Run(config.Watchlist, clock);
            var textPath = BuildSimulationOutputPath(config.Output.TextFile, currentDate, ".txt");
            var jsonPath = BuildSimulationOutputPath(config.Output.JsonFile, currentDate, ".json");

            _outputRenderer.WriteSignals(textPath, jsonPath, signals);
            new JsonlAuditWriter(simulationAuditPath).Append(signals);
            daysProcessed++;
        }

        Console.WriteLine($"Generated scaffold signals for {daysProcessed} day(s).");
        Console.WriteLine($"Simulation audit: {simulationAuditPath}");
        return 0;
    }

    private static SignalPipeline CreatePipeline(AppConfig config)
    {
        var marketDataProvider = new YahooFinanceAdapter();
        var earningsGate = new EarningsGate(
            new EarningsCalendarFile(config.News.ResolvedLocalEarningsCalendar ?? config.News.LocalEarningsCalendar ?? "earnings_calendar.json"),
            marketDataProvider,
            config.News.BlockWindowHours);
        var technicalScorer = new TechnicalScorer(marketDataProvider);
        var tradeGovernor = new TradeGovernor(
            config.Policy.MaxBuysPerDay,
            config.Policy.CooldownMinutes,
            new JsonStateStore(BuildSimulationStatePath(config.State.Path)));
        return new SignalPipeline(earningsGate, technicalScorer, tradeGovernor);
    }

    private static string BuildSimulationAuditPath(string liveAuditPath)
    {
        var directory = Path.GetDirectoryName(liveAuditPath);
        var fileName = Path.GetFileName(liveAuditPath);
        var simulationFileName = fileName.StartsWith("sim_", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"sim_{fileName}";

        return string.IsNullOrWhiteSpace(directory)
            ? simulationFileName
            : Path.Combine(directory, simulationFileName);
    }

    private static string BuildSimulationStatePath(string liveStatePath)
    {
        var directory = Path.GetDirectoryName(liveStatePath);
        var fileName = Path.GetFileName(liveStatePath);
        var simulationFileName = fileName.StartsWith("sim_", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"sim_{fileName}";

        return string.IsNullOrWhiteSpace(directory)
            ? simulationFileName
            : Path.Combine(directory, simulationFileName);
    }

    private static string BuildSimulationOutputPath(string liveOutputPath, DateOnly asOfDate, string extension)
    {
        var directory = Path.GetDirectoryName(liveOutputPath);
        var simulationFileName = $"asof_{asOfDate:yyyy-MM-dd}.signals{extension}";

        return string.IsNullOrWhiteSpace(directory)
            ? simulationFileName
            : Path.Combine(directory, simulationFileName);
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
