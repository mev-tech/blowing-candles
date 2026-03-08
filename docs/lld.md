# Low-Level Design

> Living document. Updated as each implementation phase lands. Current state reflects through **Phase 8 (Finalization)**. **Phase 9 (Yahoo Finance Adapter)** is deferred until after Phase 10/11 — offline seams and tests are complete; live transport verification requires the refresh worker.

## Class Catalog

### Domain Layer (`BlowingCandles.Domain`)

#### Enums

| Enum | Values | Notes |
|------|--------|-------|
| `NewsState` | `TRADE_OK`, `WAIT`, `NO_TRADE`, `MANAGE`, `EXIT_RECOMMENDED`, `EXIT_NOW` | MANAGE+ values reserved for forward compatibility; never emitted today |
| `Action` | `BUY`, `SELL`, `WAIT`, `IGNORE`, `MANAGE`, `EXIT_RECOMMENDED`, `EXIT_NOW` | Same forward-compat reservation applies |

#### Models

All models are `sealed record` types — immutable value objects with structural equality.

**`NewsSignal`**
| Property | Type | Description |
|----------|------|-------------|
| `Ticker` | `string` | Normalized uppercase ticker |
| `State` | `NewsState` | Earnings gate result |
| `Reason` | `string` | Human-readable explanation |
| `Timestamp` | `DateTimeOffset` | UTC time of evaluation |

**`MarketSignal`**
| Property | Type | Description |
|----------|------|-------------|
| `Ticker` | `string` | Normalized uppercase ticker |
| `TradingAction` | `Action` | Scored action (BUY/SELL/WAIT) |
| `Score` | `int` | Composite technical score |
| `Close` | `decimal` | Latest closing price |
| `Sma50` | `decimal` | 50-day simple moving average |
| `Sma200` | `decimal` | 200-day simple moving average |
| `Rsi14` | `decimal` | 14-day RSI (SMA-based) |
| `Reason` | `string` | Scoring rationale |
| `Timestamp` | `DateTimeOffset` | UTC time of evaluation |

**`FinalSignal`**
| Property | Type | Description |
|----------|------|-------------|
| `Ticker` | `string` | Normalized uppercase ticker |
| `TradingAction` | `Action` | Final governed action |
| `NewsState` | `NewsState` | Pass-through from earnings gate |
| `MarketAction` | `Action` | Pass-through from technical scorer |
| `Reason` | `string` | Combined rationale |
| `Timestamp` | `DateTimeOffset` | UTC time of decision |

**`AuditRecord`**
| Property | Type | Description |
|----------|------|-------------|
| `Ticker` | `string` | Normalized uppercase ticker |
| `TradingAction` | `Action` | BUY or SELL (filtered on read) |
| `Timestamp` | `DateTimeOffset` | UTC time of original decision |

**`HoldingPeriod`**
| Property | Type | Description |
|----------|------|-------------|
| `Ticker` | `string` | Ticker symbol |
| `BuyTimestamp` | `DateTimeOffset` | Entry time |
| `SellTimestamp` | `DateTimeOffset` | Exit time |
| `Duration` | `TimeSpan` | `SellTimestamp - BuyTimestamp` |

**`OpenPosition`**
| Property | Type | Description |
|----------|------|-------------|
| `Ticker` | `string` | Ticker symbol |
| `BuyTimestamp` | `DateTimeOffset` | Entry time (no exit yet) |

**`HoldingPeriodAnalysis`**
| Property | Type | Description |
|----------|------|-------------|
| `CompletedPeriods` | `IReadOnlyList<HoldingPeriod>` | Matched BUY-SELL pairs |
| `OpenPositions` | `IReadOnlyList<OpenPosition>` | Unmatched BUYs |

#### Interfaces

```csharp
interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

```csharp
// Defined alongside the interface:
// record struct PriceBar(DateTimeOffset Timestamp, decimal Open, decimal High,
//                        decimal Low, decimal Close, long Volume)

