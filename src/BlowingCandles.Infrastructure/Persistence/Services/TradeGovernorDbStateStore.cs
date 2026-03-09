using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BlowingCandles.Infrastructure.Persistence.Services;

public sealed class TradeGovernorDbStateStore : ITradeGovernorStateStore
{
    private readonly AppDbContext _dbContext;
    private readonly string _mode;

    public TradeGovernorDbStateStore(AppDbContext dbContext, string mode)
    {
        _dbContext = dbContext;
        _mode = TradeGovernorStatePersistence.NormalizeMode(mode);
    }

    public TradeGovernorState Load(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var currentDay = TradeGovernorStatePersistence.GetCurrentDay(clock);
        var entity = _dbContext.TradeGovernorStates
            .AsNoTracking()
            .SingleOrDefault(x => x.Mode == _mode);

        if (entity is null)
        {
            return TradeGovernorStatePersistence.CreateEmptyState(currentDay);
        }

        if (entity.Day == currentDay)
        {
            return new TradeGovernorState(entity.Day, entity.BuysToday, entity.LastBuyAt);
        }

        var resetState = TradeGovernorStatePersistence.CreateEmptyState(currentDay);
        Save(resetState);
        return resetState;
    }

    public void Save(TradeGovernorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        try
        {
            TradeGovernorStatePersistence.Upsert(_dbContext, _mode, state);
            _dbContext.SaveChanges();
        }
        // Retry on concurrent insert race: if another caller inserted the row between our
        // SingleOrDefault check and our Add, the unique index on mode causes DbUpdateException.
        // If the row exists, update it (last writer wins). If not, the error is unrelated - re-throw.
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            var existing = _dbContext.TradeGovernorStates.SingleOrDefault(x => x.Mode == _mode);
            if (existing is null)
            {
                throw;
            }

            var normalizedState = TradeGovernorStatePersistence.NormalizeState(state);
            existing.Day = normalizedState.Day;
            existing.BuysToday = normalizedState.BuysToday;
            existing.LastBuyAt = normalizedState.LastBuyAt;
            _dbContext.SaveChanges();
        }
    }
}

internal static class TradeGovernorStatePersistence
{
    internal const string LiveMode = "live";
    internal const string SimulationMode = "simulation";

    public static string NormalizeMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            throw new ArgumentException("Mode is required.", nameof(mode));
        }

        return mode.Trim().ToLowerInvariant() switch
        {
            LiveMode => LiveMode,
            SimulationMode => SimulationMode,
            _ => throw new ArgumentException("Mode must be 'live' or 'simulation'.", nameof(mode))
        };
    }

    public static string GetCurrentDay(IClock clock)
    {
        return DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).ToString("yyyy-MM-dd");
    }

    public static TradeGovernorState CreateEmptyState(string day)
    {
        return new TradeGovernorState(day, 0, null);
    }

    public static TradeGovernorState NormalizeState(TradeGovernorState state)
    {
        if (string.IsNullOrWhiteSpace(state.Day))
        {
            throw new ArgumentException("State day is required.", nameof(state));
        }

        if (state.BuysToday < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "BuysToday cannot be negative.");
        }

        var normalizedDay = state.Day.Trim();
        if (normalizedDay.Length > SignalRunPersistenceLimits.DayMaxLength)
        {
            normalizedDay = normalizedDay[..SignalRunPersistenceLimits.DayMaxLength];
        }

        return new TradeGovernorState(
            normalizedDay,
            state.BuysToday,
            state.LastBuyAt?.ToUniversalTime());
    }

    public static void Upsert(AppDbContext dbContext, string mode, TradeGovernorState state)
    {
        var normalizedMode = NormalizeMode(mode);
        var normalizedState = NormalizeState(state);

        var entity = dbContext.TradeGovernorStates.SingleOrDefault(x => x.Mode == normalizedMode);
        if (entity is null)
        {
            dbContext.TradeGovernorStates.Add(
                new Entities.TradeGovernorStateEntity
                {
                    Mode = normalizedMode,
                    Day = normalizedState.Day,
                    BuysToday = normalizedState.BuysToday,
                    LastBuyAt = normalizedState.LastBuyAt
                });
            return;
        }

        entity.Day = normalizedState.Day;
        entity.BuysToday = normalizedState.BuysToday;
        entity.LastBuyAt = normalizedState.LastBuyAt;
    }
}
