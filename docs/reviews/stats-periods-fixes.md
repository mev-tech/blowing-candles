# Stats Periods — Required Fixes

## Summary of Issues

1. **`totalRecords` counts only BUY/SELL records instead of all audit records.** The `JsonlAuditReader.ReadAll()` filters to BUY/SELL at parse time, so `records.Count` excludes WAIT, IGNORE, and other action rows. The spec and Python reference report the total number of records in the file, not just the filtered subset.

2. **Missing test coverage for edge cases.** Several scenarios from the feature spec's test matrix are not covered: empty audit file (exists but zero lines), audit file with only non-BUY/SELL actions, and multiple BUYs followed by multiple SELLs for the same ticker.

## Required Changes

### 1. Expose total record count from `JsonlAuditReader`

`JsonlAuditReader.ReadAll()` currently returns only BUY/SELL records. The handler needs access to the total number of parsed lines (excluding blank/malformed lines) to report "Total records" correctly.

**Approach:** Introduce a result type that carries both the total count of successfully parsed JSONL rows (all actions) and the filtered BUY/SELL records. The filtering to BUY/SELL remains — only the count changes.

- Add a result record (e.g., `AuditReadResult`) with two members: `int TotalRecords` and `IReadOnlyList<AuditRecord> BuySellRecords`.
- Update `JsonlAuditReader.ReadAll()` to return `AuditReadResult`.
- Count every successfully parsed JSON row with valid `ticker`, `action`, and `timestamp` fields — regardless of action type — toward `TotalRecords`.
- `BuySellRecords` contains only BUY and SELL records, as before.

### 2. Update `StatsPeriodsHandler` to use the new total count

- Change `StatsPeriodsHandler.Handle()` to use `AuditReadResult.TotalRecords` for the "Total records" line instead of `records.Count`.
- Pass `AuditReadResult.BuySellRecords` to `HoldingPeriodCalculator.Calculate()`.

### 3. Add missing test scenarios

Add tests that cover the gaps identified in the feature spec's test matrix.

## Affected Files

| File | Change |
|------|--------|
| `src/BlowingCandles.Infrastructure/Audit/JsonlAuditReader.cs` | Return `AuditReadResult` instead of `IReadOnlyList<AuditRecord>`. Count all valid rows, filter BUY/SELL into the records list. |
| `src/BlowingCandles.Cli/Handlers/StatsPeriodsHandler.cs` | Consume `AuditReadResult`. Use `TotalRecords` for display, `BuySellRecords` for calculation. |
| `tests/BlowingCandles.Infrastructure.Tests/JsonlAuditReaderTests.cs` | Update existing tests for new return type. Add test for total count including non-BUY/SELL rows. |
| `tests/BlowingCandles.Infrastructure.Tests/StatsPeriodsHandlerTests.cs` | Add tests for empty file, non-BUY/SELL-only file, and multiple BUYs before multiple SELLs. |

The `AuditReadResult` record should live alongside `JsonlAuditReader` in `src/BlowingCandles.Infrastructure/Audit/` since it is an infrastructure-level read result, not a domain model.

## Edge Cases to Handle

| Scenario | Input | Expected Behavior |
|----------|-------|-------------------|
| Empty audit file | File exists, zero lines | Total records: 0, no sections, exit 0 |
| Only non-BUY/SELL actions | File has WAIT/IGNORE rows only | Total records: N (counts all valid rows), no completed periods, no open positions |
| Mixed actions with malformed rows | Valid WAIT + malformed JSON + valid BUY/SELL | Total records counts valid WAIT and BUY/SELL rows; malformed rows excluded from count |
| Multiple BUYs then multiple SELLs | 3 BUYs then 2 SELLs for same ticker | 2 completed periods (FIFO), 1 open position |

## Test Updates Required

### `JsonlAuditReaderTests`

1. **Update `ReadAll_ParsesBuyAndSellRowsAndSkipsMalformedOrIrrelevantLines`** — Assert `TotalRecords` is 3 (BUY + WAIT + SELL are all valid parsed rows; malformed JSON and bad-timestamp rows are excluded). Assert `BuySellRecords` contains 2 records (unchanged).

2. **Update `ReadAll_ReturnsEmptyWhenFileDoesNotExist`** — Assert `TotalRecords` is 0 and `BuySellRecords` is empty.

3. **Add `ReadAll_CountsAllValidRowsIncludingNonBuySell`** — File with BUY, SELL, WAIT, IGNORE rows. Assert `TotalRecords` counts all four. Assert `BuySellRecords` contains only BUY and SELL.

### `StatsPeriodsHandlerTests`

1. **Update `Handle_PrintsCompletedPeriodsOpenPositionsAndSummary`** — Add a WAIT row to the audit fixture. Assert "Total records" reflects the total (6 instead of 5).

2. **Add `Handle_ReturnsZeroAndPrintsHeaderWhenAuditFileIsEmpty`** — File exists but has zero lines. Assert exit 0, "Total records: 0", no sections.

3. **Add `Handle_ReportsCorrectTotalWhenFileHasOnlyNonBuySellActions`** — File with only WAIT/IGNORE rows. Assert "Total records: N", no completed periods, no open positions.

4. **Add `Handle_PairsMultipleBuysWithMultipleSellsFifo`** — 3 BUYs then 2 SELLs for same ticker. Assert 2 completed periods (first two BUYs paired), 1 open position (third BUY).

### `HoldingPeriodCalculatorTests`

1. **Add `Calculate_HandlesMultipleBuysThenMultipleSellsForSameTicker`** — 3 BUYs then 2 SELLs. Assert FIFO pairing produces 2 completed periods and 1 open position.

2. **Add `Calculate_ReturnsEmptyWhenNoRecords`** — Empty input. Assert both lists empty.
