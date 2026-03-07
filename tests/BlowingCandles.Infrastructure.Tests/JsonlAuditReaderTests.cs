using BlowingCandles.Infrastructure.Audit;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Infrastructure.Tests;

public sealed class JsonlAuditReaderTests
{
    [Fact]
    public void ReadAll_ParsesBuyAndSellRowsAndSkipsMalformedOrIrrelevantLines()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("logs/decisions.jsonl", """
{"ticker":"aapl","action":"Action.BUY","timestamp":"2026-01-15T14:30:00Z"}
{"ticker":"AAPL","action":"Action.WAIT","timestamp":"2026-01-16T14:30:00Z"}
not json
{"ticker":"MSFT","action":"SELL","timestamp":"bad-timestamp"}
{"ticker":"msft","action":"sell","timestamp":"2026-01-28T16:00:00+00:00"}
""");

        var reader = new JsonlAuditReader(workspace.GetPath("logs/decisions.jsonl"));

        var result = reader.ReadAll();

        Assert.Equal(3, result.TotalRecords);
        Assert.Collection(
            result.BuySellRecords,
            record =>
            {
                Assert.Equal("AAPL", record.Ticker);
                Assert.Equal(TradingAction.BUY, record.Action);
                Assert.Equal(new DateTimeOffset(2026, 1, 15, 14, 30, 0, TimeSpan.Zero), record.Timestamp);
            },
            record =>
            {
                Assert.Equal("MSFT", record.Ticker);
                Assert.Equal(TradingAction.SELL, record.Action);
                Assert.Equal(new DateTimeOffset(2026, 1, 28, 16, 0, 0, TimeSpan.Zero), record.Timestamp);
            });
    }

    [Fact]
    public void ReadAll_ReturnsEmptyWhenFileDoesNotExist()
    {
        using var workspace = new TestWorkspace();
        var reader = new JsonlAuditReader(workspace.GetPath("logs/missing.jsonl"));

        var result = reader.ReadAll();

        Assert.Equal(0, result.TotalRecords);
        Assert.Empty(result.BuySellRecords);
    }

    [Fact]
    public void ReadAll_CountsAllValidRowsIncludingNonBuySell()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("logs/decisions.jsonl", """
{"ticker":"AAPL","action":"Action.BUY","timestamp":"2026-01-15T14:30:00Z"}
{"ticker":"AAPL","action":"Action.WAIT","timestamp":"2026-01-16T14:30:00Z"}
{"ticker":"MSFT","action":"SELL","timestamp":"2026-01-28T16:00:00Z"}
{"ticker":"NVDA","action":"IGNORE","timestamp":"2026-03-01T14:30:00Z"}
""");

        var reader = new JsonlAuditReader(workspace.GetPath("logs/decisions.jsonl"));

        var result = reader.ReadAll();

        Assert.Equal(4, result.TotalRecords);
        Assert.Collection(
            result.BuySellRecords,
            record =>
            {
                Assert.Equal("AAPL", record.Ticker);
                Assert.Equal(TradingAction.BUY, record.Action);
            },
            record =>
            {
                Assert.Equal("MSFT", record.Ticker);
                Assert.Equal(TradingAction.SELL, record.Action);
            });
    }
}
