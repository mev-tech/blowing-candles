# Fix Specification: shared-infrastructure

## Summary of Issues

1. **Timestamp format mismatch (Medium).** `JsonlAuditWriter` and `OutputRenderer` use `ToString("O")` which produces fractional seconds (`2026-03-07T14:30:00.0000000+00:00`). The feature spec and Python reference expect `2026-03-07T14:30:00+00:00` without fractional seconds. This breaks behavioral parity.

2. **`ResetIfNewDay` visibility (Low).** `JsonStateStore.ResetIfNewDay` is public but is an internal implementation detail only called by `Load`. Exposing it leaks internal logic and widens the API surface unnecessarily.

## Required Changes

### 1. Fix timestamp format in `JsonlAuditWriter`

**File:** `src/BlowingCandles.Infrastructure/Audit/JsonlAuditWriter.cs`

Replace `signal.Timestamp.ToString("O")` with an explicit format string that omits fractional seconds:

```
signal.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:sszzz")
```

This produces `2026-03-07T14:30:00+00:00` matching the spec.

### 2. Fix timestamp format in `OutputRenderer`

**File:** `src/BlowingCandles.Application/OutputRenderer.cs`

Apply the same timestamp format fix in `WriteJson`:

```
signal.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:sszzz")
```

### 3. Make `ResetIfNewDay` private

**File:** `src/BlowingCandles.Infrastructure/State/JsonStateStore.cs`

Change `public StateSnapshot ResetIfNewDay(...)` to `private StateSnapshot ResetIfNewDay(...)`.

## Affected Modules or Files

| File | Change |
|------|--------|
| `src/BlowingCandles.Infrastructure/Audit/JsonlAuditWriter.cs` | Timestamp format |
| `src/BlowingCandles.Application/OutputRenderer.cs` | Timestamp format |
| `src/BlowingCandles.Infrastructure/State/JsonStateStore.cs` | `ResetIfNewDay` visibility |
| `tests/BlowingCandles.Infrastructure.Tests/JsonlAuditWriterTests.cs` | Assert timestamp value |
| `tests/BlowingCandles.Application.Tests/OutputRendererTests.cs` | Assert timestamp value |

## Edge Cases to Address

1. **Timestamps with non-zero fractional seconds.** Verify that truncation (not rounding) is acceptable. The domain only produces whole-second timestamps, so this is safe.
2. **Timestamps with non-UTC offsets.** `ToUniversalTime()` normalizes to UTC before formatting, ensuring consistent `+00:00` suffix regardless of input offset.

## Test Updates Required

### JsonlAuditWriterTests

- **Test 9 (Writes JSONL with prefixed enum strings):** Add assertion that `timestamp` field equals `"2026-03-07T14:30:00+00:00"` (no fractional seconds).

### OutputRendererTests

- **Test 14 (JSON file uses plain enum strings):** Add assertion that `timestamp` field equals `"2026-03-07T14:30:00+00:00"` (no fractional seconds).
