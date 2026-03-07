using BlowingCandles.Domain.Models;

namespace BlowingCandles.Infrastructure.Audit;

public sealed record AuditReadResult(
    int TotalRecords,
    IReadOnlyList<AuditRecord> BuySellRecords);