interface IMarketDataProvider
{
    IReadOnlyList<PriceBar> GetDailyPriceHistory(string ticker, DateOnly asOfDate);
    DateTimeOffset? GetNextEarningsDate(string ticker, DateTimeOffset asOfUtc);
}
```

```csharp
interface IEarningsCalendar
{
    IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> Load();
    DateTimeOffset? GetNextFutureEarningsDate(string ticker, DateTimeOffset referenceTimeUtc);
}
```

#### Domain Services

**`EarningsGate`**

```
Constructor(IEarningsCalendar earningsCalendar, IMarketDataProvider marketDataProvider)
```

| Method | Signature | Description |
|--------|-----------|-------------|
| `Check` | `IReadOnlyList<NewsSignal> Check(IEnumerable<string> watchlist, IClock clock)` | Checks earnings proximity for each ticker. Local calendar first, Yahoo Finance fallback if ticker absent. Returns WAIT on any error. |

Status: **Implemented**.

**`TechnicalScorer`**

```
Constructor(IMarketDataProvider marketDataProvider)
```

| Method | Signature | Description |
|--------|-----------|-------------|
| `Score` | `IReadOnlyList<MarketSignal> Score(IEnumerable<string> watchlist, IClock clock)` | Downloads 1 year of daily prices, computes SMA50, SMA200, RSI14 (all SMA-based), scores and emits a MarketSignal per ticker. Returns WAIT on any error. |

Status: **Implemented**.

**`TradeGovernor`**

```
Constructor()  // no dependencies
```

| Method | Signature | Description |
|--------|-----------|-------------|
| `Decide` | `IReadOnlyList<FinalSignal> Decide(IEnumerable<NewsSignal>, IEnumerable<MarketSignal>, IClock)` | Merges news + market signals. Applies gating priority, max-buys-per-day, cooldown. Processes tickers alphabetically. |

Status: **Implemented**.

**`HoldingPeriodCalculator`**

```
Constructor()  // no dependencies
```

| Method | Signature | Description |
|--------|-----------|-------------|
| `Calculate` | `HoldingPeriodAnalysis Calculate(IEnumerable<AuditRecord> records)` | FIFO-matches BUY-SELL pairs per ticker. Maintains a `Queue<DateTimeOffset>` of pending buys per ticker. Unmatched BUYs become open positions. |

Status: **Implemented**.

---

### Infrastructure Layer (`BlowingCandles.Infrastructure`)

#### Config

**`AppConfig`** — `sealed record` with nested config records.

```
AppConfig
  Watchlist        : string[]          (default [])
  News             : NewsConfig
    LocalEarningsCalendar         : string?
    ResolvedLocalEarningsCalendar : string?   (absolute path, set by loader)
    BlockWindowHours              : int       (default 48)
  Policy           : PolicyConfig
    MaxBuysPerDay    : int              (default int.MaxValue)
    CooldownMinutes  : int              (default 0)
  Output           : OutputConfig
    TextFile         : string           (default "signals.txt")
    JsonFile         : string           (default "signals.json")
  State            : StateConfig
    Path             : string           (default "data/state.json")
  Audit            : AuditConfig
    JsonlPath                : string?
    ResolvedJsonlPath        : string?  (absolute path, set by loader)
