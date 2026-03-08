# Feature: Earnings Gate

## Feature Name

earnings-gate

## Purpose

Check earnings proximity for each ticker in the watchlist and produce a NewsSignal indicating whether trading is safe. Tickers with earnings within the configured block window are blocked from trading (NO_TRADE). Tickers with expired local calendar data or unavailable earnings dates default to WAIT. The local earnings calendar is the primary data source; Yahoo Finance is the fallback for tickers not present in the local calendar. Every exception path defaults to WAIT (fail-safe invariant).

## Inputs

### Watchlist

- A list of ticker strings from config. Each ticker is trimmed and uppercased before processing.

### Clock

- `IClock` — provides the current UTC time (`UtcNow`) used as the reference point for earnings proximity calculations.

### Earnings Calendar

- `IEarningsCalendar` — loads the local earnings calendar and queries the next future earnings date for a ticker relative to a reference time.

### Market Data Provider

- `IMarketDataProvider` — used only for the `GetNextEarningsDate(ticker, asOfUtc)` fallback when a ticker is absent from the local calendar.

### Configuration

- `block_window_hours` (int) — from `news.block_window_hours` in config. Default: 48. The number of hours before an earnings date during which trading is blocked.

## Outputs

- `IReadOnlyList<NewsSignal>` — one signal per input ticker, in input order. Each contains:
  - `Ticker` (string) — uppercased ticker symbol
  - `State` (NewsState enum) — TRADE_OK, NO_TRADE, or WAIT
  - `Reason` (string) — reason code explaining the state
  - `Timestamp` (DateTimeOffset) — the clock time at evaluation

## Configuration

| Field | Source | Default | Description |
|-------|--------|---------|-------------|
| `news.local_earnings_calendar` | config.yaml | `null` | Path to the local earnings calendar JSON file |
| `news.block_window_hours` | config.yaml | `48` | Hours before earnings during which trading is blocked |

## Edge Cases

1. **Ticker in local calendar with future earnings outside block window.** Return TRADE_OK. Earnings exist but are far enough away.
2. **Ticker in local calendar with future earnings inside block window.** Return NO_TRADE with reason `EARNINGS_LT_48H`. The delta from now to earnings is >= 0 and <= block_window_hours.
3. **Ticker in local calendar with earnings at exact block window boundary.** Return NO_TRADE with reason `EARNINGS_LT_48H`. The boundary is inclusive: `0 <= delta <= block_window`.
4. **Ticker in local calendar with earnings at exact current time (delta = 0).** Return NO_TRADE with reason `EARNINGS_LT_48H`. Zero delta is within the block window.
5. **Ticker in local calendar but all earnings dates are in the past.** Return WAIT with reason `CALENDAR_EXPIRED`. The calendar entry exists but has no future dates.
6. **Ticker not in local calendar — Yahoo fallback succeeds with date outside block window.** Return TRADE_OK with reason `EARNINGS_FROM_YF`.
7. **Ticker not in local calendar — Yahoo fallback succeeds with date inside block window.** Return NO_TRADE with reasons `EARNINGS_FROM_YF` and `EARNINGS_LT_48H`.
8. **Ticker not in local calendar — Yahoo fallback returns null.** Return WAIT with reason `DATA_UNAVAILABLE`. No earnings data could be found from any source.
9. **Ticker not in local calendar — Yahoo fallback throws exception.** Return WAIT with reason `DATA_ERROR`. The fail-safe invariant catches all exceptions.
10. **Local calendar throws exception during lookup.** Return WAIT with reason `DATA_ERROR`. The entire per-ticker try/catch catches this.
11. **Empty watchlist.** Return empty list.
12. **Ticker with whitespace or mixed case.** Trimmed and uppercased before processing. Consistent with existing EarningsCalendarFile normalization.
13. **Earnings date from Yahoo has no timezone.** Treat as UTC before comparison.
14. **Block window of 0 hours.** Only earnings at the exact current moment (delta == 0) would trigger NO_TRADE. Practically equivalent to no blocking.
15. **Multiple future earnings dates in calendar.** Use the nearest future date (first date after `now` in the sorted list) for proximity checking.

## Implementation Notes

### Component to modify

1. **`EarningsGate`** (Domain/Services/EarningsGate.cs) — replace the current stub implementation with the full earnings proximity logic. The class shell and constructor already exist with `IEarningsCalendar` and `IMarketDataProvider` dependencies.

### Constructor

- `EarningsGate(IEarningsCalendar earningsCalendar, IMarketDataProvider marketDataProvider, int blockWindowHours = 48)`
- Add `blockWindowHours` parameter. Store as a `TimeSpan` for comparison.

### Check method logic (per ticker)

Processing each ticker follows this sequence:

1. **Normalize ticker** — trim and uppercase (already done in stub).
2. **Try local calendar first:**
   a. Call `IEarningsCalendar.Load()` to check if the ticker exists in the calendar.
   b. Call `IEarningsCalendar.GetNextFutureEarningsDate(ticker, clock.UtcNow)` to get the next earnings date.
   c. If ticker is in the calendar but no future date exists → WAIT (`CALENDAR_EXPIRED`), continue to next ticker.
