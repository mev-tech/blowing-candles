using System.Text.Json;
using BlowingCandles.Application;
using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Models;
using BlowingCandles.Domain.Services;
using BlowingCandles.Infrastructure.Calendar;
using BlowingCandles.Infrastructure.Clock;
using BlowingCandles.Infrastructure.Config;

namespace BlowingCandles.CrossValidation.Tests;

public sealed class GoldenOutputTests
{
    private static readonly DateOnly MixedActionsAsOfDate = new(2026, 1, 15);
    private static readonly DateOnly MixedActionsRangeStart = new(2026, 1, 13);
    private static readonly DateOnly MixedActionsRangeEnd = new(2026, 1, 15);
    private static readonly DateOnly AllWaitAsOfDate = new(2026, 1, 15);
    private static readonly DateOnly EmptyWatchlistAsOfDate = new(2026, 1, 15);

    [Fact]
    public void RunAsOf_SignalsTxt_MatchesPythonReference()
    {
        using var workspace = new ScenarioWorkspace("mixed-actions");

        var result = RunAsOf(workspace, MixedActionsAsOfDate);

        ComparisonHelpers.AssertTextContentMatches(
            workspace.GetGoldenPath("golden/run-asof/signals.txt"),
            result.TextContent);
    }

    [Fact]
    public void RunAsOf_SignalsJson_MatchesPythonReference()
    {
        using var workspace = new ScenarioWorkspace("mixed-actions");

        var result = RunAsOf(workspace, MixedActionsAsOfDate);

        ComparisonHelpers.AssertJsonContentMatches(
            workspace.GetGoldenPath("golden/run-asof/signals.json"),
            result.JsonContent);
    }

    [Fact]
    public void RunAsOf_AuditJsonl_MatchesPythonReference()
    {
        using var workspace = new ScenarioWorkspace("mixed-actions");

        var result = RunAsOf(workspace, MixedActionsAsOfDate);

        ComparisonHelpers.AssertJsonlContentMatches(
            workspace.GetGoldenPath("golden/run-asof/decisions.jsonl"),
            result.AuditJsonlContent);
    }

    [Fact]
    public void RunRange_MultiDay_MatchesPythonReference()
    {
        using var workspace = new ScenarioWorkspace("mixed-actions");

        var result = RunRange(workspace, MixedActionsRangeStart, MixedActionsRangeEnd);

        foreach (var day in EachDate(MixedActionsRangeStart, MixedActionsRangeEnd))
        {
            ComparisonHelpers.AssertTextContentMatches(
                workspace.GetGoldenPath($"golden/run-range/asof_{day:yyyy-MM-dd}.signals.txt"),
                result.GetTextContent(day));
            ComparisonHelpers.AssertJsonContentMatches(
                workspace.GetGoldenPath($"golden/run-range/asof_{day:yyyy-MM-dd}.signals.json"),
                result.GetJsonContent(day));
        }

        ComparisonHelpers.AssertJsonlContentMatches(
            workspace.GetGoldenPath("golden/run-range/decisions.jsonl"),
            result.AuditJsonlContent);
    }

    [Fact]
    public void AllWaitScenario_MatchesPythonReference()
    {
        using var workspace = new ScenarioWorkspace("all-wait");

        var result = RunAsOf(workspace, AllWaitAsOfDate);

        ComparisonHelpers.AssertTextContentMatches(
            workspace.GetGoldenPath("golden/run-asof/signals.txt"),
            result.TextContent);
        ComparisonHelpers.AssertJsonContentMatches(
            workspace.GetGoldenPath("golden/run-asof/signals.json"),
            result.JsonContent);
        ComparisonHelpers.AssertJsonlContentMatches(
            workspace.GetGoldenPath("golden/run-asof/decisions.jsonl"),
            result.AuditJsonlContent);
    }

    [Fact]
    public void EmptyWatchlist_MatchesPythonReference()
    {
        using var workspace = new ScenarioWorkspace("empty-watchlist");

        var result = RunAsOf(workspace, EmptyWatchlistAsOfDate);

        ComparisonHelpers.AssertTextContentMatches(
            workspace.GetGoldenPath("golden/run-asof/signals.txt"),
            result.TextContent);
        ComparisonHelpers.AssertJsonContentMatches(
            workspace.GetGoldenPath("golden/run-asof/signals.json"),
            result.JsonContent);
        ComparisonHelpers.AssertJsonlContentMatches(
            workspace.GetGoldenPath("golden/run-asof/decisions.jsonl"),
            result.AuditJsonlContent);
    }

