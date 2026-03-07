using BlowingCandles.Infrastructure.Clock;
using BlowingCandles.Infrastructure.State;

namespace BlowingCandles.Infrastructure.Tests;

public sealed class JsonStateStoreTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Load_MissingFile_ReturnsEmptyStateForToday()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero));
        var store = new JsonStateStore(_workspace.GetPath("state.json"));

        var state = store.Load(clock);

        Assert.Equal("2026-03-07", state.Day);
        Assert.Equal(0, state.BuysToday);
        Assert.Null(state.LastBuyAt);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsEmptyStateForToday()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero));
        _workspace.WriteFile("state.json", "not valid json {{{");
        var store = new JsonStateStore(_workspace.GetPath("state.json"));

        var state = store.Load(clock);

        Assert.Equal("2026-03-07", state.Day);
        Assert.Equal(0, state.BuysToday);
        Assert.Null(state.LastBuyAt);
    }

    [Fact]
    public void Load_SameDay_ReturnsLoadedStateUnchanged()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 7, 14, 0, 0, TimeSpan.Zero));
        _workspace.WriteFile("state.json",
            """{"day":"2026-03-07","buys_today":2,"last_buy_at":"2026-03-07T10:00:00+00:00"}""");
        var store = new JsonStateStore(_workspace.GetPath("state.json"));

        var state = store.Load(clock);

        Assert.Equal("2026-03-07", state.Day);
        Assert.Equal(2, state.BuysToday);
        Assert.NotNull(state.LastBuyAt);
    }

    [Fact]
    public void Load_DifferentDay_ResetsState()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero));
        _workspace.WriteFile("state.json",
            """{"day":"2026-03-07","buys_today":3,"last_buy_at":"2026-03-07T10:00:00+00:00"}""");
        var store = new JsonStateStore(_workspace.GetPath("state.json"));

        var state = store.Load(clock);

        Assert.Equal("2026-03-08", state.Day);
        Assert.Equal(0, state.BuysToday);
        Assert.Null(state.LastBuyAt);
    }

    [Fact]
    public void RecordBuy_IncrementsBuysAndSetsLastBuyAt()
    {
        var buyTime = new DateTimeOffset(2026, 3, 7, 14, 30, 0, TimeSpan.Zero);
        var initial = new JsonStateStore.StateSnapshot
        {
            Day = "2026-03-07",
            BuysToday = 1,
            LastBuyAt = null
        };
        var store = new JsonStateStore(_workspace.GetPath("state.json"));

        var updated = store.RecordBuy(initial, buyTime);

        Assert.Equal(2, updated.BuysToday);
        Assert.Equal(buyTime, updated.LastBuyAt);
    }

    [Fact]
    public void SaveAndReload_RoundTripsCorrectly()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero));
        var buyTime = new DateTimeOffset(2026, 3, 7, 14, 30, 0, TimeSpan.Zero);
        var store = new JsonStateStore(_workspace.GetPath("data/state.json"));

        var original = new JsonStateStore.StateSnapshot
        {
            Day = "2026-03-07",
            BuysToday = 2,
            LastBuyAt = buyTime
        };

        store.Save(original);
        var reloaded = store.Load(clock);

        Assert.Equal(original.Day, reloaded.Day);
        Assert.Equal(original.BuysToday, reloaded.BuysToday);
        Assert.Equal(original.LastBuyAt, reloaded.LastBuyAt);
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        var store = new JsonStateStore(_workspace.GetPath("nested/dir/state.json"));
        var state = new JsonStateStore.StateSnapshot
        {
            Day = "2026-03-07",
            BuysToday = 0,
            LastBuyAt = null
        };

        store.Save(state);

        Assert.True(File.Exists(_workspace.GetPath("nested/dir/state.json")));
    }

    [Fact]
    public void Load_EmptyFile_ReturnsEmptyState()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero));
        _workspace.WriteFile("state.json", "");
        var store = new JsonStateStore(_workspace.GetPath("state.json"));

        var state = store.Load(clock);

        Assert.Equal("2026-03-07", state.Day);
        Assert.Equal(0, state.BuysToday);
        Assert.Null(state.LastBuyAt);
    }
}
