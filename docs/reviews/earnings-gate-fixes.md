# Earnings Gate — Fix Specification

## Summary of Issues

1. **`IEarningsCalendar.GetNextFutureEarningsDate` uses strict `>` but spec requires `>=` for delta=0.** `EarningsGate` works around this by bypassing the interface method and manually iterating via a private `GetCurrentOrFutureCalendarDate` helper with `>=`. If anyone refactors `EarningsGate` to call the interface method (as the spec prescribes), the delta=0 edge case silently breaks.

2. **`Load()` called per-ticker inside the loop.** Couples `EarningsGate` to the caching behavior of the infrastructure implementation. Should be called once before the loop.

3. **Missing test scenarios.** Three spec-defined scenarios lack explicit test coverage: delta=0 with default block window (#5), earnings 1 minute from now (#6), and calendar lookup exception (#14).

4. **`GetCurrentOrFutureCalendarDate` assumes sorted dates.** Undocumented assumption — correct only because both implementations sort during construction.

## Required Changes

### 1. Fix `IEarningsCalendar.GetNextFutureEarningsDate` boundary semantics

Change the comparison from `>` to `>=` in both implementations so the method includes earnings at the exact current instant.

- `EarningsCalendarFile.GetNextFutureEarningsDate`: change `date > referenceTimeUtc` to `date >= referenceTimeUtc`.
- After this change, `EarningsGate` should call `GetNextFutureEarningsDate` instead of using the private `GetCurrentOrFutureCalendarDate` helper.

### 2. Refactor `EarningsGate.Check` to use the interface method

- Call `_earningsCalendar.Load()` once before the loop. Use it only for `ContainsKey` to distinguish "ticker absent" from "ticker present but expired".
- Replace the private `GetCurrentOrFutureCalendarDate` call with `_earningsCalendar.GetNextFutureEarningsDate(ticker, now)`.
- Remove the private `GetCurrentOrFutureCalendarDate` method entirely.

The per-ticker logic becomes:

```csharp
var calendar = <loaded once before loop>
...
var tickerExistsInCalendar = calendar.ContainsKey(ticker);
DateTimeOffset? earningsDate = tickerExistsInCalendar
    ? _earningsCalendar.GetNextFutureEarningsDate(ticker, now)
    : null;
```

### 3. Add missing test scenarios

Add three new test methods to `EarningsGateTests`:

- **Delta=0 with default block window:** Ticker in calendar with earnings at exactly `Now`, default 48h window. Assert NO_TRADE with reason `EARNINGS_LT_48H`.
- **Earnings 1 minute from now:** Ticker in calendar with earnings at `Now.AddMinutes(1)`, default 48h window. Assert NO_TRADE with reason `EARNINGS_LT_48H`.
- **Calendar lookup exception:** Use `FakeEarningsCalendar` with `lookupException` parameter. Assert WAIT with reason `DATA_ERROR`.

## Affected Modules or Files

| File | Change |
|------|--------|
| `src/BlowingCandles.Infrastructure/Calendar/EarningsCalendarFile.cs` | `>` to `>=` in `GetNextFutureEarningsDate` |
| `src/BlowingCandles.Domain/Services/EarningsGate.cs` | Use interface method, hoist `Load()`, remove private helper |
| `tests/BlowingCandles.Domain.Tests/EarningsGateTests.cs` | Add 3 tests, update `FakeEarningsCalendar` `>=` boundary |

## Edge Cases to Address

- **Delta=0 (earnings at exact current time):** Must return NO_TRADE. Currently works via workaround; after fix, works via correct interface semantics.
- **Calendar lookup throws:** Must return WAIT/DATA_ERROR. Currently untested; the catch-all handler covers it but an explicit test is needed.
- **Sorted-date assumption:** No code change required — both `EarningsCalendarFile` and `FakeEarningsCalendar` sort during construction, and `GetNextFutureEarningsDate` iterates linearly. The assumption holds.

## Test Updates Required

### Update existing fake

- `FakeEarningsCalendar.GetNextFutureEarningsDate`: change `date > referenceTimeUtc` to `date >= referenceTimeUtc` to match the production fix.

### New tests

1. `Check_LocalCalendarDeltaZeroWithDefaultWindow_ReturnsNoTrade` — earnings at `Now`, block_window=48h, assert NO_TRADE/`EARNINGS_LT_48H`.
2. `Check_LocalCalendarEarningsOneMinuteAway_ReturnsNoTrade` — earnings at `Now.AddMinutes(1)`, assert NO_TRADE/`EARNINGS_LT_48H`.
3. `Check_CalendarLookupException_ReturnsWaitWithDataError` — `FakeEarningsCalendar` with `lookupException: new IOException("lookup failed")`, assert WAIT/`DATA_ERROR`.

### Existing tests

All 15 existing tests must continue to pass unchanged. The `>=` boundary change does not affect any existing test because no existing test has an earnings date at exactly `Now` with strict-`>` expectations — the `Check_BlockWindowZero_OnlyBlocksExactCurrentTime` test already expects NO_TRADE for delta=0.
