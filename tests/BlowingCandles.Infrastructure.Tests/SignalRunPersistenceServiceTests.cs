using BlowingCandles.Domain.Enums;
using BlowingCandles.Domain.Models;
using BlowingCandles.Infrastructure.Persistence;
using BlowingCandles.Infrastructure.Persistence.Entities;
using BlowingCandles.Infrastructure.Persistence.Models;
using BlowingCandles.Infrastructure.Persistence.Services;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Infrastructure.Tests;

[Collection(PostgresContainerCollection.Name)]
public sealed class SignalRunPersistenceServiceTests
{
    private readonly PostgresContainerFixture _fixture;

    public SignalRunPersistenceServiceTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PersistRun_Success_WritesCompletedRunResultsAndGovernorState()
    {
        await _fixture.ResetAsync();
        await using (var dbContext = _fixture.CreateDbContext())
        {
            var service = new SignalRunPersistenceService(dbContext);
            var result = service.PersistRun(
                new PersistSignalRunRequest(
                    SignalRunType.Realtime,
                    "cli",
                    new DateOnly(2026, 3, 8),
                    false,
                    new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 3, 8, 20, 1, 0, TimeSpan.Zero),
                    [
                        CreateSignal("AAPL", TradingAction.BUY, "ABOVE_SMA200"),
                        CreateSignal("MSFT", TradingAction.WAIT, "NEWS_WAIT"),
                        CreateSignal("NVDA", TradingAction.SELL, "RSI_LT_40")
                    ],
                    new TradeGovernorState("2026-03-08", 1, new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero)),
                    "live"));

            Assert.Equal(1L, result.RunId);
            Assert.Equal(SignalRunStatus.Completed, result.Status);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var run = Assert.Single(verificationContext.SignalRuns);
        var results = verificationContext.SignalRunResults
            .OrderBy(x => x.Ticker)
            .ToArray();
        var state = Assert.Single(verificationContext.TradeGovernorStates);

