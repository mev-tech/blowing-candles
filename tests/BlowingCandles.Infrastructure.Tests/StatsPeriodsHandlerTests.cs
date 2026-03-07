using BlowingCandles.Cli.Handlers;
using BlowingCandles.Domain.Services;
using BlowingCandles.Infrastructure.Clock;
using BlowingCandles.Infrastructure.Config;

namespace BlowingCandles.Infrastructure.Tests;

[Collection("Console")]
public sealed class StatsPeriodsHandlerTests
{
    [Fact]
    public void Handle_PrintsCompletedPeriodsOpenPositionsAndSummary()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml", """
watchlist: [AAPL]
audit:
  jsonl_path: logs/decisions.jsonl
""");
        workspace.WriteFile("logs/decisions.jsonl", """
{"ticker":"AAPL","action":"Action.BUY","timestamp":"2026-01-15T14:30:00Z"}
{"ticker":"MSFT","action":"Action.BUY","timestamp":"2026-01-20T14:00:00Z"}
{"ticker":"AAPL","action":"Action.WAIT","timestamp":"2026-01-22T14:30:00Z"}
{"ticker":"MSFT","action":"Action.SELL","timestamp":"2026-01-28T16:00:00Z"}
{"ticker":"AAPL","action":"Action.SELL","timestamp":"2026-02-10T15:00:00Z"}
{"ticker":"NVDA","action":"Action.BUY","timestamp":"2026-03-01T14:30:00Z"}
""");

        var handler = CreateHandler(new DateTimeOffset(2026, 3, 7, 14, 30, 0, TimeSpan.Zero));

        var (exitCode, output) = Invoke(handler, workspace.GetPath("config.yaml"));

        Assert.Equal(0, exitCode);
        Assert.Equal(
            """
Audit file: logs/decisions.jsonl
Total records: 6

Completed holding periods:
  AAPL: BUY 2026-01-15T14:30:00Z -> SELL 2026-02-10T15:00:00Z (26.0 days)
  MSFT: BUY 2026-01-20T14:00:00Z -> SELL 2026-01-28T16:00:00Z (8.1 days)

Open positions (BUY without SELL):
  NVDA: BUY 2026-03-01T14:30:00Z (open for 6.0 days)

Summary:
  Completed periods: 2
  Average holding period: 17.1 days
  Open positions: 1
""".ReplaceLineEndings() + Environment.NewLine,
            output.ReplaceLineEndings());
    }

    [Fact]
    public void Handle_ReturnsZeroAndPrintsHeaderWhenAuditFileIsEmpty()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml", """
watchlist: [AAPL]
audit:
  jsonl_path: logs/decisions.jsonl
""");
        workspace.WriteFile("logs/decisions.jsonl", string.Empty);

        var handler = CreateHandler(new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero));

        var (exitCode, output) = Invoke(handler, workspace.GetPath("config.yaml"));

        Assert.Equal(0, exitCode);
        Assert.Equal(
            """
Audit file: logs/decisions.jsonl
Total records: 0

""".ReplaceLineEndings(),
            output.ReplaceLineEndings());
    }

    [Fact]
    public void Handle_ReturnsZeroAndPrintsHeaderWhenAuditFileIsMissing()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml", """
watchlist: [AAPL]
audit:
  jsonl_path: logs/missing.jsonl
""");

        var handler = CreateHandler(new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero));

        var (exitCode, output) = Invoke(handler, workspace.GetPath("config.yaml"));

        Assert.Equal(0, exitCode);
        Assert.Equal(
            """
Audit file: logs/missing.jsonl
Total records: 0

""".ReplaceLineEndings(),
            output.ReplaceLineEndings());
    }

    [Fact]
    public void Handle_ReportsCorrectTotalWhenFileHasOnlyNonBuySellActions()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml", """
watchlist: [AAPL]
audit:
  jsonl_path: logs/decisions.jsonl
""");
        workspace.WriteFile("logs/decisions.jsonl", """
{"ticker":"AAPL","action":"Action.WAIT","timestamp":"2026-01-15T14:30:00Z"}
{"ticker":"MSFT","action":"Action.IGNORE","timestamp":"2026-01-20T14:00:00Z"}
""");

        var handler = CreateHandler(new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero));

        var (exitCode, output) = Invoke(handler, workspace.GetPath("config.yaml"));

        Assert.Equal(0, exitCode);
        Assert.Equal(
            """
Audit file: logs/decisions.jsonl
Total records: 2

""".ReplaceLineEndings(),
            output.ReplaceLineEndings());
    }

    [Fact]
    public void Handle_PairsMultipleBuysWithMultipleSellsFifo()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml", """
watchlist: [AAPL]
audit:
  jsonl_path: logs/decisions.jsonl
""");
        workspace.WriteFile("logs/decisions.jsonl", """
{"ticker":"AAPL","action":"Action.BUY","timestamp":"2026-01-01T14:30:00Z"}
{"ticker":"AAPL","action":"Action.BUY","timestamp":"2026-01-02T14:30:00Z"}
{"ticker":"AAPL","action":"Action.BUY","timestamp":"2026-01-03T14:30:00Z"}
{"ticker":"AAPL","action":"Action.SELL","timestamp":"2026-01-05T14:30:00Z"}
{"ticker":"AAPL","action":"Action.SELL","timestamp":"2026-01-06T14:30:00Z"}
""");

        var handler = CreateHandler(new DateTimeOffset(2026, 1, 10, 14, 30, 0, TimeSpan.Zero));

        var (exitCode, output) = Invoke(handler, workspace.GetPath("config.yaml"));

        Assert.Equal(0, exitCode);
        Assert.Equal(
            """
Audit file: logs/decisions.jsonl
Total records: 5

Completed holding periods:
  AAPL: BUY 2026-01-01T14:30:00Z -> SELL 2026-01-05T14:30:00Z (4.0 days)
  AAPL: BUY 2026-01-02T14:30:00Z -> SELL 2026-01-06T14:30:00Z (4.0 days)

Open positions (BUY without SELL):
  AAPL: BUY 2026-01-03T14:30:00Z (open for 7.0 days)

Summary:
  Completed periods: 2
  Average holding period: 4.0 days
  Open positions: 1
""".ReplaceLineEndings() + Environment.NewLine,
            output.ReplaceLineEndings());
    }

    [Fact]
    public void Handle_ReturnsOneWhenAuditPathMissingFromConfig()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml", """
watchlist: [AAPL]
audit: {}
""");

        var handler = CreateHandler(new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero));

        var (exitCode, output) = Invoke(handler, workspace.GetPath("config.yaml"));

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "ERROR: config.yaml -> audit.jsonl_path missing" + Environment.NewLine,
            output);
    }

    private static StatsPeriodsHandler CreateHandler(DateTimeOffset now)
    {
        return new StatsPeriodsHandler(
            new YamlConfigLoader(),
            new FixedClock(now),
            new HoldingPeriodCalculator());
    }

    private static (int ExitCode, string Output) Invoke(StatsPeriodsHandler handler, string configPath)
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