    private static RunArtifacts RunAsOf(ScenarioWorkspace workspace, DateOnly asOfDate)
    {
        var config = workspace.LoadConfig();
        var provider = new FixtureMarketDataProvider(workspace.GetFixturePath("market-data"));
        var pipeline = CreatePipeline(config, provider, new InMemoryTradeGovernorStateStore());
        var clock = new FixedClock(new DateTimeOffset(asOfDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        var signals = pipeline.Run(config.Watchlist, clock);

        return new RunArtifacts(
            RenderText(signals),
            SignalsJsonSerializer.Serialize(signals),
            RenderAuditJsonl(signals));
    }

    private static RangeRunArtifacts RunRange(ScenarioWorkspace workspace, DateOnly startDate, DateOnly endDate)
    {
        var config = workspace.LoadConfig();
        var provider = new FixtureMarketDataProvider(workspace.GetFixturePath("market-data"));
        var pipeline = CreatePipeline(config, provider, new InMemoryTradeGovernorStateStore());
        var outputsByDay = new Dictionary<DateOnly, DayArtifacts>();
        var auditLines = new List<string>();

        foreach (var day in EachDate(startDate, endDate))
        {
            var clock = new FixedClock(new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
            var signals = pipeline.Run(config.Watchlist, clock);
            outputsByDay[day] = new DayArtifacts(
                RenderText(signals),
                SignalsJsonSerializer.Serialize(signals));
            auditLines.AddRange(RenderAuditJsonlLines(signals));
        }

        return new RangeRunArtifacts(
            outputsByDay,
            string.Join(Environment.NewLine, auditLines));
    }

    private static SignalPipeline CreatePipeline(
        AppConfig config,
        FixtureMarketDataProvider provider,
        ITradeGovernorStateStore stateStore)
    {
        var calendarPath = config.News.ResolvedLocalEarningsCalendar
            ?? throw new InvalidOperationException("Cross-validation fixtures require a local earnings calendar.");

        return new SignalPipeline(
            new EarningsGate(new EarningsCalendarFile(calendarPath), provider, config.News.BlockWindowHours),
            new TechnicalScorer(provider),
            new TradeGovernor(
                config.Policy.MaxBuysPerDay,
                config.Policy.CooldownMinutes,
                stateStore));
    }

    private static IEnumerable<DateOnly> EachDate(DateOnly startDate, DateOnly endDate)
    {
        for (var currentDate = startDate; currentDate <= endDate; currentDate = currentDate.AddDays(1))
        {
            yield return currentDate;
        }
    }

    private static string RenderText(IEnumerable<FinalSignal> signals)
    {
        return string.Join(
            Environment.NewLine,
            signals.Select(signal =>
                $"{signal.Ticker}: Action.{signal.Action} | NewsState.{signal.NewsState} | Action.{signal.MarketAction} | {signal.Reason}"));
    }

    private static string RenderAuditJsonl(IEnumerable<FinalSignal> signals)
    {
        return string.Join(Environment.NewLine, RenderAuditJsonlLines(signals));
    }

    private static IReadOnlyList<string> RenderAuditJsonlLines(IEnumerable<FinalSignal> signals)
    {
        return signals.Select(signal =>
            JsonSerializer.Serialize(
                new
                {
                    ticker = signal.Ticker,
                    action = $"Action.{signal.Action}",
                    news_state = $"NewsState.{signal.NewsState}",
                    market_action = $"Action.{signal.MarketAction}",
                    reason = signal.Reason,
                    timestamp = signal.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:sszzz")
                }))
            .ToArray();
    }

    private sealed class InMemoryTradeGovernorStateStore : ITradeGovernorStateStore
    {
        private TradeGovernorState? _state;

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
        }
    }

    private sealed class ScenarioWorkspace : IDisposable
    {
        public ScenarioWorkspace(string scenarioName)
        {
            FixtureRoot = Path.Combine(AppContext.BaseDirectory, "fixtures", "cross-validation", scenarioName);
            RootPath = Path.Combine(Path.GetTempPath(), $"blowing-candles-cross-validation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);

            CopyFixtureFile("config.yaml");
            CopyFixtureFile("earnings_calendar.json");
        }

        public string FixtureRoot { get; }

        public string RootPath { get; }

        public string GetFixturePath(string relativePath)
        {
            return Path.Combine(FixtureRoot, relativePath);
        }

        public string GetGoldenPath(string relativePath)
        {
            return GetFixturePath(relativePath);
        }

        public string GetOutputPath(string relativePath)
        {
            return Path.Combine(RootPath, relativePath);
        }

        public AppConfig LoadConfig()
        {
            return new YamlConfigLoader().Load(GetOutputPath("config.yaml"));
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private void CopyFixtureFile(string fileName)
        {
            var sourcePath = GetFixturePath(fileName);
            if (!File.Exists(sourcePath))
            {
                return;
            }

            File.Copy(sourcePath, GetOutputPath(fileName), overwrite: true);
        }
    }

    private sealed record RunArtifacts(string TextContent, string JsonContent, string AuditJsonlContent);

    private sealed record DayArtifacts(string TextContent, string JsonContent);

    private sealed record RangeRunArtifacts(
        IReadOnlyDictionary<DateOnly, DayArtifacts> OutputsByDay,
        string AuditJsonlContent)
    {
        public string GetTextContent(DateOnly date)
        {
            return OutputsByDay[date].TextContent;
        }

        public string GetJsonContent(DateOnly date)
        {
            return OutputsByDay[date].JsonContent;
        }
    }
}