        Assert.Equal(SignalRunStatus.Completed, run.Status);
        Assert.Equal(SignalRunType.Realtime, run.RunType);
        Assert.False(run.IsSimulation);
        Assert.Equal(3, run.TickerCount);
        Assert.Equal("cli", run.Trigger);
        Assert.Equal(3, results.Length);
        Assert.All(results, x => Assert.Equal(run.Id, x.RunId));
        Assert.Equal(["AAPL", "MSFT", "NVDA"], results.Select(x => x.Ticker).ToArray());
        Assert.Equal("live", state.Mode);
        Assert.Equal("2026-03-08", state.Day);
        Assert.Equal(1, state.BuysToday);
    }

    [Fact]
    public async Task PersistRun_EmptySignals_WritesCompletedRunWithoutResults()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        var service = new SignalRunPersistenceService(dbContext);

        var result = service.PersistRun(
            new PersistSignalRunRequest(
                SignalRunType.Realtime,
                "worker",
                new DateOnly(2026, 3, 8),
                false,
                new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 8, 20, 0, 5, TimeSpan.Zero),
                []));

        var run = Assert.Single(dbContext.SignalRuns);

        Assert.Equal(SignalRunStatus.Completed, result.Status);
        Assert.Equal(0, run.TickerCount);
        Assert.Equal(SignalRunStatus.Completed, run.Status);
        Assert.Empty(dbContext.SignalRunResults);
    }

    [Fact]
    public async Task PersistRun_ErrorMessage_WritesFailedRunWithoutResults()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        var service = new SignalRunPersistenceService(dbContext);

        var result = service.PersistRun(
            new PersistSignalRunRequest(
                SignalRunType.Realtime,
                "api",
                new DateOnly(2026, 3, 8),
                false,
                new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 8, 20, 0, 2, TimeSpan.Zero),
                [],
                ErrorMessage: "pipeline failed"));

        var run = Assert.Single(dbContext.SignalRuns);

        Assert.Equal(SignalRunStatus.Failed, result.Status);
        Assert.Equal(SignalRunStatus.Failed, run.Status);
        Assert.Equal(0, run.TickerCount);
        Assert.Equal("pipeline failed", run.ErrorMessage);
        Assert.Empty(dbContext.SignalRunResults);
    }

    [Fact]
    public async Task PersistRun_FailedRunWithSignals_ThrowsArgumentException()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        var service = new SignalRunPersistenceService(dbContext);

        Assert.Throws<ArgumentException>(
            () => service.PersistRun(
                new PersistSignalRunRequest(
                    SignalRunType.Realtime,
                    "api",
                    new DateOnly(2026, 3, 8),
                    false,
                    new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 3, 8, 20, 0, 2, TimeSpan.Zero),
                    [CreateSignal("AAPL", TradingAction.WAIT, "DATA_ERROR")],
                    ErrorMessage: "pipeline failed")));

        Assert.Empty(dbContext.SignalRuns);
        Assert.Empty(dbContext.SignalRunResults);
    }

    [Fact]
    public async Task PersistRun_NormalizesTickerAndTruncatesReason()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        var service = new SignalRunPersistenceService(dbContext);
        var longReason = new string('R', 300);

        service.PersistRun(
            new PersistSignalRunRequest(
                SignalRunType.Realtime,
                "cli",
                new DateOnly(2026, 3, 8),
                false,
                new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 8, 20, 0, 1, TimeSpan.Zero),
                [CreateSignal(" aapl ", TradingAction.BUY, longReason)]));

        var signal = Assert.Single(dbContext.SignalRunResults);

        Assert.Equal("AAPL", signal.Ticker);
        Assert.Equal(256, signal.Reason.Length);
    }

    [Fact]
    public async Task PersistRun_DuplicateNormalizedTickers_DeduplicatesLastWins()
    {
        await _fixture.ResetAsync();

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var service = new SignalRunPersistenceService(dbContext);

            var result = service.PersistRun(
                new PersistSignalRunRequest(
                    SignalRunType.Realtime,
                    "cli",
                    new DateOnly(2026, 3, 8),
                    false,
                    new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 3, 8, 20, 0, 1, TimeSpan.Zero),
                    [
                        CreateSignal("AAPL", TradingAction.BUY, string.Empty),
                        CreateSignal(" aapl ", TradingAction.WAIT, string.Empty)
                    ]));

            Assert.Equal(SignalRunStatus.Completed, result.Status);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var run = Assert.Single(verificationContext.SignalRuns);
        var persistedResult = Assert.Single(verificationContext.SignalRunResults);

        Assert.Equal(SignalRunStatus.Completed, run.Status);
        Assert.Equal(1, run.TickerCount);
        Assert.Equal("AAPL", persistedResult.Ticker);
        Assert.Equal(TradingAction.WAIT, persistedResult.Action);
    }

    [Fact]
    public async Task PersistRun_SimulationMetadata_WritesAsOfRunAndSimulationState()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        var service = new SignalRunPersistenceService(dbContext);

        service.PersistRun(
            new PersistSignalRunRequest(
                SignalRunType.AsOf,
                "cli",
                new DateOnly(2026, 3, 7),
                true,
                new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 7, 0, 0, 1, TimeSpan.Zero),
                [CreateSignal("TSLA", TradingAction.WAIT, "MARKET_DATA_ERROR")],
                new TradeGovernorState("2026-03-07", 0, null),
                "simulation"));

        var run = Assert.Single(dbContext.SignalRuns);
        var state = Assert.Single(dbContext.TradeGovernorStates);

        Assert.Equal(SignalRunType.AsOf, run.RunType);
        Assert.True(run.IsSimulation);
        Assert.Equal(new DateOnly(2026, 3, 7), run.AsOfDate);
        Assert.Equal("simulation", state.Mode);
    }

    private static FinalSignal CreateSignal(string ticker, TradingAction action, string reason)
    {
        return new FinalSignal(
            ticker,
            action,
            NewsState.TRADE_OK,
            action,
            reason,
            new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero));
    }
}
