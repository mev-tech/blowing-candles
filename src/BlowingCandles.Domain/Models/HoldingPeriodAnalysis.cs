namespace BlowingCandles.Domain.Models;

public sealed record HoldingPeriodAnalysis(
    IReadOnlyList<HoldingPeriod> CompletedPeriods,
    IReadOnlyList<OpenPosition> OpenPositions);