3. **Yahoo Finance fallback (only if ticker is NOT in local calendar and no earnings date yet):**
   a. Call `IMarketDataProvider.GetNextEarningsDate(ticker, clock.UtcNow)`.
   b. If a date is returned, use it and record reason `EARNINGS_FROM_YF`.
4. **Evaluate proximity:**
   a. If no earnings date from any source → WAIT (`DATA_UNAVAILABLE`).
   b. Compute `delta = earningsDate - now`.
   c. If `0 <= delta <= blockWindow` → NO_TRADE (`EARNINGS_LT_48H`).
   d. Otherwise → TRADE_OK.
5. **Exception handler:** Wrap the entire per-ticker block in try/catch. Any exception → WAIT (`DATA_ERROR`).

### Reason codes

The `Reason` field on `NewsSignal` is a single string. When multiple reasons apply (e.g., Yahoo fallback + within block window), join them with a comma: `"EARNINGS_FROM_YF,EARNINGS_LT_48H"`.

### Dependencies

- `IEarningsCalendar` from Phase 1 (local calendar loading and querying)
- `IMarketDataProvider` from Domain interfaces (Yahoo Finance fallback for earnings dates)
- `IClock` from Phase 1 (time abstraction)
- `AppConfig.NewsConfig` from Phase 3 (block window hours)
- Domain models: `NewsSignal` from Domain layer
- Domain enums: `NewsState` from Domain layer

### Key constraints

- The local calendar is the primary source. Yahoo Finance is only consulted when the ticker is absent from the local calendar.
- If a ticker is present in the local calendar but all dates are past, the result is WAIT (`CALENDAR_EXPIRED`) — Yahoo fallback is NOT consulted.
- Every exception produces WAIT. No exception may result in NO_TRADE or TRADE_OK.
- The block window boundary is inclusive on both ends: `0 <= delta <= blockWindow`.
- Output order matches input order (not alphabetical — alphabetical ordering is the Trade Governor's responsibility).

## Test Scenarios

### Local calendar — TRADE_OK

1. **Earnings far in the future.** Ticker in calendar, next earnings 30 days from now, block_window=48h. Verify State=TRADE_OK, Reason is empty.
2. **Earnings just outside block window.** Ticker in calendar, next earnings 48h + 1 minute from now. Verify State=TRADE_OK.

### Local calendar — NO_TRADE

3. **Earnings within block window.** Ticker in calendar, next earnings 24 hours from now, block_window=48h. Verify State=NO_TRADE, Reason=`EARNINGS_LT_48H`.
4. **Earnings at exact block window boundary.** Ticker in calendar, next earnings exactly 48 hours from now. Verify State=NO_TRADE, Reason=`EARNINGS_LT_48H` (boundary inclusive).
5. **Earnings at exact current time (delta=0).** Ticker in calendar, next earnings equals `clock.UtcNow`. Verify State=NO_TRADE, Reason=`EARNINGS_LT_48H`.
6. **Earnings 1 minute from now.** Verify State=NO_TRADE, Reason=`EARNINGS_LT_48H`.

### Local calendar — WAIT (expired)

7. **All earnings dates in the past.** Ticker in calendar, all dates before now. Verify State=WAIT, Reason=`CALENDAR_EXPIRED`.
8. **Calendar expired does not trigger Yahoo fallback.** Ticker in calendar with past dates only. Verify `IMarketDataProvider.GetNextEarningsDate` is never called.

### Yahoo Finance fallback

9. **Ticker absent from calendar, Yahoo returns date outside block window.** Verify State=TRADE_OK, Reason contains `EARNINGS_FROM_YF`.
10. **Ticker absent from calendar, Yahoo returns date inside block window.** Verify State=NO_TRADE, Reason contains `EARNINGS_FROM_YF` and `EARNINGS_LT_48H`.
11. **Ticker absent from calendar, Yahoo returns null.** Verify State=WAIT, Reason=`DATA_UNAVAILABLE`.
12. **Ticker absent from calendar, Yahoo throws exception.** Verify State=WAIT, Reason=`DATA_ERROR`.

### Fail-safe

13. **Local calendar throws during Load.** Verify State=WAIT, Reason=`DATA_ERROR`. No exception escapes.
14. **Local calendar throws during GetNextFutureEarningsDate.** Verify State=WAIT, Reason=`DATA_ERROR`.
15. **All exceptions produce WAIT.** Never TRADE_OK or NO_TRADE on error.

### Input handling

16. **Empty watchlist.** Verify empty list returned.
17. **Ticker normalization.** Input ` aapl ` produces signal for `AAPL`.
18. **Multiple tickers.** Verify one NewsSignal per ticker in input order.
19. **Multiple future earnings dates.** Ticker has dates at +10 days and +60 days. Verify the nearest future date (+10 days) is used for proximity check.

### Configuration

20. **Custom block window.** Set block_window_hours=24. Earnings 30 hours away → TRADE_OK. Earnings 20 hours away → NO_TRADE.
21. **Block window of 0.** Earnings 1 hour from now → TRADE_OK. Earnings at exact current time → NO_TRADE.
