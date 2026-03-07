using System.Text.Json;
using BlowingCandles.Application;
using BlowingCandles.Domain.Enums;
using BlowingCandles.Domain.Models;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Application.Tests;

public sealed class OutputRendererTests : IDisposable
{
    private readonly string _tempDir;

    public OutputRendererTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"blowing-candles-renderer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string GetPath(string name) => Path.Combine(_tempDir, name);

    [Fact]
    public void WriteSignals_TextFile_UsesPrefixedEnumStrings()
    {
        var renderer = new OutputRenderer();
        var timestamp = new DateTimeOffset(2026, 3, 7, 14, 30, 0, TimeSpan.Zero);

        var signals = new[]
        {
            new FinalSignal("AAPL", TradingAction.BUY, NewsState.TRADE_OK, TradingAction.BUY, "SCORE_80", timestamp),
            new FinalSignal("MSFT", TradingAction.WAIT, NewsState.WAIT, TradingAction.WAIT, "NO_DATA", timestamp)
        };

        renderer.WriteSignals(GetPath("signals.txt"), GetPath("signals.json"), signals);

        var lines = File.ReadAllLines(GetPath("signals.txt"));
        Assert.Equal(2, lines.Length);
        Assert.Equal("AAPL: Action.BUY | NewsState.TRADE_OK | Action.BUY | SCORE_80", lines[0]);
        Assert.Equal("MSFT: Action.WAIT | NewsState.WAIT | Action.WAIT | NO_DATA", lines[1]);
    }

    [Fact]
    public void WriteSignals_JsonFile_UsesPlainEnumStrings()
    {
        var renderer = new OutputRenderer();
        var timestamp = new DateTimeOffset(2026, 3, 7, 14, 30, 0, TimeSpan.Zero);

        var signals = new[]
        {
            new FinalSignal("AAPL", TradingAction.BUY, NewsState.TRADE_OK, TradingAction.BUY, "SCORE_80", timestamp)
        };

        renderer.WriteSignals(GetPath("signals.txt"), GetPath("signals.json"), signals);

        var json = File.ReadAllText(GetPath("signals.json"));
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, arr.ValueKind);
        Assert.Equal(1, arr.GetArrayLength());

        var item = arr[0];
        Assert.Equal("AAPL", item.GetProperty("ticker").GetString());
        Assert.Equal("BUY", item.GetProperty("action").GetString());
        Assert.Equal("TRADE_OK", item.GetProperty("newsState").GetString());
        Assert.Equal("BUY", item.GetProperty("marketAction").GetString());
        Assert.Equal("SCORE_80", item.GetProperty("reason").GetString());
        Assert.Equal("2026-03-07T14:30:00+00:00", item.GetProperty("timestamp").GetString());
    }

    [Fact]
    public void WriteSignals_EmptyList_WritesEmptyOutput()
    {
        var renderer = new OutputRenderer();

        renderer.WriteSignals(GetPath("signals.txt"), GetPath("signals.json"), Array.Empty<FinalSignal>());

        var textLines = File.ReadAllLines(GetPath("signals.txt"));
        Assert.Empty(textLines);

        var json = File.ReadAllText(GetPath("signals.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public void WriteSignals_CreatesDirectoriesIfMissing()
    {
        var renderer = new OutputRenderer();
        var timestamp = new DateTimeOffset(2026, 3, 7, 14, 30, 0, TimeSpan.Zero);

        var signals = new[]
        {
            new FinalSignal("AAPL", TradingAction.WAIT, NewsState.WAIT, TradingAction.WAIT, "TEST", timestamp)
        };

        renderer.WriteSignals(
            GetPath("output/text/signals.txt"),
            GetPath("output/json/signals.json"),
            signals);

        Assert.True(File.Exists(GetPath("output/text/signals.txt")));
        Assert.True(File.Exists(GetPath("output/json/signals.json")));
    }
}
