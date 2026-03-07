# Feature: Shared Infrastructure

## Feature Name

shared-infrastructure

## Purpose

Provide the shared infrastructure components that every remaining pipeline command depends on: JSON-backed state persistence with automatic day-reset, JSONL audit log writing with prefixed enum serialization, and dual-format output rendering (text and JSON). This phase also consolidates the config loader to cover all `config.yaml` sections (policy, output, state, audit) beyond the bootstrap fields used by Phase 1 and Phase 2.

## Inputs

### JsonStateStore

- A JSON file at the path specified by `state.path` in config.
- `IClock` — for determining the current day during day-reset comparison.

```json
{ "day": "2026-03-07", "buys_today": 1, "last_buy_at": "2026-03-07T14:30:00Z" }
```

### JsonlAuditWriter

- A list of `FinalSignal` objects produced by the pipeline.
- The file path specified by `audit.jsonl_path` in config.

### OutputRenderer

- A list of `FinalSignal` objects produced by the pipeline.
- Text file path from `output.text_file` in config.
- JSON file path from `output.json_file` in config.

### AppConfig (consolidated)

- `config.yaml` with all sections:

```yaml
watchlist: [AAPL, MSFT, NVDA, TSLA]
news:
  local_earnings_calendar: earnings_calendar.json
  block_window_hours: 48
policy:
  max_buys_per_day: 99999999
  cooldown_minutes: 0
output:
  text_file: signals.txt
  json_file: signals.json
state:
  path: data/state.json
audit:
  jsonl_path: logs/decisions.jsonl
```

## Outputs

### JsonStateStore

- Reads and writes `state.json`. Fields: `day` (string, `yyyy-MM-dd`), `buys_today` (int), `last_buy_at` (ISO 8601 UTC or null).
- On load: compares `day` against `IClock.UtcNow`. If different day, resets to empty state (buys=0, last_buy_at=null, day=today).
- On corruption or missing file: silently returns empty state.

### JsonlAuditWriter

- Appends one JSONL line per signal. Each line is a JSON object:

```json
{"ticker":"AAPL","action":"Action.BUY","news_state":"NewsState.TRADE_OK","market_action":"Action.BUY","reason":"SCORE_80","timestamp":"2026-03-07T14:30:00+00:00"}
```

- Enum values use **prefixed** format: `Action.BUY`, `NewsState.TRADE_OK`.
- Append-only. Never truncates or overwrites.

### OutputRenderer

**signals.txt** — one line per signal using prefixed enum strings:

```
AAPL: Action.BUY | NewsState.TRADE_OK | Action.BUY | SCORE_80
```

**signals.json** — JSON array using **plain** enum strings:

```json
[
  {
    "ticker": "AAPL",
    "action": "BUY",
    "newsState": "TRADE_OK",
    "marketAction": "BUY",
    "reason": "SCORE_80",
    "timestamp": "2026-03-07T14:30:00+00:00"
  }
]
```

### AppConfig

- Strongly-typed record with sub-records: `NewsConfig`, `PolicyConfig`, `OutputConfig`, `StateConfig`, `AuditConfig`.
- Relative paths resolved against the config file's directory.
- Watchlist tickers uppercased and trimmed.
- Default values: `max_buys_per_day` = `int.MaxValue`, `cooldown_minutes` = 0, `text_file` = `signals.txt`, `json_file` = `signals.json`, `state.path` = `data/state.json`.

## Configuration

All `config.yaml` sections are used by this feature:

| Section | Fields | Default |
|---------|--------|---------|
| `watchlist` | list of ticker strings | `[]` |
| `news.local_earnings_calendar` | path string | `null` |
| `news.block_window_hours` | int | `48` |
| `policy.max_buys_per_day` | int | `int.MaxValue` |
| `policy.cooldown_minutes` | int | `0` |
| `output.text_file` | path string | `signals.txt` |
| `output.json_file` | path string | `signals.json` |
| `state.path` | path string | `data/state.json` |
| `audit.jsonl_path` | path string | `null` |

## Edge Cases

1. **State file does not exist.** Return empty state for today (day=today, buys=0, last_buy_at=null).
2. **State file is corrupt JSON.** Silently return empty state for today. No error logged.
3. **State file has valid JSON but different day.** Reset buys_today to 0 and clear last_buy_at. Set day to today.
4. **State file has valid JSON and same day.** Return as-is.
5. **State file parent directory does not exist.** Create it on save.
6. **Audit file parent directory does not exist.** Create it on append.
7. **Audit writer called twice.** Second call appends; first call's rows are preserved.
8. **OutputRenderer with empty signal list.** Write empty text file and `[]` JSON array.
9. **Output file parent directory does not exist.** Create it before writing.
10. **Config with missing optional sections.** Use defaults. Only `watchlist` is structurally required.
11. **Config with relative paths.** Resolve against the directory containing `config.yaml`.
12. **RecordBuy called.** Increment `buys_today` by 1 and set `last_buy_at` to the buy timestamp.

