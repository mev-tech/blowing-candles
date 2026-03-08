# Feature: Technical Scoring

## Feature Name

technical-scoring

## Purpose

Compute SMA50, SMA200, and RSI14 from daily closing prices for each ticker in the watchlist. Score the technical indicators and emit a MarketSignal indicating whether to BUY, SELL, or WAIT. Price data is retrieved via `IMarketDataProvider`. All calculations use simple moving averages (rolling mean), not EMA or Wilder's smoothing. Every exception path defaults to WAIT (fail-safe invariant).

## Inputs

### Watchlist

- A list of ticker strings from config. Each ticker is trimmed and uppercased before processing.

### Clock

- `IClock` — provides the current UTC time (`UtcNow`) used as the end date for price history retrieval.

### Market Data Provider

- `IMarketDataProvider` — retrieves one year of daily OHLCV price data for a ticker up to the clock's current date.

## Outputs

- `IReadOnlyList<MarketSignal>` — one signal per input ticker, in input order. Each contains:
  - `Ticker` (string) — uppercased ticker symbol
  - `Action` (Action enum) — BUY, SELL, or WAIT
  - `Score` (int) — composite score from 0 to 100
  - `Close` (decimal) — most recent closing price
  - `Sma50` (decimal) — 50-day simple moving average
  - `Sma200` (decimal) — 200-day simple moving average
  - `Rsi14` (decimal) — 14-day RSI using simple moving averages
  - `Reason` (string) — reason code explaining the action
  - `Timestamp` (DateTimeOffset) — the clock time at evaluation

## Configuration

| Field | Source | Default | Description |
|-------|--------|---------|-------------|
| `watchlist` | config.yaml | required | List of ticker symbols to score |

No additional configuration fields. Scoring thresholds are hardcoded constants matching the Python implementation.

## Edge Cases

1. **Insufficient price data for SMA200.** Fewer than 200 data points returned. Return WAIT with reason `INSUFFICIENT_DATA`. SMA200 requires 200 closing prices.
2. **Insufficient price data for SMA50.** Fewer than 50 data points. Return WAIT with reason `INSUFFICIENT_DATA`.
3. **Insufficient price data for RSI14.** Fewer than 15 data points (14 deltas + 1 baseline). Return WAIT with reason `INSUFFICIENT_DATA`.
4. **Market data provider returns empty list.** Return WAIT with reason `MARKET_DATA_ERROR`.
5. **Market data provider throws exception.** Return WAIT with reason `MARKET_DATA_ERROR`. The fail-safe invariant catches all exceptions.
6. **Score exactly 80 (BUY threshold).** Score >= 80 triggers BUY. Score of 80 is a BUY.
7. **RSI exactly 40 (oversold boundary).** RSI <= 40 is considered oversold. RSI of 40 qualifies.
8. **RSI exactly 50 (neutral boundary).** RSI <= 50 is considered neutral-to-oversold for scoring. RSI of 50 qualifies.
9. **All price deltas are zero (flat market).** Average gain and average loss are both zero. RSI is undefined — treat as 50 (neutral).
10. **All price deltas are positive.** Average loss is zero. RSI = 100.
11. **All price deltas are negative.** Average gain is zero. RSI = 0.
12. **Empty watchlist.** Return empty list.
13. **Ticker normalization.** Input ` msft ` produces signal for `MSFT`.

## Implementation Notes

### Component to modify

1. **`TechnicalScorer`** (Domain/Services/TechnicalScorer.cs) — replace the current stub implementation with the full scoring logic. The class shell already exists.

### Constructor

- `TechnicalScorer(IMarketDataProvider marketDataProvider)`
- No configuration parameters — all thresholds are constants.

### Score method logic (per ticker)

Processing each ticker follows this sequence:

1. **Normalize ticker** — trim and uppercase.
2. **Retrieve price data:**
   a. Call `IMarketDataProvider.GetDailyPrices(ticker, clock.UtcNow)` to get up to one year of daily OHLCV data.
   b. If data is null, empty, or has fewer than 200 points → WAIT (`INSUFFICIENT_DATA`).
3. **Compute indicators:**
   a. Extract the closing prices as a decimal array, ordered chronologically (oldest first).
   b. **SMA50** = mean of last 50 closes.
   c. **SMA200** = mean of last 200 closes.
   d. **RSI14** = 14-period RSI using simple moving averages:
      - Compute daily price changes (deltas) for the last 14 periods (15 prices).
      - Separate into gains (positive deltas) and losses (absolute value of negative deltas).
      - Average gain = mean of gains over 14 periods (zero if no gains).
      - Average loss = mean of losses over 14 periods (zero if no losses).
      - If average loss == 0 and average gain == 0 → RSI = 50.
      - If average loss == 0 → RSI = 100.
      - RS = average gain / average loss.
      - RSI = 100 - (100 / (1 + RS)).
4. **Compute score (0–100):**
   a. Start with score = 0.
   b. **Close vs SMA50:** Close > SMA50 → +20 points.
   c. **Close vs SMA200:** Close > SMA200 → +20 points.
   d. **SMA50 vs SMA200 (golden cross):** SMA50 > SMA200 → +20 points.
   e. **RSI scoring:**
      - RSI <= 40 (oversold) → +40 points.
      - RSI <= 50 (neutral-low) → +20 points.
      - RSI > 50 → +0 points.
5. **Determine action:**
   a. Score >= 80 → BUY.
   b. Score <= 20 → SELL.
   c. Otherwise → WAIT.
