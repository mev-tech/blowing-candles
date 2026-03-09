# Fix Specification: API Endpoints DB-Backed Reads

## Summary of Issues

1. **JsonStringEnumConverter missing (critical)** — `SignalsJsonSerializer.JsonOptions` does not include `JsonStringEnumConverter` on the committed branch. An unstaged change adds it but has not been committed. Without this, enum properties on run-related endpoints (`runType`, `status`, `action`, `newsState`, `marketAction`) serialize as integers instead of strings, breaking the JSON contract.

2. **POST endpoint exception handling missing (medium)** — `POST /api/runs/realtime`, `POST /api/runs/asof`, and `POST /api/runs/range` have no try/catch. Infrastructure failures (DB connection loss, config errors) propagate as unhandled 500s. Read endpoints already follow a consistent try/catch pattern returning 503.

3. **Inconsistent signal shape between read and run-detail endpoints (low)** — `GET /api/signals` returns `SignalFileEntry` (all string properties, formatted timestamp). `GET /api/runs/{id}` returns raw `SignalRunReadResult` containing `SignalRunSignalResult` (domain enum properties, `DateTimeOffset` timestamp). The JSON output differs in timestamp format (`"2026-03-09T20:00:00+00:00"` vs `"2026-03-09T20:00:00+00:00"` formatted string) and relies on `JsonStringEnumConverter` for enum rendering rather than explicit `.ToString()`.

## Required Changes

### 1. Commit the JsonStringEnumConverter addition

**File:** `src/BlowingCandles.Application/SignalsJsonSerializer.cs`

Stage and commit the existing unstaged change that adds `JsonStringEnumConverter` to `JsonOptions` via a static constructor. The change adds:

```csharp
using System.Text.Json.Serialization;

static SignalsJsonSerializer()
{
    JsonOptions.Converters.Add(new JsonStringEnumConverter());
}
```

No further code changes needed — the unstaged diff is correct as-is.

### 2. Add try/catch to POST endpoint handlers

**File:** `src/BlowingCandles.Api/ApiHost.cs`

Wrap each POST handler in try/catch matching the read endpoint pattern:

- `POST /api/runs/realtime` — wrap the `executionService.RunRealtime()` call. On exception, log and return 503 with `RunDataUnavailableError`.
- `POST /api/runs/asof` — wrap the `executionService.RunAsOf()` call inside `CreateAsOfRunResponse` (after validation passes). On exception, log and return 503.
- `POST /api/runs/range` — wrap the `executionService.RunRange()` call inside `CreateRangeRunResponse` (after validation passes). On exception, log and return 503.

Each POST handler needs access to `ILogger`. Either:
- (a) Pass `app.Logger` into the handler methods (same pattern as read endpoints), or
- (b) Inject `ILogger<ApiHost>` or resolve from DI in the lambda.

Option (a) is consistent with the existing read endpoint pattern. The POST endpoint lambdas need to be updated to accept the logger, which means they cannot remain inline for `realtime` — extract to a named method like the other POST handlers.

### 3. No changes needed for signal shape inconsistency

The shape difference between `GET /api/signals` (string-mapped `SignalFileEntry`) and `GET /api/runs/{id}` (enum-typed `SignalRunSignalResult`) is acceptable. The `JsonStringEnumConverter` from fix #1 ensures enum values render as strings (`"BUY"`, `"TRADE_OK"`). The only remaining difference is timestamp format — `SignalFileEntry` uses `"yyyy-MM-ddTHH:mm:sszzz"` while `SignalRunSignalResult` uses default `DateTimeOffset` serialization. This is a cosmetic difference that does not break consumers and aligns with the spec's statement that run result responses include their own field set.

## Affected Modules or Files

| File | Change |
|------|--------|
| `src/BlowingCandles.Application/SignalsJsonSerializer.cs` | Commit existing unstaged change (JsonStringEnumConverter) |
| `src/BlowingCandles.Api/ApiHost.cs` | Add try/catch + logging to POST /api/runs/realtime, /api/runs/asof, /api/runs/range |
| `tests/BlowingCandles.Api.Tests/SignalsEndpointTests.cs` | Add tests for POST failure handling and route constraint edge case |

## Edge Cases to Address

### POST endpoint infrastructure failure

When `SignalRunExecutionService` throws due to DB unavailability or other infrastructure error during a POST request, the endpoint should return HTTP 503 with a JSON error body, not an unhandled 500.

**Applies to:** `POST /api/runs/realtime`, `POST /api/runs/asof`, `POST /api/runs/range`

**Expected response:**
```json
HTTP 503
{ "error": "Signal run data is unavailable. Try again." }
```

### Non-numeric run ID route

`GET /api/runs/abc` should return HTTP 404 (or 405/400) — not a 500. The `:long` route constraint handles this, but there is no test verifying the behavior.

## Test Updates Required

### 1. POST endpoint failure tests

**File:** `tests/BlowingCandles.Api.Tests/SignalsEndpointTests.cs`

Add a test (or parameterized theory) that verifies POST endpoints return 503 when the database is unavailable. Use the same pattern as `ReadEndpoints_Return503WhenDatabaseIsUnavailable` — connect to a bogus PostgreSQL address and issue POST requests.

```
[Theory]
[InlineData("/api/runs/realtime", null)]
[InlineData("/api/runs/asof", "{ \"asOfDate\": \"2026-03-01\" }")]
[InlineData("/api/runs/range", "{ \"startDate\": \"2026-03-01\", \"endDate\": \"2026-03-03\" }")]
```

Each should assert HTTP 503 and the standard error body.

### 2. Non-numeric run ID route test

**File:** `tests/BlowingCandles.Api.Tests/SignalsEndpointTests.cs`

Add a test that requests `GET /api/runs/abc` and asserts it does not return 500. Expected: 404 (route does not match `{runId:long}`).

### 3. Enum serialization verification test

**File:** `tests/BlowingCandles.Api.Tests/SignalsEndpointTests.cs`

The existing `PostRealtimeRun_ReturnsAcceptedAndPersistsCompletedRun` test already checks `"Realtime"` and `"Completed"` string values, which implicitly validates enum serialization. No additional test needed — but this test will fail until fix #1 (JsonStringEnumConverter) is committed. Verify it passes after staging the change.
