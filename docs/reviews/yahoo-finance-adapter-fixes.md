# Yahoo Finance Adapter - Required Fixes

## Summary of Issues

1. **Live market data retrieval does not work.** `run-realtime` still emits blanket `WAIT / MARKET_DATA_ERROR`, and a direct probe of the current `YahooFinanceApi` download flow returned `401 Unauthorized`.
2. **Yahoo earnings fallback is missing.** `GetNextEarningsDate` is stubbed to `null`, so the Python behavior for tickers absent from the local calendar is not implemented.
3. **The adapter is not testable at the transport level.** Existing tests only exercise fake or fixture-backed `IMarketDataProvider` implementations, not the real Yahoo integration path.
4. **Request boundary and timestamp handling are underspecified.** The current adapter requests `asOfDate + 1 day` and stamps source candle times as UTC without validating the source semantics.
5. **The documentation is ahead of reality.** Phase 9 is marked complete even though live validation fails.

## Required Changes

### 1. Replace or harden the live historical-price integration path

Keep `YahooFinanceAdapter` as the public `IMarketDataProvider` implementation, but do not treat `YahooFinanceApi` v2.3.3 as locked in. The historical-price path must be replaced or hardened until a live smoke run proves it can retrieve daily price history successfully.

The implementation choice may be:

- a repaired package-based Yahoo path, or
- a direct Yahoo HTTP integration behind internal helper classes.

What matters is verified behavior, not the package name.

### 2. Implement Yahoo earnings fallback

`GetNextEarningsDate` must stop returning unconditional `null`. It should:

- query Yahoo only when the local calendar does not contain the ticker,
- return the next future earnings date when available,
- return `null` only when Yahoo genuinely has no usable earnings data, and
- throw on provider or parsing failure so `EarningsGate` can preserve the fail-safe `WAIT / DATA_ERROR` behavior.

### 3. Introduce internal adapter seams and diagnostics

The adapter needs small internal seam(s) so provider-specific logic can be tested without live network access. The exact class names are not important, but the responsibilities are:

- building Yahoo requests,
- handling auth, headers, cookies, or session setup,
- parsing Yahoo responses into raw records, and
- mapping raw records to domain-facing `PriceBar` or earnings-date values.

Also add enough diagnostics that a future live failure can be distinguished as auth, transport, parsing, or empty-data behavior.

### 4. Fix request boundaries and timestamp normalization

The adapter must explicitly handle:

- future end dates in live mode,
- inclusive or exclusive end-boundary rules expected by the Yahoo endpoint,
- weekend and market-holiday requests, and
- source timestamps that may not already be UTC.

Do not assign `DateTimeOffset(..., TimeSpan.Zero)` unless the source timestamp has been verified to already represent UTC.

### 5. Add adapter-focused tests

Add Infrastructure-level tests covering:

- historical-price mapping and chronological ordering,
- empty-history behavior,
- unauthorized, transport, and parsing failures,
- future-date clamping and weekend boundaries,
- timestamp normalization, and
- earnings fallback success and failure cases.

These tests must run without live network access by exercising the new internal seams.

### 6. Reopen the documentation and phase acceptance criteria

Update the planning docs so Phase 9 is explicitly reopened. Do not mark it complete again until:

- the default solution build succeeds,
- the offline automated suite passes,
- adapter-focused tests pass, and
- a network-enabled smoke verification shows the adapter can fetch live Yahoo data without systematic `MARKET_DATA_ERROR`.

## Affected Modules or Files

| File | Change |
|------|--------|
| `src/BlowingCandles.Infrastructure/MarketData/YahooFinanceAdapter.cs` | Replace or harden live price retrieval, implement earnings fallback, fix boundary and timestamp handling |
| `src/BlowingCandles.Infrastructure/BlowingCandles.Infrastructure.csproj` | Keep or change Yahoo dependencies according to the verified integration path |
| `src/BlowingCandles.Cli/Handlers/RunCommandSupport.cs` | Update dependency wiring if the adapter needs helper composition |
| `tests/BlowingCandles.Infrastructure.Tests/` | Add adapter-focused tests and seam coverage |
| `docs/features/yahoo-finance-adapter.md` | Reopen and extend the feature scope |
| `docs/implementation-order.md` | Reopen Phase 9 and replace false completion claims |
| `docs/architecture.md` | Correct the adapter responsibilities and acceptance state |
| `docs/lld.md` | Mark the adapter as reopened and describe the target behavior |

## Edge Cases to Address

1. **Future live request.** A real-time run on 2026-03-08 must not request an invalid future range from Yahoo.
2. **Weekend or holiday as-of date.** The adapter should still return the last available trading-day close set without treating the day as an error.
3. **Unauthorized or auth-cookie failure.** The adapter must surface a deterministic exception path that becomes `WAIT`, and the tests must prove it.
4. **No data for ticker.** Empty history must remain distinct from transport or parsing failure.
5. **Ticker absent from local calendar.** `EarningsGate` must regain Yahoo fallback parity with Python.
6. **Source timezone mismatch.** Returned candle timestamps must not silently shift the effective last close used by `TechnicalScorer`.

## Test Updates Required

1. Add Infrastructure tests for the adapter's historical-price path using fake transport or parser seams.
2. Add Infrastructure tests for Yahoo earnings fallback behavior and failure handling.
3. Keep the existing Domain, Application, and CrossValidation tests green; they remain valuable contract tests.
4. Add a documented smoke-validation step for the live Yahoo path. This may remain a manual or opt-in check if the normal CI environment has no network access.
5. Re-run `dotnet build BlowingCandles.sln`, `dotnet test BlowingCandles.sln --no-restore`, and an isolated `run-realtime` smoke run with network access.