```

**`YamlConfigLoader`**

```
Constructor()  // creates YamlDotNet IDeserializer internally
```

| Method | Signature | Description |
|--------|-----------|-------------|
| `Load` | `AppConfig Load(string path)` | Reads YAML, deserializes into `RawAppConfig`, normalizes into `AppConfig`. Resolves relative paths against the config file's directory. |
| `Normalize` | `static AppConfig Normalize(RawAppConfig, string path)` | Validates watchlist exists, resolves file paths to absolute. |

Internals: Uses `UnderscoredNamingConvention` and `IgnoreUnmatchedProperties` for YAML deserialization. Nested `RawAppConfig` record mirrors `AppConfig` with nullable fields.

Status: **Implemented**.

#### Clock

**`SystemClock : IClock`** — Returns `DateTimeOffset.UtcNow`. Used in live mode.

**`FixedClock : IClock`** — Constructor takes `DateTimeOffset utcNow`, converts to UTC, returns it from `UtcNow` property. Used in simulation and tests.

Status: **Implemented**.

#### State

**`JsonStateStore`**

```
Constructor(string path)
```

| Method | Signature | Description |
|--------|-----------|-------------|
| `Load` | `StateSnapshot Load(IClock clock)` | Reads JSON file. Returns `Empty()` on missing/corrupt. Applies `ResetIfNewDay`. |
| `Save` | `void Save(StateSnapshot state)` | Writes JSON with indentation. |
| `ResetIfNewDay` | `StateSnapshot ResetIfNewDay(StateSnapshot state, IClock clock)` | Compares stored `Day` against clock's current date. Resets buy count if different. |
| `RecordBuy` | `StateSnapshot RecordBuy(StateSnapshot state, DateTimeOffset whenUtc)` | Increments `BuysToday`, updates `LastBuyAt`. |

Nested type: `sealed record StateSnapshot`
| Property | JSON key | Type |
|----------|----------|------|
| `Day` | `"day"` | `string` (yyyy-MM-dd) |
| `BuysToday` | `"buys_today"` | `int` |
| `LastBuyAt` | `"last_buy_at"` | `DateTimeOffset?` |

Status: **Implemented**.

#### Audit

**`JsonlAuditWriter`**

```
Constructor(string path)
```

| Method | Signature | Description |
|--------|-----------|-------------|
| `Append` | `void Append(IEnumerable<FinalSignal> signals)` | Appends one JSONL line per signal. Uses prefixed enum format (`"Action.BUY"`, `"NewsState.WAIT"`). Creates directory if needed. |

Status: **Implemented**.

**`JsonlAuditReader`**

```
Constructor(string path)
```

| Method | Signature | Description |
|--------|-----------|-------------|
| `ReadAll` | `AuditReadResult ReadAll()` | Reads all lines, parses each as JSON, filters to BUY/SELL only. Returns empty result if file missing. |
| `TryParseRecord` | `static bool TryParseRecord(string line, out AuditRecord)` | Parses a single JSONL line. Handles both `"Action.BUY"` and `"BUY"` formats. |

Status: **Implemented**.

**`AuditReadResult`** — `sealed record`
| Property | Type | Description |
|----------|------|-------------|
| `TotalRecords` | `int` | Total lines parsed (including non-BUY/SELL) |
| `BuySellRecords` | `IReadOnlyList<AuditRecord>` | Filtered to BUY and SELL only |

#### Calendar

**`EarningsCalendarFile : IEarningsCalendar`**

```
Constructor(string path)
```

| Method | Signature | Description |
|--------|-----------|-------------|
| `Load` | `IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> Load()` | Lazy-loads and caches the JSON calendar. |
| `GetNextFutureEarningsDate` | `DateTimeOffset? GetNextFutureEarningsDate(string ticker, DateTimeOffset referenceTimeUtc)` | Returns first date after reference, or null. |

Internals: `LoadCore()` reads JSON file, `ParseCalendar()` validates root object structure, `ParseDates()` handles both single string and array formats, `TryParseUtc()` handles ISO 8601 with explicit Z suffix.

Status: **Implemented**.

#### Market Data

**`YahooFinanceAdapter : IMarketDataProvider`**

```
Constructor()  // public adapter entry point; may compose internal Yahoo transport or parsing helpers
```

| Method | Signature | Description |
|--------|-----------|-------------|
| `GetDailyPriceHistory` | `IReadOnlyList<PriceBar> GetDailyPriceHistory(string ticker, DateOnly asOfDate)` | Fetches about 365 days of daily OHLCV via a verified Yahoo integration path, clamps invalid future boundaries, normalizes timestamps deliberately, and returns `PriceBar[]` ordered by date. |
| `GetNextEarningsDate` | `DateTimeOffset? GetNextEarningsDate(string ticker, DateTimeOffset asOfUtc)` | Queries Yahoo for the next future earnings date when the local calendar lacks the ticker. Returns null only when Yahoo has no usable date. |

Status: **Deferred** — offline seams and tests are complete; live Yahoo HTTP transport verification is blocked on Phase 10 (refresh worker). Runtime reads will use persisted snapshots.

---

### Application Layer (`BlowingCandles.Application`)

**`SignalPipeline`**

```
Constructor(EarningsGate earningsGate, TechnicalScorer technicalScorer, TradeGovernor tradeGovernor)
```

| Method | Signature | Description |
|--------|-----------|-------------|
| `Run` | `IReadOnlyList<FinalSignal> Run(IEnumerable<string> watchlist, IClock clock)` | Normalizes watchlist (trim, uppercase, distinct), then executes: EarningsGate.Check -> TechnicalScorer.Score -> TradeGovernor.Decide. |

Status: **Implemented**.

**`OutputRenderer`**

```
Constructor()  // no dependencies
```

| Method | Signature | Description |
|--------|-----------|-------------|
| `WriteSignals` | `void WriteSignals(string textFilePath, string jsonFilePath, IEnumerable<FinalSignal> signals)` | Writes both text and JSON output files. |

Text format per line: `{Ticker}: Action.{TradingAction} | NewsState.{NewsState} | Action.{MarketAction} | {Reason}`

JSON format: Array of objects with camelCase keys, plain enum strings (`"BUY"` not `"Action.BUY"`).

Status: **Implemented**.

---

### CLI Layer (`BlowingCandles.Cli`)

**`Program`** — Static entry point. Parses commands manually (no System.CommandLine yet). Routes to handler classes.

| Command | Handler | Key Parameters |
|---------|---------|----------------|
| `check-calendar` | `CheckCalendarHandler` | `configPath` |
| `stats-periods` | `StatsPeriodsHandler` | `configPath` |
| `run-realtime` | `RunRealtimeHandler` | `configPath` |
| `run-asof {date}` | `RunAsOfHandler` | `configPath`, `DateOnly asOfDate` |
| `run-range {start} {end}` | `RunRangeHandler` | `configPath`, `DateOnly startDate`, `DateOnly endDate` |

Default config path: `"config.yaml"` (relative to CWD).

#### Handler Details

**`CheckCalendarHandler(YamlConfigLoader, IClock)`**
- Loads config, instantiates `EarningsCalendarFile`, loads calendar.
- Categorizes each watchlist ticker as OK / EXPIRED / MISSING.
- Exit code: 0 = all valid, 2 = any expired or missing.

**`StatsPeriodsHandler(YamlConfigLoader, IClock, HoldingPeriodCalculator)`**
- Reads audit JSONL via `JsonlAuditReader`, runs `HoldingPeriodCalculator.Calculate`.
- Prints completed periods, open positions, average holding days.

**`RunRealtimeHandler(YamlConfigLoader, OutputRenderer)`**
- Creates pipeline with `SystemClock`, real adapters.
- Writes `signals.txt`, `signals.json`, appends to `logs/decisions.jsonl`.

**`RunAsOfHandler(YamlConfigLoader, OutputRenderer)`**
- Creates pipeline with `FixedClock(asOfDate at midnight UTC)`.
- Writes to `asof_{date}.signals.txt`, `asof_{date}.signals.json`.
- Audit goes to `sim_decisions.jsonl`.

**`RunRangeHandler(YamlConfigLoader, OutputRenderer)`**
- Validates `endDate >= startDate`, loops each day.
- Creates fresh `FixedClock` per day. Accumulates audit in single sim file.
- Output files per day: `asof_{date}.signals.{txt,json}`.

---

## Dependency Wiring

All wiring is manual in handler constructors and `CreatePipeline` factory methods. No DI container.

```
Program.Main
  ├── YamlConfigLoader (shared across handlers)
  ├── OutputRenderer (shared across run-* handlers)
  │
  ├── CheckCalendarHandler
  │     └── EarningsCalendarFile(config.News.ResolvedLocalEarningsCalendar)
  │
  ├── StatsPeriodsHandler
  │     ├── JsonlAuditReader(auditPath)
  │     └── HoldingPeriodCalculator()
  │
  └── Run*Handler.CreatePipeline(config)
        ├── EarningsCalendarFile(calendarPath)
        ├── YahooFinanceAdapter()
        ├── EarningsGate(calendar, yahoo)
        ├── TechnicalScorer(yahoo)
        ├── TradeGovernor()
        └── SignalPipeline(gate, scorer, governor)
