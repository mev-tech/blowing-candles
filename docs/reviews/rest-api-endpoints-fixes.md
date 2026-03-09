# REST API Endpoints — Fix Specification

## Summary of Issues

1. **Stale architecture doc.** `docs/architecture.md` "What Not to Build" section still lists "No API server, no HTTP endpoints" despite Phase 12 defining this feature.
2. **Feature spec response schema uses wrong casing.** `docs/features/rest-api-endpoints.md` shows `snake_case` property names (`news_state`, `market_action`) but the implementation produces `camelCase` (`newsState`, `marketAction`) via `JsonSerializerDefaults.Web`. Timestamp format also differs (`Z` suffix vs `+00:00`).
3. **Implicit serializer coupling.** `ApiHost` calls `Results.Json()` without explicit `JsonSerializerOptions`, relying on ASP.NET Core's default matching `SignalsJsonSerializer`'s options by coincidence. A future `ConfigureHttpJsonOptions` call would silently break parity.

## Required Changes

### 1. Update `docs/architecture.md` — "What Not to Build" section

Remove the line:
```
- No API server, no HTTP endpoints
```

### 2. Update `docs/features/rest-api-endpoints.md` — response schema examples

Replace `snake_case` property names with `camelCase` in both response schema blocks:

`GET /api/signals` example:
```json
[
  {
    "ticker": "AAPL",
    "action": "BUY",
    "newsState": "TRADE_OK",
    "marketAction": "BUY",
    "reason": "",
    "timestamp": "2026-03-09T14:30:00+00:00"
  }
]
```

`GET /api/signals/{ticker}` 404 example — no change needed (uses `error` and `ticker`, both already lowercase single-word keys).

### 3. Expose `JsonSerializerOptions` from `SignalsJsonSerializer`

In `src/BlowingCandles.Application/SignalsJsonSerializer.cs`, change `JsonOptions` visibility from `private static readonly` to `internal static readonly` so `ApiHost` can reference it.

### 4. Pass explicit options to `Results.Json()` in `ApiHost`

In `src/BlowingCandles.Api/ApiHost.cs`, pass `SignalsJsonSerializer.JsonOptions` to every `Results.Json()` call:

- `Results.Json(result.Signals)` → `Results.Json(result.Signals, SignalsJsonSerializer.JsonOptions)`
- `Results.Json(signal)` → `Results.Json(signal, SignalsJsonSerializer.JsonOptions)`
- `Results.Json(new { error = "Ticker not found", ticker }, statusCode: ...)` → `Results.Json(new { error = "Ticker not found", ticker }, SignalsJsonSerializer.JsonOptions, statusCode: ...)`
- `Results.Json(new { error }, statusCode: ...)` → `Results.Json(new { error }, SignalsJsonSerializer.JsonOptions, statusCode: ...)`

This requires adding `using BlowingCandles.Application;` if not already present.

## Affected Modules or Files

| File | Change |
|------|--------|
| `docs/architecture.md` | Remove stale "no API" line from "What Not to Build" |
| `docs/features/rest-api-endpoints.md` | Fix response schema property casing and timestamp format |
| `src/BlowingCandles.Application/SignalsJsonSerializer.cs` | Change `JsonOptions` from `private` to `internal` |
| `src/BlowingCandles.Api/ApiHost.cs` | Pass explicit `JsonSerializerOptions` to all `Results.Json()` calls |

## Edge Cases to Address

None. The fixes are documentation corrections and a defensive coupling fix. No new behavioral edge cases are introduced. The existing edge cases (file missing, corrupt, empty array, case-insensitive lookup, concurrent writes) are already correctly handled.

## Test Updates Required

No new tests needed. Existing tests in `SignalsEndpointTests.cs` already validate camelCase output and will continue to pass. The explicit serializer options change is behavior-preserving — it locks in the current behavior rather than changing it.

Run the full `BlowingCandles.Api.Tests` suite after changes to confirm no regressions.
