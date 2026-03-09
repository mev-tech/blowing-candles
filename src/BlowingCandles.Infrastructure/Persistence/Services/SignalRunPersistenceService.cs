using BlowingCandles.Domain.Models;
using BlowingCandles.Infrastructure.Persistence.Entities;
using BlowingCandles.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BlowingCandles.Infrastructure.Persistence.Services;

public sealed class SignalRunPersistenceService
{
    private readonly AppDbContext _dbContext;

    public SignalRunPersistenceService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public SignalRunPersistenceResult PersistRun(PersistSignalRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrWhiteSpace(request.ErrorMessage) && request.Signals.Count > 0)
        {
            throw new ArgumentException(
                "A failed run must not include signals. Pass an empty signal list when ErrorMessage is set.",
                nameof(request));
        }

        var run = new SignalRunEntity
        {
            RunType = request.RunType,
            Trigger = TrimToLength(request.Trigger, SignalRunPersistenceLimits.TriggerMaxLength)
                ?? throw new ArgumentException("Trigger is required.", nameof(request)),
            Status = SignalRunStatus.Running,
            AsOfDate = request.AsOfDate,
            IsSimulation = request.IsSimulation,
            StartedAtUtc = request.StartedAtUtc
        };

        _dbContext.SignalRuns.Add(run);
        _dbContext.SaveChanges();

        if (!string.IsNullOrWhiteSpace(request.ErrorMessage))
        {
            run.TickerCount = 0;
            run.Status = SignalRunStatus.Failed;
            run.CompletedAtUtc = request.CompletedAtUtc;
            run.ErrorMessage = TrimToLength(
                request.ErrorMessage,
                SignalRunPersistenceLimits.ErrorMessageMaxLength);
            _dbContext.SaveChanges();

            return new SignalRunPersistenceResult(run.Id, run.Status);
        }

        IDbContextTransaction? transaction = null;

        try
        {
            if (_dbContext.Database.IsRelational())
            {
                transaction = _dbContext.Database.BeginTransaction();
            }

            var resultEntities = request.Signals
                .Select(signal => (Signal: signal, Ticker: NormalizeTicker(signal.Ticker)))
                .Where(x => x.Ticker.Length > 0)
                .GroupBy(x => x.Ticker, StringComparer.Ordinal)
                .Select(group => CreateSignalResult(run.Id, group.Last().Signal))
                .ToArray();

            if (resultEntities.Length > 0)
            {
                _dbContext.SignalRunResults.AddRange(resultEntities);
            }

            PersistGovernorState(request);

            run.TickerCount = resultEntities.Length;
            run.Status = SignalRunStatus.Completed;
            run.CompletedAtUtc = request.CompletedAtUtc;
            _dbContext.SaveChanges();
            transaction?.Commit();

            return new SignalRunPersistenceResult(run.Id, run.Status);
        }
        catch
        {
            transaction?.Rollback();
            MarkRunFailed(run.Id, request.CompletedAtUtc, request.ErrorMessage);
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    private void PersistGovernorState(PersistSignalRunRequest request)
    {
        if (request.GovernorState is null && string.IsNullOrWhiteSpace(request.GovernorStateMode))
        {
            return;
        }

        if (request.GovernorState is null || string.IsNullOrWhiteSpace(request.GovernorStateMode))
        {
            throw new ArgumentException(
                "GovernorState and GovernorStateMode must be provided together.",
                nameof(request));
        }

        TradeGovernorStatePersistence.Upsert(
            _dbContext,
            request.GovernorStateMode,
            request.GovernorState);
    }

    private void MarkRunFailed(long runId, DateTimeOffset completedAtUtc, string? errorMessage)
    {
        try
        {
            _dbContext.ChangeTracker.Clear();
            var run = _dbContext.SignalRuns.Single(x => x.Id == runId);
            run.Status = SignalRunStatus.Failed;
            run.CompletedAtUtc = completedAtUtc;
            run.ErrorMessage = TrimToLength(errorMessage, SignalRunPersistenceLimits.ErrorMessageMaxLength);
            _dbContext.SaveChanges();
        }
        catch
        {
        }
    }

    private static SignalRunResultEntity CreateSignalResult(long runId, FinalSignal signal)
    {
        var ticker = NormalizeTicker(signal.Ticker);
        if (ticker.Length == 0)
        {
            throw new InvalidOperationException("Signal ticker is required.");
        }

        return new SignalRunResultEntity
        {
            RunId = runId,
            Ticker = ticker,
            Action = signal.Action,
            NewsState = signal.NewsState,
            MarketAction = signal.MarketAction,
            Reason = TrimToLength(signal.Reason, SignalRunPersistenceLimits.ReasonMaxLength) ?? string.Empty,
            Timestamp = signal.Timestamp.ToUniversalTime()
        };
    }

    private static string NormalizeTicker(string ticker)
    {
        return ticker.Trim().ToUpperInvariant();
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }
}