## Implementation Notes

### Components to build or verify

1. **`AppConfig`** (Infrastructure) — strongly-typed config record with sub-records for news, policy, output, state, audit. Already implemented; verify all fields and defaults match architecture.
2. **`YamlConfigLoader`** (Infrastructure) — parse `config.yaml` into `AppConfig`. Already implemented; verify path resolution, defaults, and normalization.
3. **`JsonStateStore`** (Infrastructure) — read/write `state.json` with day-reset logic via `IClock`. Already implemented; verify day-reset, corruption handling, and `RecordBuy`.
4. **`JsonlAuditWriter`** (Infrastructure) — append JSONL rows with prefixed enum strings. Already implemented; verify enum formatting and append-only behavior.
5. **`OutputRenderer`** (Application) — write `signals.txt` (prefixed enums) and `signals.json` (plain enums). Already implemented; verify formatting contracts.

### Key decisions

- **JSON serializer:** `System.Text.Json` (decided at this phase per implementation-order.md).
- **State reset uses `IClock`:** Simulation mode uses `as_of` date for day-reset, diverging from Python's real-clock behavior. This is an accepted architectural decision.
- **Enum serialization contract:** Audit and text output use `"Action.BUY"` prefix format. JSON signal output uses plain `"BUY"`. This dual format must be tested explicitly.
- **State file isolation:** Live and simulation modes use separate state file paths. The path is configurable via `state.path` in config.

## Test Scenarios

### JsonStateStore

1. **Missing file returns empty state.** Load from a non-existent path. Verify day=today, buys=0, last_buy_at=null.
2. **Corrupt JSON returns empty state.** Write invalid JSON to the file. Load. Verify day=today, buys=0, last_buy_at=null.
3. **Same day preserves state.** Write valid state with today's date. Load. Verify buys_today and last_buy_at are preserved.
4. **Different day resets state.** Write valid state with yesterday's date. Load with today's clock. Verify buys=0, last_buy_at=null, day=today.
5. **RecordBuy increments count.** Start with buys=1. Call RecordBuy. Verify buys=2 and last_buy_at is set.
6. **Save and reload round-trips.** Save a snapshot with buys=2 and a last_buy_at. Reload. Verify all fields match.
7. **Save creates parent directory.** Save to `nested/dir/state.json`. Verify file exists.
8. **Empty file returns empty state.** Write empty string to file. Load. Verify day=today, buys=0, last_buy_at=null.

### JsonlAuditWriter

9. **Writes JSONL with prefixed enum strings.** Append one BUY signal. Read the file. Verify `action` is `"Action.BUY"`, `news_state` is `"NewsState.TRADE_OK"`.
10. **Append twice preserves both batches.** Append batch 1, then batch 2. Read all lines. Verify both batches present.
11. **Creates parent directory.** Append to `logs/audit.jsonl` where `logs/` does not exist. Verify file is created.
12. **Multiple signals produce one line each.** Append 3 signals. Verify 3 JSONL lines.

### OutputRenderer

13. **Text file uses prefixed enum format.** Write signals. Read text file. Verify lines match `TICKER: Action.X | NewsState.Y | Action.Z | REASON` format.
14. **JSON file uses plain enum strings.** Write signals. Parse JSON. Verify `action` is `"BUY"` not `"Action.BUY"`.
15. **Empty signal list writes empty output.** Write empty list. Verify text file is empty and JSON file contains `[]`.
16. **Creates parent directories.** Write to nested paths. Verify files exist.

### YamlConfigLoader (consolidated)

17. **Full config loads all sections.** Load a config with all sections. Verify policy, output, state, audit fields are populated.
18. **Missing optional sections use defaults.** Load a config with only `watchlist`. Verify defaults: max_buys=int.MaxValue, cooldown=0, text_file=signals.txt, json_file=signals.json, state.path=data/state.json.
19. **Relative paths resolved against config directory.** Load config from a subdirectory. Verify output paths are absolute and rooted in that subdirectory.
20. **Watchlist tickers uppercased and trimmed.** Load config with `[" aapl ", "msft"]`. Verify `["AAPL", "MSFT"]`.
