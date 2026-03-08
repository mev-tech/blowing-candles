# Trade Governor — Required Fixes

## Summary of Issues

1. **Fail-safe invariant is not enforced for state persistence failures.** `TradeGovernor.Decide()` loads and saves state without guarding against exceptions. A failing state store can abort the run instead of degrading the affected BUY path to `WAIT`.

2. **Unsupported enum values are not handled defensively.** The governor assumes any `NewsState` other than `NO_TRADE` or `WAIT` is tradable, and any `Action` other than `SELL` or `WAIT` should flow into the BUY policy path. Forward-compatible enum values (`MANAGE`, `EXIT_RECOMMENDED`, `EXIT_NOW`, `IGNORE`) must not be able to produce BUY behavior accidentally.

3. **The stale-news gate required by architecture/reference docs is missing.** The architecture, implementation order, and Python reference all describe a `DATA_STALE` path, but the C# implementation never evaluates signal age and the feature spec does not currently document it.

4. **The implementation hides ambiguity unnecessarily.** Duplicate ticker inputs are collapsed with `GroupBy(...).Last()`, which silently discards earlier signals. Buy-state mutation logic is also duplicated between the domain service and `JsonStateStore`.

## Required Changes

### 1. Harden `TradeGovernor` to preserve fail-safe behavior

Update `TradeGovernor.Decide()` so failures in state access or policy evaluation do not terminate the run.

- Wrap state load and state save in fail-safe handling.
- If state load fails, treat the state as empty for the current clock day and continue.
- If state save fails after a BUY would otherwise be emitted, downgrade that ticker to `WAIT` with a governor-level failure reason instead of returning `BUY` and leaving state inconsistent.
- Ensure every exception path inside the governor results in `WAIT`, never `BUY` or `SELL`.

Reason codes should stay single-string based to match the current domain model. Introduce one explicit failure code for governor-side operational failures, for example `STATE_ERROR`.

### 2. Make action/state handling explicit and closed

Refactor the decision flow so only explicitly supported values are tradable.

- Only `NewsState.TRADE_OK` may proceed to market evaluation.
- `NewsState.NO_TRADE` remains `WAIT` with `BLOCKED_BY_NEWS` to match the approved feature behavior.
- `NewsState.WAIT` remains `WAIT` with `NEWS_WAIT`.
- Any other `NewsState` value defaults to `WAIT` with a fail-safe reason such as `UNSUPPORTED_NEWS_STATE`.
- Only `Action.BUY`, `Action.SELL`, and `Action.WAIT` are valid market actions for this feature.
- Any other `Action` value defaults to `WAIT` with a fail-safe reason such as `UNSUPPORTED_MARKET_ACTION`.

This should be implemented with explicit branching rather than relying on fall-through behavior.

### 3. Implement the stale-news gate and align the feature doc

Add the missing stale-news behavior required by the architecture/reference docs.

- Introduce a configurable or constructor-supplied news TTL for `TradeGovernor`.
- Before applying the news-state gate, compare `clock.UtcNow` to `NewsSignal.Timestamp`.
- If the news signal is older than the TTL, emit `WAIT` with reason `DATA_STALE`.
- Preserve `FinalSignal.NewsState` and `FinalSignal.MarketAction` on stale outputs the same way other blocked outputs do.

Also update `docs/features/trade-governor.md` so it explicitly documents:

- the stale-news gate,
- the TTL input/configuration if introduced,
- the expected `DATA_STALE` reason,
- the related test scenario.

If the team decides not to keep stale-data logic, then the architecture, python reference, and implementation-order docs must all be updated in the same change set. Do not leave the documents inconsistent.

### 4. Remove silent duplicate-signal resolution

Replace `GroupBy(...).Last()` with behavior that is explicit and testable.

Preferred approach:

- assume upstream services produce at most one signal per ticker,
- detect duplicates during dictionary construction,
- downgrade duplicates to `WAIT` via a fail-safe path or reject them in a deterministic, documented way.

At minimum, do not silently pick the last signal without documenting and testing that rule.

### 5. Eliminate duplicated buy-state mutation logic

There should be a single place that defines how buy state is incremented and timestamped.

Choose one of these approaches:

- keep `RecordBuy` in the domain service and remove the unused infrastructure helper, or
- move buy-state mutation behind the store abstraction and have the governor call only the abstraction.

Do not keep two separate implementations of the same state transition.

## Affected Modules or Files

