using System.Text.Json;
using BlowingCandles.Domain.Enums;
using BlowingCandles.Domain.Models;
using BlowingCandles.Infrastructure.Audit;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Infrastructure.Tests;

public sealed class JsonlAuditWriterTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Append_WritesJsonlWithPrefixedEnumStrings()
    {
        var writer = new JsonlAuditWriter(_workspace.GetPath("audit.jsonl"));
        var timestamp = new DateTimeOffset(2026, 3, 7, 14, 30, 0, TimeSpan.Zero);

        var signals = new[]
        {
            new FinalSignal("AAPL", TradingAction.BUY, NewsState.TRADE_OK, TradingAction.BUY, "SCORE_80", timestamp)
        };

        writer.Append(signals);

        var lines = File.ReadAllLines(_workspace.GetPath("audit.jsonl"));
        Assert.Single(lines);

        using var doc = JsonDocument.Parse(lines[0]);
        var root = doc.RootElement;
        Assert.Equal("AAPL", root.GetProperty("ticker").GetString());
        Assert.Equal("Action.BUY", root.GetProperty("action").GetString());
        Assert.Equal("NewsState.TRADE_OK", root.GetProperty("news_state").GetString());
        Assert.Equal("Action.BUY", root.GetProperty("market_action").GetString());
        Assert.Equal("SCORE_80", root.GetProperty("reason").GetString());
        Assert.Equal("2026-03-07T14:30:00+00:00", root.GetProperty("timestamp").GetString());
    }

    [Fact]
    public void Append_TwiceDoesNotTruncate()
    {
        var writer = new JsonlAuditWriter(_workspace.GetPath("audit.jsonl"));
        var timestamp = new DateTimeOffset(2026, 3, 7, 14, 30, 0, TimeSpan.Zero);

        var batch1 = new[]
        {
            new FinalSignal("AAPL", TradingAction.BUY, NewsState.TRADE_OK, TradingAction.BUY, "SCORE_80", timestamp)
        };
        var batch2 = new[]
        {
            new FinalSignal("MSFT", TradingAction.WAIT, NewsState.WAIT, TradingAction.WAIT, "NO_DATA", timestamp)
        };

        writer.Append(batch1);
        writer.Append(batch2);

        var lines = File.ReadAllLines(_workspace.GetPath("audit.jsonl"));
        Assert.Equal(2, lines.Length);

        using var doc1 = JsonDocument.Parse(lines[0]);
        Assert.Equal("AAPL", doc1.RootElement.GetProperty("ticker").GetString());

        using var doc2 = JsonDocument.Parse(lines[1]);
        Assert.Equal("MSFT", doc2.RootElement.GetProperty("ticker").GetString());
        Assert.Equal("Action.WAIT", doc2.RootElement.GetProperty("action").GetString());
        Assert.Equal("NewsState.WAIT", doc2.RootElement.GetProperty("news_state").GetString());
    }

    [Fact]
    public void Append_CreatesDirectoryIfMissing()
    {
        var writer = new JsonlAuditWriter(_workspace.GetPath("logs/audit.jsonl"));
        var timestamp = new DateTimeOffset(2026, 3, 7, 14, 30, 0, TimeSpan.Zero);

        var signals = new[]
        {
            new FinalSignal("AAPL", TradingAction.WAIT, NewsState.WAIT, TradingAction.WAIT, "TEST", timestamp)
        };

        writer.Append(signals);

        Assert.True(File.Exists(_workspace.GetPath("logs/audit.jsonl")));
    }

    [Fact]
    public void Append_MultipleSignals_WritesOneLinePerSignal()
    {
        var writer = new JsonlAuditWriter(_workspace.GetPath("audit.jsonl"));
        var timestamp = new DateTimeOffset(2026, 3, 7, 14, 30, 0, TimeSpan.Zero);

        var signals = new[]
        {
            new FinalSignal("AAPL", TradingAction.BUY, NewsState.TRADE_OK, TradingAction.BUY, "SCORE_80", timestamp),
            new FinalSignal("MSFT", TradingAction.SELL, NewsState.TRADE_OK, TradingAction.SELL, "SCORE_LOW", timestamp),
            new FinalSignal("NVDA", TradingAction.WAIT, NewsState.NO_TRADE, TradingAction.WAIT, "EARNINGS", timestamp)
        };

        writer.Append(signals);

        var lines = File.ReadAllLines(_workspace.GetPath("audit.jsonl"));
        Assert.Equal(3, lines.Length);

        using var doc3 = JsonDocument.Parse(lines[2]);
        Assert.Equal("Action.WAIT", doc3.RootElement.GetProperty("action").GetString());
        Assert.Equal("NewsState.NO_TRADE", doc3.RootElement.GetProperty("news_state").GetString());
    }
}
