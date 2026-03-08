# Feature: Trade Governor

## Feature Name

trade-governor

## Purpose

Merge NewsSignals and MarketSignals into FinalSignals by applying gating priority and policy constraints. The Trade Governor is the final decision-maker in the signal pipeline: it decides whether a ticker's market action should be passed through, downgraded to WAIT, or blocked entirely based on news state, buy limits, and cooldown rules. Every error or missing data condition defaults to WAIT (fail-safe invariant).

## Inputs

### NewsSignals

- A list of `NewsSignal` objects, one per ticker. Each contains:
  - `Ticker` (string)
  - `State` (NewsState enum: TRADE_OK, WAIT, NO_TRADE)
  - `Reason` (string)
  - `Timestamp` (DateTimeOffset)

### MarketSignals

- A list of `MarketSignal` objects, one per ticker. Each contains:
  - `Ticker` (string)
  - `Action` (Action enum: BUY, SELL, WAIT)
  - `Score` (int)
  - `Close` (decimal)
  - `Sma50` (decimal)
  - `Sma200` (decimal)
  - `Rsi14` (decimal)
  - `Reason` (string)
  - `Timestamp` (DateTimeOffset)

### State

- `JsonStateStore` providing: `buys_today` (int), `last_buy_at` (DateTimeOffset?).

### Policy Config

- `max_buys_per_day` (int) — from `policy.max_buys_per_day` in config.
- `cooldown_minutes` (int) — from `policy.cooldown_minutes` in config.
- `news_ttl_minutes` (int) — constructor-supplied governor setting controlling when a news signal becomes stale.

### Clock

- `IClock` — provides current UTC time for cooldown comparison.

## Outputs

- A list of `FinalSignal` objects, one per ticker processed in **alphabetical order**. Each contains:
  - `Ticker` (string)
  - `Action` (Action enum)
  - `NewsState` (NewsState enum)
  - `MarketAction` (Action enum — the original market action before gating)
  - `Reason` (string)
  - `Timestamp` (DateTimeOffset)

- Side effect: `JsonStateStore` is updated when a BUY is emitted (increment `buys_today`, set `last_buy_at`).

## Configuration

| Field | Source | Default | Description |
|-------|--------|---------|-------------|
| `policy.max_buys_per_day` | config.yaml | `int.MaxValue` | Maximum BUY signals per day |
| `policy.cooldown_minutes` | config.yaml | `0` | Minimum minutes between BUY signals |
| `news_ttl_minutes` | `TradeGovernor` constructor | `180` | Maximum allowed age of a `NewsSignal` before it is treated as stale |

## Edge Cases

1. **Missing NewsSignal for a ticker.** Emit WAIT with reason `NO_NEWS_STATE`. The ticker has no earnings gate result.
2. **Stale NewsSignal for a ticker.** If `clock.UtcNow - NewsSignal.Timestamp > news_ttl_minutes`, emit WAIT with reason `DATA_STALE`. At the exact TTL boundary, the signal is still valid.
3. **Missing MarketSignal for a ticker.** Emit WAIT with reason `NO_MARKET_DATA`. The ticker has no technical scoring result.
4. **NewsState is NO_TRADE.** Emit WAIT with reason `BLOCKED_BY_NEWS` regardless of market action. NO_TRADE always blocks.
5. **NewsState is WAIT.** Emit WAIT with reason `NEWS_WAIT` regardless of market action. Pending news state blocks trading.
6. **Unsupported NewsState.** Emit WAIT with reason `UNSUPPORTED_NEWS_STATE`. Only `TRADE_OK` may proceed.
7. **Unsupported market action.** Emit WAIT with reason `UNSUPPORTED_MARKET_ACTION`. Only `BUY`, `SELL`, and `WAIT` are valid inputs for this feature.
8. **Duplicate NewsSignal for a ticker.** Emit WAIT with reason `DUPLICATE_NEWS_SIGNAL`. The first encountered signal is retained only to populate preserved output fields deterministically.
9. **Duplicate MarketSignal for a ticker.** Emit WAIT with reason `DUPLICATE_MARKET_SIGNAL`. The first encountered signal is retained only to populate preserved output fields deterministically.
10. **NewsState is TRADE_OK, market action is SELL.** Pass through SELL. SELL is never blocked by policy constraints (buy limits, cooldown).
11. **NewsState is TRADE_OK, market action is WAIT.** Pass through WAIT with the market signal's reason.
12. **NewsState is TRADE_OK, market action is BUY, but max buys reached.** Emit WAIT with reason `MAX_BUYS_REACHED`.
13. **NewsState is TRADE_OK, market action is BUY, but cooldown active.** Emit WAIT with reason `COOLDOWN_ACTIVE`.
14. **NewsState is TRADE_OK, market action is BUY, state save fails.** Emit WAIT with reason `STATE_ERROR`. A BUY is never returned unless the updated state is persisted successfully.
15. **NewsState is TRADE_OK, market action is BUY, within limits.** Emit BUY. Update state (increment `buys_today`, set `last_buy_at`) immediately for that ticker.
16. **Multiple BUYs in one run.** Each successful BUY increments `buys_today`. Processing order is alphabetical, so earlier tickers consume buy slots first.
17. **Cooldown boundary.** If `cooldown_minutes` is 0, cooldown never blocks. If exactly at the cooldown boundary (elapsed == cooldown_minutes), the cooldown has expired and BUY is allowed.
18. **State load failure.** Treat state as empty for the current clock day and continue evaluating the run.
19. **Empty ticker lists.** Return empty FinalSignal list.