6. **Build reason string** — list the contributing factors (e.g., `ABOVE_SMA50,ABOVE_SMA200,GOLDEN_CROSS,RSI_OVERSOLD`).
7. **Exception handler:** Wrap the entire per-ticker block in try/catch. Any exception → WAIT (`MARKET_DATA_ERROR`).

### SMA calculation

- SMA(N) = sum of last N closing prices / N.
- Uses the last N prices in chronological order. No weighting.

### RSI calculation (critical constraint)

- **Must use simple moving average (rolling mean).** Not exponential moving average, not Wilder's smoothing.
- Uses only the last 14 price deltas (from the last 15 prices).
- This matches the Python implementation exactly.

### Scoring constants

| Constant | Value | Description |
|----------|-------|-------------|
| SMA_PERIOD_SHORT | 50 | Short-term SMA period |
| SMA_PERIOD_LONG | 200 | Long-term SMA period |
| RSI_PERIOD | 14 | RSI calculation period |
| SCORE_SMA_SHORT | 20 | Points for close > SMA50 |
| SCORE_SMA_LONG | 20 | Points for close > SMA200 |
| SCORE_GOLDEN_CROSS | 20 | Points for SMA50 > SMA200 |
| SCORE_RSI_OVERSOLD | 40 | Points for RSI <= 40 |
| SCORE_RSI_NEUTRAL | 20 | Points for 40 < RSI <= 50 |
| BUY_THRESHOLD | 80 | Minimum score for BUY |
| SELL_THRESHOLD | 20 | Maximum score for SELL |
| RSI_OVERSOLD | 40 | RSI oversold boundary |
| RSI_NEUTRAL | 50 | RSI neutral boundary |

### Dependencies

- `IMarketDataProvider` from Domain interfaces (daily price retrieval)
- `IClock` from Phase 1 (time abstraction)
- Domain models: `MarketSignal` from Domain layer
- Domain enums: `Action` from Domain layer

### Key constraints

- RSI uses simple moving average. This is the single most important numerical constraint.
- All calculations use decimal arithmetic to avoid floating-point precision issues.
- Minimum 200 data points required. If fewer are available, the ticker gets WAIT.
- Every exception produces WAIT. No exception may result in BUY or SELL.
- Output order matches input order (not alphabetical).

## Test Scenarios

### BUY signals

1. **Score = 100 (all conditions met).** Close > SMA50, Close > SMA200, SMA50 > SMA200, RSI <= 40. Verify Action=BUY, Score=100.
2. **Score = 80 (BUY threshold exact).** Close > SMA50, Close > SMA200, SMA50 > SMA200, RSI > 50. Score = 60? Adjust: Close > SMA50 (+20), Close > SMA200 (+20), SMA50 > SMA200 (+20), RSI <= 50 (+20) = 80. Verify Action=BUY.
3. **Score = 80 via RSI oversold.** Close > SMA50 (+20), Close > SMA200 (+20), RSI <= 40 (+40) = 80. Verify Action=BUY even without golden cross.

### SELL signals

4. **Score = 0 (all conditions missed).** Close < SMA50, Close < SMA200, SMA50 < SMA200, RSI > 50. Verify Action=SELL, Score=0.
5. **Score = 20 (SELL threshold exact).** Score of exactly 20. Verify Action=SELL.

### WAIT signals

6. **Score = 40 (middle range).** Close > SMA50 (+20), RSI <= 50 (+20) = 40. Verify Action=WAIT.
7. **Score = 60.** Close > SMA50 (+20), Close > SMA200 (+20), SMA50 > SMA200 (+20) = 60. Verify Action=WAIT, not BUY.

### SMA calculations

8. **SMA50 correct value.** Provide 200 known prices. Verify SMA50 = mean of last 50 closes.
9. **SMA200 correct value.** Provide 200 known prices. Verify SMA200 = mean of all 200 closes.

### RSI calculations

10. **RSI with known values.** Provide 15 specific prices with known deltas. Verify RSI matches hand-calculated value using simple moving average.
11. **RSI = 40 exactly (oversold boundary).** Construct prices so RSI = 40. Verify +40 score points.
12. **RSI = 50 exactly (neutral boundary).** Construct prices so RSI = 50. Verify +20 score points (neutral-low tier).
13. **RSI = 50.01 (just above neutral).** Verify +0 score points for RSI.
14. **All gains, no losses.** RSI = 100. Verify computation does not divide by zero.
15. **All losses, no gains.** RSI = 0. Verify computation does not divide by zero.
16. **Flat market (no changes).** RSI = 50 (neutral default). Verify no division by zero.

### Insufficient data

17. **Fewer than 200 prices.** Provider returns 199 prices. Verify Action=WAIT, Reason=`INSUFFICIENT_DATA`.
18. **Empty price list.** Provider returns empty list. Verify Action=WAIT, Reason=`MARKET_DATA_ERROR`.
19. **Null price data.** Provider returns null. Verify Action=WAIT, Reason=`MARKET_DATA_ERROR`.

### Fail-safe

20. **Provider throws exception.** Verify Action=WAIT, Reason=`MARKET_DATA_ERROR`. No exception escapes.
21. **All exceptions produce WAIT.** Never BUY or SELL on error.

### Input handling

22. **Empty watchlist.** Verify empty list returned.
23. **Ticker normalization.** Input ` tsla ` produces signal for `TSLA`.
24. **Multiple tickers.** Verify one MarketSignal per ticker in input order.