| File | Change |
|------|--------|
| `src/BlowingCandles.Domain/Services/TradeGovernor.cs` | Add fail-safe handling, explicit enum gating, stale-news evaluation, and duplicate-signal handling. |
| `src/BlowingCandles.Domain/Interfaces/ITradeGovernorStateStore.cs` | Update only if the chosen fail-safe/state-mutation design requires a richer store contract. |
| `src/BlowingCandles.Infrastructure/State/JsonStateStore.cs` | Support the chosen fail-safe behavior and remove duplicated/unused buy-state mutation logic if retained here. |
| `docs/features/trade-governor.md` | Add stale-news behavior and any new governor-side failure reason(s); align edge cases and test matrix with the implementation. |
| `docs/architecture.md` | Update only if the stale-data requirement is intentionally removed rather than implemented. |
| `docs/python-reference.md` | Update only if the stale-data requirement is intentionally removed rather than implemented. |
| `docs/implementation-order.md` | Update only if the stale-data validation requirement is intentionally removed rather than implemented. |
| `tests/BlowingCandles.Domain.Tests/TradeGovernorTests.cs` | Add coverage for stale data, unsupported enum values, state-store failures, and duplicate signals. |
| `tests/BlowingCandles.Infrastructure.Tests/JsonStateStoreTests.cs` | Add failure-mode tests only if store behavior or contract changes. |

## Edge Cases to Address

| Scenario | Input | Expected Behavior |
|----------|-------|-------------------|
| State load failure | Store throws during `Load(clock)` | Governor continues with empty state for the current day; no exception escapes |
| State save failure on BUY | Store throws during `Save(state)` | Final result is `WAIT`, not `BUY`; reason indicates state/persistence failure |
| Unsupported news state | `MANAGE` / `EXIT_RECOMMENDED` / `EXIT_NOW` | `WAIT` with fail-safe reason; no policy checks run |
| Unsupported market action | `IGNORE` / `MANAGE` / `EXIT_RECOMMENDED` / `EXIT_NOW` | `WAIT` with fail-safe reason; no buy-state mutation |
| Stale news | `clock.UtcNow - news.Timestamp > ttl` | `WAIT` with `DATA_STALE` |
| TTL boundary | `clock.UtcNow - news.Timestamp == ttl` | Allowed to continue; stale only when strictly older than TTL |
| Duplicate news signals for one ticker | Two `NewsSignal` instances with same ticker | Deterministic fail-safe result; no silent last-write-wins behavior |
| Duplicate market signals for one ticker | Two `MarketSignal` instances with same ticker | Deterministic fail-safe result; no silent last-write-wins behavior |
| Mixed run with one failing ticker | One ticker hits state/save or duplicate-signal failure, others are valid | Failure remains isolated to the affected ticker; other tickers still produce decisions |

## Test Updates Required

### `TradeGovernorTests`

1. **Add `Decide_StaleNews_ReturnsWaitWithDataStaleReason`**  
   Build a `NewsSignal` older than the governor TTL. Assert `Action.WAIT`, `Reason == "DATA_STALE"`, and preserved `MarketAction`.

2. **Add `Decide_ExactStaleBoundary_IsNotBlocked`**  
   Set `clock.UtcNow - news.Timestamp == ttl`. Assert the signal proceeds through normal gating.

3. **Add `Decide_StateLoadFailure_FallsBackToEmptyState`**  
   Use a fake store that throws on `Load`. Assert no exception escapes and an otherwise-eligible BUY is evaluated against empty state.

4. **Add `Decide_StateSaveFailure_DowngradesBuyToWait`**  
   Use a fake store that throws on `Save`. Assert the final action is `WAIT`, not `BUY`.

5. **Add `Decide_UnsupportedNewsState_FailsSafeToWait`**  
   Use `NewsState.MANAGE` or `EXIT_NOW`. Assert `WAIT` and a fail-safe reason.

6. **Add `Decide_UnsupportedMarketAction_FailsSafeToWait`**  
   Use `Action.IGNORE` or `MANAGE`. Assert `WAIT` and no state update.

7. **Add duplicate-signal tests**  
   One test for duplicate `NewsSignal` entries and one for duplicate `MarketSignal` entries for the same ticker. Assert the chosen deterministic behavior explicitly.

8. **Keep existing coverage for alphabetical ordering, cooldown boundary, max-buy ordering, and multi-BUY state accumulation**  
   These behaviors are already correct and should remain unchanged.

### `JsonStateStoreTests`

1. **Add or update tests only if the store contract changes**  
   Examples: a save wrapper result, a domain-level `RecordBuy` removal, or any new state-store API used by the governor.

2. **If `JsonStateStore.RecordBuy()` is removed**  
   Delete the now-obsolete test that exercises the infrastructure helper directly and move buy-state transition coverage to domain tests.