## Implementation Notes

### Component to build

1. **`TradeGovernor`** (Domain/Services) — single public method: `Decide(List<NewsSignal> newsSignals, List<MarketSignal> marketSignals, IClock clock) -> List<FinalSignal>`. Constructor inputs supply `max_buys_per_day`, `cooldown_minutes`, optional state store, and `news_ttl_minutes`.

### Gating priority (evaluated in order)

1. **News input validation** — detect duplicate `NewsSignal` entries for the ticker.
   - Duplicate: WAIT (`DUPLICATE_NEWS_SIGNAL`)
2. **News gate** — check NewsSignal for the ticker.
   - Missing: WAIT (`NO_NEWS_STATE`)
   - Stale (`clock.UtcNow - timestamp > news_ttl_minutes`): WAIT (`DATA_STALE`)
   - NO_TRADE: WAIT (`BLOCKED_BY_NEWS`)
   - WAIT: WAIT (`NEWS_WAIT`)
   - Unsupported enum value: WAIT (`UNSUPPORTED_NEWS_STATE`)
   - TRADE_OK: proceed to market action evaluation
3. **Market input validation** — detect duplicate `MarketSignal` entries for the ticker.
   - Duplicate: WAIT (`DUPLICATE_MARKET_SIGNAL`)
4. **Market action pass-through** — check MarketSignal for the ticker.
   - Missing: WAIT (`NO_MARKET_DATA`)
   - SELL: pass through as SELL (no policy checks apply to SELL)
   - WAIT: pass through as WAIT with market reason
   - Unsupported enum value: WAIT (`UNSUPPORTED_MARKET_ACTION`)
   - BUY: proceed to policy checks
5. **Policy: max buys per day** — if `buys_today >= max_buys_per_day`, WAIT (`MAX_BUYS_REACHED`)
6. **Policy: cooldown** — if `last_buy_at` is set and `(clock.UtcNow - last_buy_at).TotalMinutes < cooldown_minutes`, WAIT (`COOLDOWN_ACTIVE`)
7. **Emit BUY** — record buy in state, persist immediately, and pass through BUY
   - Save failure: WAIT (`STATE_ERROR`)

### Dependencies

- `JsonStateStore` from Phase 3 (state persistence)
- `AppConfig.PolicyConfig` from Phase 3 (policy settings)
- `IClock` from Phase 1 (time abstraction)
- Domain models: `NewsSignal`, `MarketSignal`, `FinalSignal` from Domain layer
- Domain enums: `Action`, `NewsState` from Domain layer

### Key constraints

- Tickers are always processed in **alphabetical order**.
- Only `NewsState.TRADE_OK` may proceed past the news gate.
- Only `Action.BUY`, `Action.SELL`, and `Action.WAIT` are treated as valid market actions in this feature.
- SELL is never subject to policy constraints (max buys, cooldown). Only BUY is gated by policy.
- The `FinalSignal.MarketAction` field preserves the original market action even when the final action is downgraded to WAIT.
- The `FinalSignal.NewsState` field preserves the original news state.
- Duplicate ticker inputs never use silent last-write-wins behavior.
- Every error or missing-data path produces WAIT. No exception may result in BUY or SELL.

