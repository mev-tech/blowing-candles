# Feature: Stats Periods

## Summary

Analyze the JSONL audit log to compute BUY-to-SELL holding periods per ticker. Reports each completed round-trip (BUY followed by SELL) with its duration, plus any open positions that have a BUY but no subsequent SELL. Read-only and deterministic — operates entirely on historical audit data.

## Python Reference Behavior

Source: `signals-bot/src/signals_bot/cli/stats_periods.py`

1. Reads the audit JSONL file path from `config.yaml` (`audit.jsonl_path`).
2. Reads each line as a JSON object. Each row has at minimum: `ticker`, `action`, `timestamp`.
3. Actions use prefixed enum strings in the audit log (e.g., `"Action.BUY"`, `"Action.SELL"`). Only `Action.BUY` and `Action.SELL` rows are relevant; all other actions are ignored.
4. Processes rows in file order (chronological). For each ticker, pairs the earliest unmatched BUY with the next SELL to form a completed holding period.
5. Computes the duration of each holding period (SELL timestamp minus BUY timestamp).
6. Reports open positions — tickers that have a BUY with no subsequent SELL.
7. Prints a summary to stdout with holding period statistics.

## Acceptance Criteria

- [ ] Reads JSONL audit file path from `config.yaml` (`audit.jsonl_path`).
- [ ] Parses each JSONL row and extracts `ticker`, `action`, and `timestamp` fields.
- [ ] Correctly pairs BUY rows with subsequent SELL rows per ticker (FIFO order).
- [ ] Computes holding period duration for each completed round-trip.
- [ ] Reports open positions (unmatched BUYs).
- [ ] Prints human-readable summary to stdout.
- [ ] Exit code 0 on success, 1 on error (missing config key, unreadable file).
- [ ] Handles empty audit file gracefully (no periods, no open positions).

## Inputs

| Input | Source | Format | Required |
|-------|--------|--------|----------|
| `config.yaml` | CWD | YAML | Yes |
| Audit log | `audit.jsonl_path` from config | JSONL | Yes |

## Outputs

| Output | Format | Destination |
|--------|--------|-------------|
| Holding period summary | Plain text | stdout |

### stdout format

```
Audit file: logs/decisions.jsonl
Total records: 42

Completed holding periods:
  AAPL: BUY 2026-01-15T14:30:00Z -> SELL 2026-02-10T15:00:00Z (26.0 days)
  MSFT: BUY 2026-01-20T14:00:00Z -> SELL 2026-01-28T16:00:00Z (8.1 days)

Open positions (BUY without SELL):
  NVDA: BUY 2026-03-01T14:30:00Z (open for 6.0 days)

Summary:
  Completed periods: 2
  Average holding period: 17.1 days
  Open positions: 1
```

- Sections with no entries are omitted.
- Tickers within each section appear in the order of their BUY timestamp (earliest first).
- Duration is displayed in days with one decimal place.
- Open position duration is calculated from BUY timestamp to current time (from `IClock`).

### Exit codes

| Condition | Exit Code |
|-----------|-----------|
| Success | 0 |
| Missing `audit.jsonl_path` in config | 1 |
| Audit file does not exist | 0 (empty results) |

## Domain Rules

1. Only `Action.BUY` and `Action.SELL` rows are considered. All other actions are skipped.
2. BUY-SELL pairing is per-ticker, FIFO: the first unmatched BUY for a ticker is paired with the next SELL for that same ticker.
3. A SELL without a preceding BUY for that ticker is ignored (orphan SELL).
4. Multiple BUYs before a SELL: only the first unmatched BUY is paired; remaining BUYs stay in the open queue.
5. Timestamps are parsed as UTC ISO 8601.

## Error Handling

- Missing `audit.jsonl_path` config key: print error message, exit code 1.
- Audit file does not exist: treat as empty (no periods, no open positions), exit code 0.
- Malformed JSONL row: skip the row silently.
- Unparseable timestamp: skip the row silently.

## Dependencies

- `YamlConfigLoader` (from Phase 1) — needs `audit.jsonl_path` field added to `AppConfig`.
- `JsonlAuditReader` (Infrastructure) — already scaffolded, needs to expose parsed records with typed fields.
- `IClock` (from Phase 1) — for calculating open position duration.

## Test Scenarios

| Scenario | Input | Expected Output |
|----------|-------|-----------------|
| Single completed period | One BUY + one SELL for same ticker | One completed period with correct duration |
| Multiple tickers | BUY/SELL pairs for different tickers | Each ticker's period reported separately |
| Open position | BUY without SELL | Reported in open positions section |
| Mixed completed and open | Some tickers completed, some open | Both sections present |
| Empty audit file | No rows | No sections, just header |
| Audit file missing | File does not exist | No sections, just header, exit 0 |
| Orphan SELL | SELL without prior BUY | SELL row ignored |
| Multiple BUYs before SELL | Two BUYs then one SELL | First BUY paired, second remains open |
| Non-BUY/SELL actions | Rows with WAIT, IGNORE actions | Skipped entirely |
| Malformed row | Invalid JSON line | Skipped silently |
| Missing config key | No `audit.jsonl_path` | Error message, exit 1 |

## Implementation Notes

### Components to build

1. **Extend `AppConfig`** — add `audit.jsonl_path` field to the config model.
2. **`AuditRecord`** (Domain model) — typed representation of a relevant audit row: `ticker`, `action`, `timestamp`.
3. **`HoldingPeriod`** (Domain model) — represents a completed BUY-to-SELL period: `ticker`, `buyTimestamp`, `sellTimestamp`, `duration`.
4. **`HoldingPeriodCalculator`** (Domain service) — takes a list of audit records, pairs BUYs with SELLs per ticker, returns completed periods and open positions.
5. **Update `JsonlAuditReader`** — parse rows into `AuditRecord` objects, filtering to only BUY/SELL actions.
6. **`StatsPeriodsHandler`** (CLI) — orchestrates: load config, read audit, compute periods, print output, return exit code.

### Key decisions

- Use `IClock` for open position duration calculation so tests are deterministic.
- The `HoldingPeriodCalculator` is pure domain logic with no I/O dependencies.
- Extend the existing `AppConfig` rather than creating a separate config type.
