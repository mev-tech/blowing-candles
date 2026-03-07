# Feature: Check Calendar

## Feature Name

check-calendar

## Purpose

Validate that every ticker in the configured watchlist has at least one future earnings date in the local earnings calendar file. Reports OK, EXPIRED, and MISSING tickers with human-readable output and a meaningful exit code for CI integration.

## Inputs

- `config.yaml` — provides the watchlist and the path to the earnings calendar file (`news.local_earnings_calendar`).
- `earnings_calendar.json` — a JSON file mapping uppercase ticker symbols to arrays of ISO 8601 date strings (UTC). May also map a ticker to a single date string instead of an array.
- Current UTC time — provided by `IClock` to determine which earnings dates are in the future.

## Outputs

### stdout

```
Now (UTC): 2026-03-07T12:00:00Z
Calendar file: earnings_calendar.json

OK (next earnings found):
  AAPL: 2026-05-07T20:00:00Z
  MSFT: 2026-04-28T20:00:00Z

EXPIRED (no future dates in calendar):
  TSLA: CALENDAR_EXPIRED

MISSING (not present in calendar file):
  GOOG
```

- The `Now (UTC):` line prints the current time from `IClock` in ISO 8601 with `Z` suffix.
- The `Calendar file:` line prints the path as read from config.
- Sections are separated by a blank line.
- Sections with no entries are omitted entirely.
- Tickers within each section appear in watchlist order (not sorted alphabetically).

### Exit codes

| Condition | Exit Code |
|-----------|-----------|
| All tickers OK | 0 |
| Any ticker EXPIRED or MISSING | 2 |
| Missing `news.local_earnings_calendar` in config | 1 |

## Configuration

Only the following `config.yaml` fields are used:

```yaml
watchlist: [AAPL, MSFT, NVDA, TSLA]
news:
  local_earnings_calendar: earnings_calendar.json
```

- `watchlist` — list of ticker symbols. Each is uppercased before use.
- `news.local_earnings_calendar` — relative or absolute path to the earnings calendar JSON file.

No other config sections are required for this command.

## Edge Cases

1. **Calendar file does not exist.** `_load_calendar` returns an empty dictionary. All watchlist tickers are reported as MISSING. Exit code 2.
2. **Calendar file is empty or invalid JSON.** Should be treated as an error. Print an error message and exit with code 1.
3. **Ticker value is a single string instead of an array.** Parse it as a single-element list (Python reference handles this).
4. **Date string is unparseable.** Silently skip that date entry. If all dates for a ticker are unparseable, the ticker is excluded from the calendar (treated as MISSING).
5. **Ticker has dates but all are in the past.** Reported as EXPIRED with reason `CALENDAR_EXPIRED`.
6. **Ticker in calendar has an empty array.** Ticker is excluded from the loaded calendar (treated as MISSING).
7. **Duplicate dates for a ticker.** Deduplicated and sorted before evaluation.
8. **Watchlist is empty.** Print the header lines, no sections, exit code 0.
9. **`news.local_earnings_calendar` key is missing from config.** Print `ERROR: config.yaml -> news.local_earnings_calendar missing` and exit with code 1.
10. **Ticker casing.** Both watchlist tickers and calendar keys are uppercased before comparison.

## Python Reference Behavior

Source: `signals-bot/src/signals_bot/cli/check_calendar.py`

1. Loads `config.yaml` from CWD using `yaml.safe_load`.
2. Reads `watchlist` and uppercases each ticker.
3. Reads `news.local_earnings_calendar` path from config. Exits with code 1 if missing.
4. Loads the calendar JSON file. If the file does not exist, returns an empty dict.
5. For each calendar entry: uppercases the key, parses date strings via `datetime.fromisoformat` (replacing `Z` with `+00:00`), deduplicates and sorts.
6. Gets current UTC time via `datetime.now(timezone.utc)`.
7. Iterates the watchlist in order. For each ticker:
   - If not in calendar: added to `missing` list.
   - If in calendar but no future date: added to `expired` list with reason `CALENDAR_EXPIRED`.
   - Otherwise: added to `ok` list with the next future date formatted as ISO 8601 with `Z` suffix.
8. Prints the header (`Now (UTC):`, `Calendar file:`), then each non-empty section (`OK`, `EXPIRED`, `MISSING`), each followed by a blank line.
9. Exits with code 2 if `expired` or `missing` is non-empty, otherwise exits with code 0.

## Implementation Notes

### Components to build

1. **`YamlConfigLoader`** (Infrastructure) — parse `config.yaml` into a typed config object. For this phase, only `watchlist` and `news.local_earnings_calendar` fields are needed. Use YamlDotNet.
2. **`EarningsCalendarFile`** (Infrastructure) — implements `IEarningsCalendar`. Loads `earnings_calendar.json`, parses dates, deduplicates, sorts. Exposes a method to get the next future earnings date for a ticker given a reference time.
3. **`IClock`** (Domain interface) — provides current UTC time.
4. **`SystemClock`** (Infrastructure) — returns `DateTime.UtcNow`.
5. **`FixedClock`** (Infrastructure) — returns a fixed `DateTime` for testing.
6. **`CheckCalendarHandler`** (CLI) — orchestrates the command: loads config, loads calendar, evaluates each ticker, prints output, returns exit code.
7. **`Program.cs`** (CLI) — entry point that routes the `check-calendar` command to `CheckCalendarHandler`.

### Key decisions

- Use `IClock` instead of `DateTime.UtcNow` directly so tests are deterministic.
- The config loader only needs to support the fields required by this command; it will be extended in later phases.
- Date parsing must handle ISO 8601 strings with `Z` suffix (convert to UTC).
- Output format must match the Python reference exactly (same spacing, same line structure).

## Test Scenarios

1. **All tickers valid.** Every watchlist ticker has a future earnings date. Verify stdout contains only the OK section. Verify exit code 0.
2. **One ticker expired.** One ticker has only past dates. Verify it appears in the EXPIRED section with `CALENDAR_EXPIRED`. Verify exit code 2.
3. **One ticker missing.** One ticker is not in the calendar file. Verify it appears in the MISSING section. Verify exit code 2.
4. **Mixed: OK, EXPIRED, and MISSING.** Verify all three sections appear in correct order with correct tickers. Verify exit code 2.
5. **Calendar file does not exist.** All tickers reported as MISSING. Exit code 2.
6. **Empty watchlist.** Only header lines printed. Exit code 0.
7. **Missing config key.** `news.local_earnings_calendar` absent from config. Verify error message on stdout and exit code 1.
8. **Single string value in calendar.** Ticker maps to a string instead of an array. Verify it is parsed correctly and appears in the OK section if the date is in the future.
9. **Duplicate dates.** Calendar has duplicate entries for a ticker. Verify deduplication (ticker appears once in OK with the correct next date).
10. **Ticker casing mismatch.** Watchlist has `aapl`, calendar has `Aapl`. Verify both are uppercased and matched correctly.
11. **Unparseable date string.** One date in a ticker's array is invalid. Verify it is skipped and the ticker is still evaluated against remaining valid dates.
12. **Deterministic clock.** Use `FixedClock` set to a known time. Verify a date just after the clock is reported as OK, and a date just before is not counted as future.
