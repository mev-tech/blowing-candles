using System.Text.Json;
using BlowingCandles.Application;
using BlowingCandles.Cli.Handlers;
using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Models;
using BlowingCandles.Infrastructure.Clock;
using BlowingCandles.Infrastructure.Config;

namespace BlowingCandles.Infrastructure.Tests;

[Collection("Console")]
public sealed class RunCommandHandlerTests
{
    [Fact]
    public void RunAsOf_WritesSimulationArtifactsAndKeepsLivePathsUntouched()
    {
        using var workspace = new TestWorkspace();
        WriteRunConfig(workspace, watchlist: "[AAPL]", maxBuysPerDay: 5);

        var asOfDate = new DateOnly(2026, 1, 15);
        var provider = new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>>(StringComparer.Ordinal)
        {
            ["AAPL"] = CreateBuyHistory(asOfDate)
        });
        var calendar = new FakeCalendar(new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal)
        {
            ["AAPL"] = new DateTimeOffset(2026, 1, 22, 20, 0, 0, TimeSpan.Zero)
        });
        var support = new RunCommandSupport(_ => calendar, _ => provider);
        var handler = new RunAsOfHandler(new YamlConfigLoader(), new OutputRenderer(), support);

        var (exitCode, output) = Invoke(() => handler.Handle(workspace.GetPath("config.yaml"), asOfDate));

        var textPath = workspace.GetPath("output/asof_2026-01-15.signals.txt");
        var jsonPath = workspace.GetPath("output/asof_2026-01-15.signals.json");
        var simAuditPath = workspace.GetPath("logs/sim_decisions.jsonl");
        var simStatePath = workspace.GetPath("data/sim_state.json");

        Assert.Equal(0, exitCode);
        Assert.Equal(
            $$"""
            Generated 1 signals for 2026-01-15.
            Text output: {{textPath}}
            JSON output: {{jsonPath}}
            """ + Environment.NewLine,
            output);

        Assert.True(File.Exists(textPath));
        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(simAuditPath));
        Assert.True(File.Exists(simStatePath));
        Assert.False(File.Exists(workspace.GetPath("output/live.signals.txt")));
        Assert.False(File.Exists(workspace.GetPath("output/live.signals.json")));
        Assert.False(File.Exists(workspace.GetPath("logs/decisions.jsonl")));
        Assert.False(File.Exists(workspace.GetPath("data/state.json")));

        Assert.Equal(
            "AAPL: Action.BUY | NewsState.TRADE_OK | Action.BUY | ABOVE_SMA50,ABOVE_SMA200,GOLDEN_CROSS,RSI_OVERSOLD",
            File.ReadAllText(textPath).Trim());

        using (var doc = JsonDocument.Parse(File.ReadAllText(jsonPath)))
        {
            var signal = Assert.Single(doc.RootElement.EnumerateArray());
            Assert.Equal("2026-01-15T00:00:00+00:00", signal.GetProperty("timestamp").GetString());
        }

        using (var doc = JsonDocument.Parse(File.ReadAllText(simStatePath)))
        {
            Assert.Equal("2026-01-15", doc.RootElement.GetProperty("day").GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("buys_today").GetInt32());
            Assert.Equal("2026-01-15T00:00:00+00:00", doc.RootElement.GetProperty("last_buy_at").GetString());
        }

        var auditLines = File.ReadAllLines(simAuditPath);
        Assert.Single(auditLines);
        using (var doc = JsonDocument.Parse(auditLines[0]))
        {
            Assert.Equal("AAPL", doc.RootElement.GetProperty("ticker").GetString());
            Assert.Equal("2026-01-15T00:00:00+00:00", doc.RootElement.GetProperty("timestamp").GetString());
        }

        Assert.Equal([new PriceRequest("AAPL", asOfDate)], provider.PriceRequests);
    }

    [Fact]
    public void RunRealtime_WritesLiveArtifactsToConfiguredPaths()
    {
        using var workspace = new TestWorkspace();
        WriteRunConfig(workspace, watchlist: "[AAPL]", maxBuysPerDay: 5);

        var now = new DateTimeOffset(2026, 3, 7, 14, 30, 0, TimeSpan.Zero);
        var provider = new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>>(StringComparer.Ordinal)
        {
            ["AAPL"] = CreateBuyHistory(DateOnly.FromDateTime(now.UtcDateTime))
        });
        var calendar = new FakeCalendar(new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal)
        {
            ["AAPL"] = now.AddDays(7)
        });
        var support = new RunCommandSupport(_ => calendar, _ => provider);
        var handler = new RunRealtimeHandler(
            new YamlConfigLoader(),
            new OutputRenderer(),
            support,
            () => new FixedClock(now));

        var (exitCode, output) = Invoke(() => handler.Handle(workspace.GetPath("config.yaml")));

        var textPath = workspace.GetPath("output/live.signals.txt");
        var jsonPath = workspace.GetPath("output/live.signals.json");
        var auditPath = workspace.GetPath("logs/decisions.jsonl");
        var statePath = workspace.GetPath("data/state.json");

        Assert.Equal(0, exitCode);
        Assert.Equal(
            $$"""
            Generated 1 signals.
            Text output: {{textPath}}
            JSON output: {{jsonPath}}
            """ + Environment.NewLine,
            output);

        Assert.True(File.Exists(textPath));
        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(auditPath));
        Assert.True(File.Exists(statePath));
        Assert.False(File.Exists(workspace.GetPath("logs/sim_decisions.jsonl")));
        Assert.False(File.Exists(workspace.GetPath("data/sim_state.json")));

        using var doc = JsonDocument.Parse(File.ReadAllText(statePath));
        Assert.Equal("2026-03-07", doc.RootElement.GetProperty("day").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("buys_today").GetInt32());
        Assert.Equal("2026-03-07T14:30:00+00:00", doc.RootElement.GetProperty("last_buy_at").GetString());
    }

    [Fact]
    public void RunRealtime_DefaultClockFactory_UsesSystemClock()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml", "watchlist: [AAPL]" + Environment.NewLine);

        var usedSystemClock = false;
        var support = new RunCommandSupport(
            signalRunner: (_, _, clock) =>
            {
                usedSystemClock = clock is SystemClock;
                return Array.Empty<FinalSignal>();
            });
        var handler = new RunRealtimeHandler(new YamlConfigLoader(), new OutputRenderer(), support);

        var (exitCode, _) = Invoke(() => handler.Handle(workspace.GetPath("config.yaml")));

        Assert.Equal(0, exitCode);
        Assert.True(usedSystemClock);
    }

    [Fact]
    public void RunRange_WritesPerDayOutputsAndSharedSimulationAudit()
    {
        using var workspace = new TestWorkspace();
        WriteRunConfig(workspace, watchlist: "[MSFT, AAPL]", maxBuysPerDay: 1);

        var startDate = new DateOnly(2026, 1, 15);
        var endDate = new DateOnly(2026, 1, 16);
        var provider = new FakeMarketDataProvider(new Dictionary<string, IReadOnlyList<PriceBar>>(StringComparer.Ordinal)
        {
            ["AAPL"] = CreateBuyHistory(startDate),
            ["MSFT"] = CreateBuyHistory(startDate)
        });
        var calendar = new FakeCalendar(new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal)
        {
            ["AAPL"] = new DateTimeOffset(2026, 1, 25, 20, 0, 0, TimeSpan.Zero),
            ["MSFT"] = new DateTimeOffset(2026, 1, 25, 20, 0, 0, TimeSpan.Zero)
        });
        var support = new RunCommandSupport(_ => calendar, _ => provider);
        var handler = new RunRangeHandler(new YamlConfigLoader(), new OutputRenderer(), support);

        var (exitCode, output) = Invoke(() => handler.Handle(workspace.GetPath("config.yaml"), startDate, endDate));

        var dayOneText = workspace.GetPath("output/asof_2026-01-15.signals.txt");
        var dayOneJson = workspace.GetPath("output/asof_2026-01-15.signals.json");
        var dayTwoText = workspace.GetPath("output/asof_2026-01-16.signals.txt");
        var dayTwoJson = workspace.GetPath("output/asof_2026-01-16.signals.json");
        var simAuditPath = workspace.GetPath("logs/sim_decisions.jsonl");
        var simStatePath = workspace.GetPath("data/sim_state.json");

        Assert.Equal(0, exitCode);
        Assert.Equal(
            $$"""
            Generated signals for 2 day(s).
            Simulation audit: {{simAuditPath}}
            """ + Environment.NewLine,
            output);

        Assert.True(File.Exists(dayOneText));
        Assert.True(File.Exists(dayOneJson));
        Assert.True(File.Exists(dayTwoText));
        Assert.True(File.Exists(dayTwoJson));
        Assert.True(File.Exists(simAuditPath));
        Assert.True(File.Exists(simStatePath));
        Assert.False(File.Exists(workspace.GetPath("logs/decisions.jsonl")));
        Assert.False(File.Exists(workspace.GetPath("data/state.json")));

        Assert.Equal(
            """
            AAPL: Action.BUY | NewsState.TRADE_OK | Action.BUY | ABOVE_SMA50,ABOVE_SMA200,GOLDEN_CROSS,RSI_OVERSOLD
            MSFT: Action.WAIT | NewsState.TRADE_OK | Action.BUY | MAX_BUYS_REACHED
            """.Trim(),
            File.ReadAllText(dayOneText).Trim());
        Assert.Equal(File.ReadAllText(dayOneText), File.ReadAllText(dayTwoText));

        var auditLines = File.ReadAllLines(simAuditPath);
        Assert.Equal(4, auditLines.Length);

        using (var first = JsonDocument.Parse(auditLines[0]))
        using (var second = JsonDocument.Parse(auditLines[1]))
        using (var third = JsonDocument.Parse(auditLines[2]))
        using (var fourth = JsonDocument.Parse(auditLines[3]))
        {
            Assert.Equal("AAPL", first.RootElement.GetProperty("ticker").GetString());
            Assert.Equal("2026-01-15T00:00:00+00:00", first.RootElement.GetProperty("timestamp").GetString());
            Assert.Equal("MSFT", second.RootElement.GetProperty("ticker").GetString());
            Assert.Equal("2026-01-15T00:00:00+00:00", second.RootElement.GetProperty("timestamp").GetString());
            Assert.Equal("AAPL", third.RootElement.GetProperty("ticker").GetString());
            Assert.Equal("2026-01-16T00:00:00+00:00", third.RootElement.GetProperty("timestamp").GetString());
            Assert.Equal("MSFT", fourth.RootElement.GetProperty("ticker").GetString());
            Assert.Equal("2026-01-16T00:00:00+00:00", fourth.RootElement.GetProperty("timestamp").GetString());
        }

        using var stateDoc = JsonDocument.Parse(File.ReadAllText(simStatePath));
        Assert.Equal("2026-01-16", stateDoc.RootElement.GetProperty("day").GetString());
        Assert.Equal(1, stateDoc.RootElement.GetProperty("buys_today").GetInt32());
        Assert.Equal("2026-01-16T00:00:00+00:00", stateDoc.RootElement.GetProperty("last_buy_at").GetString());

        Assert.Equal(
            [
                new PriceRequest("MSFT", startDate),
                new PriceRequest("AAPL", startDate),
                new PriceRequest("MSFT", endDate),
                new PriceRequest("AAPL", endDate)
            ],
            provider.PriceRequests);
    }

    [Fact]
    public void RunRange_ThrowsWhenEndDatePrecedesStartDate()
    {
        var handler = new RunRangeHandler(new YamlConfigLoader(), new OutputRenderer());

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            handler.Handle("config.yaml", new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 1)));

        Assert.Equal("endDate", exception.ParamName);
    }

    private static void WriteRunConfig(TestWorkspace workspace, string watchlist, int maxBuysPerDay)
    {
        workspace.WriteFile("config.yaml", $$"""
        watchlist: {{watchlist}}
        news:
          local_earnings_calendar: earnings_calendar.json
          block_window_hours: 48
        policy:
          max_buys_per_day: {{maxBuysPerDay}}
          cooldown_minutes: 0
        output:
          text_file: output/live.signals.txt
          json_file: output/live.signals.json
        state:
          path: data/state.json
        audit:
          jsonl_path: logs/decisions.jsonl
        """);
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

    private static (int ExitCode, string Output) Invoke(Func<int> action)
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);
            var exitCode = action();
            writer.Flush();
            return (exitCode, writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
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

    private sealed record PriceRequest(string Ticker, DateOnly AsOfDate);
}
