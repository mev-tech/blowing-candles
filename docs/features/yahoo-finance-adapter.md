# Feature: Yahoo Finance Adapter

## Feature Name

yahoo-finance-adapter

## Status

DEFERRED. The adapter seams (request factory, transport, response parser) and offline tests are complete. The next missing step is a Testcontainers-backed mock HTTP contract suite, followed by live Yahoo smoke validation once the future refresh workflow exists. The synchronous CLI pipeline is no longer the primary consumer of the live Yahoo path.

## Purpose

Provide verified live Yahoo-backed market data for the **future refresh workflow** that produces persisted snapshots, and as an earnings-date fallback for tickers absent from the local calendar. This feature is not complete until that workflow can fetch real price history from Yahoo and persist a valid snapshot.

## What Landed

- `YahooFinanceAdapter` implements `IMarketDataProvider` with internal seams: `YahooFinanceRequestFactory`, `IYahooFinanceTransport`, `YahooFinanceResponseParser`.
- Typed exception hierarchy: auth, rate-limit, transport, parsing.
- Boundary clamping for future end dates and live-run cap at `nowUtc`.
- Chronological ordering and deliberate timestamp normalization.
- `GetNextEarningsDate` parses Yahoo `calendarEvents` with raw/fmt/string/date-only handling.
- 12 adapter-focused offline tests covering mapping, ordering, empty results, boundary clamping, earnings parsing, and transport error handling.
- Diagnostic writer plumbed through `EarningsGate` and `TechnicalScorer` for stderr diagnostics.

## Remaining Gaps

- `YahooFinanceHttpTransport` has not been verified against live Yahoo endpoints (last probe returned `401 Unauthorized`).
- No socket-level contract suite currently exercises the real `HttpClient` transport against deterministic Yahoo-shaped responses.
- The refresh workflow that will consume the adapter does not exist yet.
- No network-enabled smoke validation has passed.

## Scope (Remaining)

- Add a Testcontainers-backed mock HTTP server suite to exercise the real `YahooFinanceHttpTransport` against deterministic Yahoo-shaped responses.
- Verify or fix the live HTTP transport so the refresh workflow can fetch real data from Yahoo.
- The adapter may become async if the refresh workflow uses an async pipeline.
- Add a network-enabled smoke-validation step proving the refresh workflow produces a valid snapshot.
- The synchronous CLI pipeline will read from persisted snapshots, not from Yahoo directly.

## Inputs

### Ticker

- A ticker string (for example, `"AAPL"`). The adapter normalizes or forwards it according to the verified Yahoo integration path.

### As-Of Date

- `DateOnly asOfDate` - the requested end date for price-history retrieval.
- The adapter fetches approximately 365 days of daily data ending at the requested as-of date.
- Implementations must clamp provider-specific end boundaries so live runs do not request unsupported future data.

### As-Of Timestamp

- `DateTimeOffset asOfUtc` - the reference time for earnings fallback lookups.
- Used only when the local earnings calendar does not contain the ticker.

## Outputs

### GetDailyPriceHistory

- `IReadOnlyList<PriceBar>` containing daily OHLCV data ordered chronologically (oldest first).
- `PriceBar.Timestamp` must reflect deliberate source-time normalization, not a guessed UTC stamp.
- Empty results are allowed only when the provider legitimately has no data for the ticker or date range.
- On transport, auth, parsing, or provider errors, the adapter throws and the calling domain service converts that to `WAIT / MARKET_DATA_ERROR`.

### GetNextEarningsDate

- Returns the next future earnings date from Yahoo for tickers absent from the local calendar.
- Returns `null` only when Yahoo legitimately has no usable earnings date.
- On transport, auth, parsing, or provider errors, the adapter throws and the calling domain service converts that to `WAIT / DATA_ERROR`.

## Requirements

1. The future refresh workflow must be able to fetch live historical prices and earnings dates from Yahoo in a healthy network environment.
2. Earnings fallback must match the Python behavior: local calendar first; Yahoo only when the ticker is absent locally.
3. Provider-specific HTTP, auth, and parsing behavior must be isolated behind internal seam(s) so failures can be tested deterministically without live network access. *(Done)*
4. The adapter must avoid future end dates and other provider-invalid requests. *(Done)*
5. The adapter must preserve chronological ordering and deliberate timestamp normalization. *(Done)*
6. The fail-safe invariant remains unchanged: all adapter failures must still become `WAIT` in domain services. *(Done)*
7. A Testcontainers-backed contract suite must exercise the real HTTP transport against deterministic Yahoo-shaped responses before live validation is attempted.
8. The phase cannot be marked complete without a network-enabled smoke verification proving the refresh workflow produces a valid snapshot.

## Remaining Implementation Plan

1. Add the mock-container contract suite described in `docs/features/yahoo-finance-testcontainers.md`.
2. Verify or fix `YahooFinanceHttpTransport` against live Yahoo endpoints (auth, headers, cookies).
3. If the refresh workflow uses an async pipeline, add `GetStringAsync` to `IYahooFinanceTransport` and update the adapter accordingly.
4. Run a network-enabled smoke validation proving the refresh workflow persists a valid snapshot with real Yahoo data.
5. Update this document once the verification gate passes.

## Configuration

No user-facing adapter configuration is required beyond existing app config. The integration technology, headers, or transport details are infrastructure concerns.

## Dependencies

- `BlowingCandles.Domain.Interfaces.IMarketDataProvider`
- A verified Yahoo integration path (direct HTTP via `YahooFinanceHttpTransport`)
- Infrastructure tests covering adapter behavior without live network dependency *(Done)*
- Testcontainers-backed mock HTTP contract tests (planned)
- Phase 10 (Market Data Persistence) — the snapshot schema must exist before refresh-path live validation is meaningful
- Phase 11 (PostgreSQL Testcontainers) — Docker-dependent integration test infrastructure is already part of the test suite plan

## Files

- `src/BlowingCandles.Infrastructure/MarketData/YahooFinanceAdapter.cs` - public adapter implementation
- `src/BlowingCandles.Infrastructure/BlowingCandles.Infrastructure.csproj` - market-data dependencies
- `tests/BlowingCandles.Infrastructure.Tests/` - adapter-focused tests
- `docs/features/yahoo-finance-testcontainers.md` - planned mock-container contract suite
- `docs/reviews/yahoo-finance-adapter-fixes.md` - remediation plan

## Validation

- `dotnet build BlowingCandles.sln` succeeds in default configuration. *(Done)*
- Existing Domain, Application, Infrastructure, and CrossValidation tests pass. *(Done)*
- Adapter-focused offline tests cover historical prices, earnings fallback, and failure handling. *(Done — 12 tests)*
- Testcontainers-backed Yahoo contract tests pass without live network access. *(Pending)*
- The refresh workflow produces a valid snapshot with real Yahoo data in a healthy network environment. *(Pending — blocked on the future refresh workflow)*

## Out of Scope

- Retries or background refresh scheduling (refresh workflow responsibility, not adapter).
- Any change to domain scoring rules, gating priority, or policy behavior.
- Non-Yahoo providers unless Yahoo proves unusable and a separate decision explicitly approves a replacement.
- Synchronous CLI-to-Yahoo live retrieval — runtime reads will use persisted snapshots after Phase 10/11.
- Treating the mock-container contract suite as a substitute for live Yahoo validation.