```

## Serialization Contracts

### Audit JSONL (written by `JsonlAuditWriter`, read by `JsonlAuditReader`)

```json
{"ticker":"AAPL","action":"Action.BUY","news_state":"NewsState.TRADE_OK","market_action":"Action.BUY","reason":"...","timestamp":"2026-03-07T14:30:00.000000Z"}
```

- Enum values are prefixed: `"Action.BUY"`, `"NewsState.WAIT"`.
- Timestamp: ISO 8601 with microsecond precision and Z suffix.

### signals.json (written by `OutputRenderer`)

```json
[
  {
    "ticker": "AAPL",
    "tradingAction": "BUY",
    "newsState": "TRADE_OK",
    "marketAction": "BUY",
    "reason": "...",
    "timestamp": "2026-03-07T14:30:00.000000Z"
  }
]
```

- Enum values are plain: `"BUY"`, `"TRADE_OK"`.
- Keys are camelCase.

### state.json (managed by `JsonStateStore`)

```json
{
  "day": "2026-03-07",
  "buys_today": 1,
  "last_buy_at": "2026-03-07T14:30:00Z"
}
```

### earnings_calendar.json (read by `EarningsCalendarFile`)

```json
{
  "AAPL": ["2026-04-24T00:00:00Z"],
  "MSFT": "2026-04-22T00:00:00Z"
}
```

- Values can be a single string or an array of strings.
- Dates are ISO 8601 with Z suffix.

## Implementation Status

| Phase | Scope | Status |
|-------|-------|--------|
| 1 — Scaffold + Calendar Validator | Project structure, config loader, calendar reader, clock, `check-calendar` | Done |
| 2 — Audit Analysis | JSONL reader, `HoldingPeriodCalculator`, `stats-periods` | Done |
| 3 — Shared Infrastructure | Full config loader, state store, audit writer, output renderer | Done |
| 4 — Trade Governor | News gating, market pass-through, buy limits, cooldown | Done |
| 5 — Earnings Gate | Proximity checking, Yahoo Finance fallback | Done |
| 6 — Technical Scoring | SMA50, SMA200, RSI14 (SMA-based), composite scoring | Done |
| 7 — Command Orchestration | Wire full pipeline, `run-asof`, `run-realtime`, `run-range` | Done |
| 8 — Finalization | Dockerfile, cross-validation against Python | Done |
| 9 — Yahoo Finance Adapter | Verified live Yahoo transport for the refresh worker | Deferred (blocked on 10/11) |
| 10 — Market Data Persistence | PostgreSQL refresh runs, immutable snapshots, historical quote rows, missing-symbol tracking, and snapshot freshness metadata | Planned |

## Design Decisions Log

| Decision | Rationale |
|----------|-----------|
| All models are `sealed record` | Immutability, structural equality, minimal boilerplate |
| No DI container | Minimal abstraction principle; manual wiring is simple enough for 5 commands |
| `IClock` for all time access | Enables deterministic testing and fixes Python's state-reset bug in simulation |
| FIFO matching in `HoldingPeriodCalculator` | Matches Python behavior for BUY-SELL pairing |
| Lazy-load calendar with caching | Parsed once per run; avoids repeated file I/O |
| Prefixed enums in audit, plain in JSON output | Matches Python output format for behavioral parity |
| State uses `IClock` for day-reset | Accepted divergence from Python (which uses wall-clock even in simulation) |
| Path resolution relative to config file | Allows config to be in any directory without CWD dependency |
| Yahoo integration strategy deferred | Offline adapter seams are complete; live transport verification blocked on the Phase 10 refresh worker |
| Fresh snapshot-backed market data is acceptable for runtime | End-of-day signals only require the latest completed daily close; persisted refresh snapshots reduce provider throttling risk |
| Immutable snapshot persistence is the primary refresh model | Refreshes should write a new snapshot with explicit missing-symbol tracking instead of mutating prior market-data rows in place |
