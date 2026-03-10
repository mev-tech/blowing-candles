using BlowingCandles.Infrastructure.Config;

namespace BlowingCandles.Infrastructure.Tests;

public sealed class YamlConfigLoaderTests
{
    [Fact]
    public void Load_FullConfig_LoadsConsumedSectionsAndResolvesRelativePaths()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("configs/runtime/config.yaml", """
watchlist: [" msft ", "aapl", " msft "]
news:
  local_earnings_calendar: cal/earnings_calendar.json
  block_window_hours: 72
policy:
  max_buys_per_day: 3
  cooldown_minutes: 15
audit:
  jsonl_path: logs/decisions.jsonl
""");

        var loader = new YamlConfigLoader();

        var config = loader.Load(workspace.GetPath("configs/runtime/config.yaml"));

        Assert.Equal(["MSFT", "AAPL", "MSFT"], config.Watchlist);
        Assert.Equal("cal/earnings_calendar.json", config.News.LocalEarningsCalendar);
        Assert.Equal(workspace.GetPath("configs/runtime/cal/earnings_calendar.json"), config.News.ResolvedLocalEarningsCalendar);
        Assert.Equal(72, config.News.BlockWindowHours);
        Assert.Equal(3, config.Policy.MaxBuysPerDay);
        Assert.Equal(15, config.Policy.CooldownMinutes);
        Assert.Equal("logs/decisions.jsonl", config.Audit.JsonlPath);
        Assert.Equal(workspace.GetPath("configs/runtime/logs/decisions.jsonl"), config.Audit.ResolvedJsonlPath);
    }

    [Fact]
    public void Load_MissingOptionalSections_UsesDefaults()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml", """
watchlist: [ aapl ]
""");

        var loader = new YamlConfigLoader();

        var config = loader.Load(workspace.GetPath("config.yaml"));

        Assert.Equal(["AAPL"], config.Watchlist);
        Assert.Null(config.News.LocalEarningsCalendar);
        Assert.Null(config.News.ResolvedLocalEarningsCalendar);
        Assert.Equal(48, config.News.BlockWindowHours);
        Assert.Equal(int.MaxValue, config.Policy.MaxBuysPerDay);
        Assert.Equal(0, config.Policy.CooldownMinutes);
        Assert.Null(config.Audit.JsonlPath);
        Assert.Null(config.Audit.ResolvedJsonlPath);
    }

    [Fact]
    public void Load_BlankOptionalPathValues_FallBackToNull()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml", """
watchlist: [AAPL]
news:
  local_earnings_calendar: "   "
audit:
  jsonl_path: ""
""");

        var loader = new YamlConfigLoader();

        var config = loader.Load(workspace.GetPath("config.yaml"));

        Assert.Null(config.News.LocalEarningsCalendar);
        Assert.Null(config.News.ResolvedLocalEarningsCalendar);
        Assert.Null(config.Audit.JsonlPath);
        Assert.Null(config.Audit.ResolvedJsonlPath);
    }

    [Fact]
    public void Load_WhenWatchlistMissing_ThrowsInvalidDataException()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml", """
news:
  local_earnings_calendar: earnings_calendar.json
""");

        var loader = new YamlConfigLoader();

        var exception = Assert.Throws<InvalidDataException>(() => loader.Load(workspace.GetPath("config.yaml")));

        Assert.Equal("Config file is missing required 'watchlist'.", exception.Message);
    }
}
