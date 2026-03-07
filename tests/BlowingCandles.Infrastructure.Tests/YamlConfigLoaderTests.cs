using BlowingCandles.Infrastructure.Config;

namespace BlowingCandles.Infrastructure.Tests;

public sealed class YamlConfigLoaderTests
{
    [Fact]
    public void Load_PreservesWatchlistOrderAndResolvesCalendarPathSeparately()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml", """
watchlist: [msft, aapl, msft]
news:
  local_earnings_calendar: earnings_calendar.json
""");

        var loader = new YamlConfigLoader();

        var config = loader.Load(workspace.GetPath("config.yaml"));

        Assert.Equal(["MSFT", "AAPL", "MSFT"], config.Watchlist);
        Assert.Equal("earnings_calendar.json", config.News.LocalEarningsCalendar);
        Assert.Equal(workspace.GetPath("earnings_calendar.json"), config.News.ResolvedLocalEarningsCalendar);
    }
}
