using BlowingCandles.Domain.Models;
using BlowingCandles.Domain.Services;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Domain.Tests;

public sealed class HoldingPeriodCalculatorTests
{
    [Fact]
    public void Calculate_PairsBuyAndSellPerTickerInFifoOrder()
    {
        var calculator = new HoldingPeriodCalculator();
        var records = new[]
        {
            new AuditRecord("AAPL", TradingAction.BUY, new DateTimeOffset(2026, 1, 1, 14, 30, 0, TimeSpan.Zero)),
            new AuditRecord("AAPL", TradingAction.BUY, new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero)),
            new AuditRecord("AAPL", TradingAction.SELL, new DateTimeOffset(2026, 1, 5, 14, 30, 0, TimeSpan.Zero))
        };

        var analysis = calculator.Calculate(records);

        var period = Assert.Single(analysis.CompletedPeriods);
        Assert.Equal("AAPL", period.Ticker);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 14, 30, 0, TimeSpan.Zero), period.BuyTimestamp);
        Assert.Equal(new DateTimeOffset(2026, 1, 5, 14, 30, 0, TimeSpan.Zero), period.SellTimestamp);
        Assert.Equal(TimeSpan.FromDays(4), period.Duration);

        var openPosition = Assert.Single(analysis.OpenPositions);
        Assert.Equal("AAPL", openPosition.Ticker);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero), openPosition.BuyTimestamp);
    }

    [Fact]
    public void Calculate_IgnoresOrphanSellAndOrdersCompletedPeriodsByBuyTimestamp()
    {
        var calculator = new HoldingPeriodCalculator();
        var records = new[]
        {
            new AuditRecord("AAPL", TradingAction.BUY, new DateTimeOffset(2026, 1, 15, 14, 30, 0, TimeSpan.Zero)),
            new AuditRecord("MSFT", TradingAction.BUY, new DateTimeOffset(2026, 1, 20, 14, 0, 0, TimeSpan.Zero)),
            new AuditRecord("GOOG", TradingAction.SELL, new DateTimeOffset(2026, 1, 21, 14, 0, 0, TimeSpan.Zero)),
            new AuditRecord("MSFT", TradingAction.SELL, new DateTimeOffset(2026, 1, 28, 16, 0, 0, TimeSpan.Zero)),
            new AuditRecord("AAPL", TradingAction.SELL, new DateTimeOffset(2026, 2, 10, 15, 0, 0, TimeSpan.Zero))
        };

        var analysis = calculator.Calculate(records);

        Assert.Equal(2, analysis.CompletedPeriods.Count);
        Assert.Collection(
            analysis.CompletedPeriods,
            period =>
            {
                Assert.Equal("AAPL", period.Ticker);
                Assert.Equal(new DateTimeOffset(2026, 1, 15, 14, 30, 0, TimeSpan.Zero), period.BuyTimestamp);
            },
            period =>
            {
                Assert.Equal("MSFT", period.Ticker);
                Assert.Equal(new DateTimeOffset(2026, 1, 20, 14, 0, 0, TimeSpan.Zero), period.BuyTimestamp);
            });

        Assert.Empty(analysis.OpenPositions);
    }

    [Fact]
    public void Calculate_HandlesMultipleBuysThenMultipleSellsForSameTicker()
    {
        var calculator = new HoldingPeriodCalculator();
        var records = new[]
        {
            new AuditRecord("AAPL", TradingAction.BUY, new DateTimeOffset(2026, 1, 1, 14, 30, 0, TimeSpan.Zero)),
            new AuditRecord("AAPL", TradingAction.BUY, new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero)),
            new AuditRecord("AAPL", TradingAction.BUY, new DateTimeOffset(2026, 1, 3, 14, 30, 0, TimeSpan.Zero)),
            new AuditRecord("AAPL", TradingAction.SELL, new DateTimeOffset(2026, 1, 5, 14, 30, 0, TimeSpan.Zero)),
            new AuditRecord("AAPL", TradingAction.SELL, new DateTimeOffset(2026, 1, 6, 14, 30, 0, TimeSpan.Zero))
        };

        var analysis = calculator.Calculate(records);

        Assert.Equal(2, analysis.CompletedPeriods.Count);
        Assert.Collection(
            analysis.CompletedPeriods,
            period =>
            {
                Assert.Equal("AAPL", period.Ticker);
                Assert.Equal(new DateTimeOffset(2026, 1, 1, 14, 30, 0, TimeSpan.Zero), period.BuyTimestamp);
                Assert.Equal(new DateTimeOffset(2026, 1, 5, 14, 30, 0, TimeSpan.Zero), period.SellTimestamp);
                Assert.Equal(TimeSpan.FromDays(4), period.Duration);
            },
            period =>
            {
                Assert.Equal("AAPL", period.Ticker);
                Assert.Equal(new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero), period.BuyTimestamp);
                Assert.Equal(new DateTimeOffset(2026, 1, 6, 14, 30, 0, TimeSpan.Zero), period.SellTimestamp);
                Assert.Equal(TimeSpan.FromDays(4), period.Duration);
            });

        var openPosition = Assert.Single(analysis.OpenPositions);
        Assert.Equal("AAPL", openPosition.Ticker);
        Assert.Equal(new DateTimeOffset(2026, 1, 3, 14, 30, 0, TimeSpan.Zero), openPosition.BuyTimestamp);
    }

    [Fact]
    public void Calculate_ReturnsEmptyWhenNoRecords()
    {
        var calculator = new HoldingPeriodCalculator();

        var analysis = calculator.Calculate(Array.Empty<AuditRecord>());

        Assert.Empty(analysis.CompletedPeriods);
        Assert.Empty(analysis.OpenPositions);
    }
}
