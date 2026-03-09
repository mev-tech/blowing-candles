using BlowingCandles.Domain.Models;
using BlowingCandles.Infrastructure.Clock;
using BlowingCandles.Infrastructure.Persistence;
using BlowingCandles.Infrastructure.Persistence.Entities;
using BlowingCandles.Infrastructure.Persistence.Services;

namespace BlowingCandles.Infrastructure.Tests;

[Collection(PostgresContainerCollection.Name)]
public sealed class TradeGovernorDbStateStoreTests
{
    private readonly PostgresContainerFixture _fixture;

    public TradeGovernorDbStateStoreTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Load_NoExistingRow_ReturnsEmptyStateForToday()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        var store = new TradeGovernorDbStateStore(dbContext, "live");
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.Zero));

        var state = store.Load(clock);

        Assert.Equal("2026-03-08", state.Day);
        Assert.Equal(0, state.BuysToday);
        Assert.Null(state.LastBuyAt);
    }

    [Fact]
    public async Task Load_MatchingDay_ReturnsStoredState()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.TradeGovernorStates.Add(
            new TradeGovernorStateEntity
            {
                Mode = "live",
                Day = "2026-03-08",
                BuysToday = 2,
                LastBuyAt = new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero)
            });
        await dbContext.SaveChangesAsync();
        var store = new TradeGovernorDbStateStore(dbContext, "live");
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.Zero));

        var state = store.Load(clock);

        Assert.Equal("2026-03-08", state.Day);
        Assert.Equal(2, state.BuysToday);
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero), state.LastBuyAt);
    }

    [Fact]
    public async Task Load_StaleDay_ResetsAndUpdatesExistingRow()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.TradeGovernorStates.Add(
            new TradeGovernorStateEntity
            {
                Mode = "live",
                Day = "2026-03-07",
                BuysToday = 3,
                LastBuyAt = new DateTimeOffset(2026, 3, 7, 10, 0, 0, TimeSpan.Zero)
            });
        await dbContext.SaveChangesAsync();
        var store = new TradeGovernorDbStateStore(dbContext, "live");
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.Zero));

        var state = store.Load(clock);

        Assert.Equal("2026-03-08", state.Day);
        Assert.Equal(0, state.BuysToday);
        Assert.Null(state.LastBuyAt);

        var persisted = Assert.Single(dbContext.TradeGovernorStates);
        Assert.Equal("2026-03-08", persisted.Day);
        Assert.Equal(0, persisted.BuysToday);
        Assert.Null(persisted.LastBuyAt);
    }

    [Fact]
    public async Task Save_CreatesAndUpdatesSingleRowPerMode()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        var store = new TradeGovernorDbStateStore(dbContext, "live");

        store.Save(new TradeGovernorState("2026-03-08", 1, null));
        store.Save(new TradeGovernorState("2026-03-08", 2, new DateTimeOffset(2026, 3, 8, 11, 0, 0, TimeSpan.Zero)));

        var persisted = Assert.Single(dbContext.TradeGovernorStates);
        Assert.Equal("live", persisted.Mode);
        Assert.Equal("2026-03-08", persisted.Day);
        Assert.Equal(2, persisted.BuysToday);
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 11, 0, 0, TimeSpan.Zero), persisted.LastBuyAt);
    }

    [Fact]
    public async Task Save_LiveAndSimulationModesRemainIsolated()
    {
        await _fixture.ResetAsync();

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var liveStore = new TradeGovernorDbStateStore(dbContext, "live");
            var simulationStore = new TradeGovernorDbStateStore(dbContext, "simulation");

            liveStore.Save(new TradeGovernorState("2026-03-08", 1, null));
            simulationStore.Save(new TradeGovernorState("2026-03-08", 4, null));
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var rows = verificationContext.TradeGovernorStates
            .OrderBy(x => x.Mode)
            .ToArray();

        Assert.Equal(2, rows.Length);
        Assert.Equal("live", rows[0].Mode);
        Assert.Equal(1, rows[0].BuysToday);
        Assert.Equal("simulation", rows[1].Mode);
        Assert.Equal(4, rows[1].BuysToday);
    }
}