## Test Scenarios

### News gating

1. **TRADE_OK + BUY = BUY.** NewsState=TRADE_OK, MarketAction=BUY, no policy limits. Verify FinalAction=BUY.
2. **NO_TRADE + BUY = WAIT.** NewsState=NO_TRADE, MarketAction=BUY. Verify FinalAction=WAIT, reason=`BLOCKED_BY_NEWS`.
3. **WAIT + BUY = WAIT.** NewsState=WAIT, MarketAction=BUY. Verify FinalAction=WAIT, reason=`NEWS_WAIT`.
4. **NO_TRADE + SELL = WAIT.** NewsState=NO_TRADE, MarketAction=SELL. Verify FinalAction=WAIT, reason=`BLOCKED_BY_NEWS`.
5. **Missing news signal = WAIT.** No NewsSignal for ticker. Verify FinalAction=WAIT, reason=`NO_NEWS_STATE`.
6. **Stale news signal = WAIT.** News timestamp older than TTL. Verify FinalAction=WAIT, reason=`DATA_STALE`.
7. **Exact stale boundary is allowed.** News timestamp exactly at TTL age. Verify normal gating continues.

### Market action pass-through

8. **TRADE_OK + SELL = SELL.** Verify FinalAction=SELL, no policy checks applied.
9. **TRADE_OK + WAIT = WAIT.** Verify FinalAction=WAIT with market reason preserved.
10. **Missing market signal = WAIT.** No MarketSignal for ticker. Verify FinalAction=WAIT, reason=`NO_MARKET_DATA`.
11. **Unsupported market action = WAIT.** Verify FinalAction=WAIT, reason=`UNSUPPORTED_MARKET_ACTION`.

### Policy: max buys per day

12. **BUY allowed when under limit.** buys_today=0, max_buys_per_day=2. Verify BUY emitted, buys_today becomes 1.
13. **BUY blocked when at limit.** buys_today=2, max_buys_per_day=2. Verify WAIT, reason=`MAX_BUYS_REACHED`.
14. **Multiple BUYs consume slots.** Two tickers both eligible for BUY, max_buys_per_day=1. First ticker (alphabetically) gets BUY, second gets WAIT (`MAX_BUYS_REACHED`).

### Policy: cooldown

15. **BUY allowed when no previous buy.** last_buy_at=null, cooldown_minutes=30. Verify BUY emitted.
16. **BUY blocked during cooldown.** last_buy_at=10 minutes ago, cooldown_minutes=30. Verify WAIT, reason=`COOLDOWN_ACTIVE`.
17. **BUY allowed after cooldown expires.** last_buy_at=31 minutes ago, cooldown_minutes=30. Verify BUY emitted.
18. **BUY allowed at exact cooldown boundary.** last_buy_at=30 minutes ago, cooldown_minutes=30. Verify BUY emitted (elapsed == cooldown means expired).
19. **Cooldown of 0 never blocks.** last_buy_at=1 minute ago, cooldown_minutes=0. Verify BUY emitted.

### State integration

20. **BUY updates state.** Emit BUY. Verify buys_today incremented and last_buy_at set to current clock time.
21. **State load failure falls back to empty state.** Throw during `Load`. Verify no exception escapes and the ticker is evaluated against empty state.
22. **State save failure downgrades BUY to WAIT.** Throw during `Save`. Verify final result is `WAIT` with reason=`STATE_ERROR`.
23. **WAIT does not update state.** Emit WAIT. Verify buys_today unchanged.
24. **SELL does not update state.** Emit SELL. Verify buys_today unchanged.

### Ordering and structure

25. **Tickers processed alphabetically.** Input tickers [TSLA, AAPL, MSFT]. Verify output order is [AAPL, MSFT, TSLA].
26. **FinalSignal preserves original market action.** NewsState=NO_TRADE, MarketAction=BUY. Verify FinalSignal.MarketAction=BUY even though FinalSignal.Action=WAIT.
27. **Unsupported news state = WAIT.** Verify FinalAction=WAIT, reason=`UNSUPPORTED_NEWS_STATE`.
28. **Duplicate news or market signals = WAIT.** Verify deterministic fail-safe reasons (`DUPLICATE_NEWS_SIGNAL`, `DUPLICATE_MARKET_SIGNAL`) and no silent last-write-wins behavior.
29. **Empty input returns empty output.** No tickers. Verify empty list returned.
