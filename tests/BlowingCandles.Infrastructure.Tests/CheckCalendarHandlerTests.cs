using BlowingCandles.Cli.Handlers;
using BlowingCandles.Infrastructure.Clock;
using BlowingCandles.Infrastructure.Config;

namespace BlowingCandles.Infrastructure.Tests;

public sealed class CheckCalendarHandlerTests
{
    [Fact]
    public void Handle_PrintsMixedResultsInWatchlistOrderAndReturnsTwo()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml", """
watchlist: [msft, TSLA, goog, aapl]
news:
  local_earnings_calendar: earnings_calendar.json
""");
        workspace.WriteFile("earnings_calendar.json", """
{
  "aapl": ["2026-05-07T20:00:00Z", "bad-date", "2026-05-07T20:00:00Z"],
  "MSFT": "2026-04-28T20:00:00Z",
  "tsla": ["2026-02-01T20:00:00Z"]
}
""");

        var handler = new CheckCalendarHandler(
            new YamlConfigLoader(),
            new FixedClock(new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero)));

        var (exitCode, output) = Invoke(handler, workspace.GetPath("config.yaml"));

        Assert.Equal(2, exitCode);
        Assert.Equal(
            """
Now (UTC): 2026-03-07T12:00:00Z
Calendar file: earnings_calendar.json

OK (next earnings found):
  MSFT: 2026-04-28T20:00:00Z
  AAPL: 2026-05-07T20:00:00Z

EXPIRED (no future dates in calendar):
  TSLA: CALENDAR_EXPIRED

MISSING (not present in calendar file):
  GOOG


""".ReplaceLineEndings(),
            output.ReplaceLineEndings());
    }

    [Fact]
    public void Handle_ReturnsOneWhenCalendarPathMissingFromConfig()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml", """
watchlist: [AAPL]
news: {}
""");

        var handler = new CheckCalendarHandler(
            new YamlConfigLoader(),
            new FixedClock(new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero)));

        var (exitCode, output) = Invoke(handler, workspace.GetPath("config.yaml"));

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "ERROR: config.yaml -> news.local_earnings_calendar missing" + Environment.NewLine,
            output);
    }

    [Fact]
    public void Handle_ReturnsOneWhenCalendarJsonIsInvalid()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml", """
watchlist: [AAPL]
news:
  local_earnings_calendar: earnings_calendar.json
""");
        workspace.WriteFile("earnings_calendar.json", "{ invalid json");

        var handler = new CheckCalendarHandler(
            new YamlConfigLoader(),
            new FixedClock(new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero)));

        var (exitCode, output) = Invoke(handler, workspace.GetPath("config.yaml"));

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "ERROR: invalid earnings calendar file: earnings_calendar.json" + Environment.NewLine,
            output);
    }

    private static (int ExitCode, string Output) Invoke(CheckCalendarHandler handler, string configPath)
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);
            var exitCode = handler.Handle(configPath);
            writer.Flush();
            return (exitCode, writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
