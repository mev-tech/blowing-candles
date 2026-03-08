# Feature: Market Data Persistence

## Feature Name

market-data-persistence

## Status

PLANNED. This replaces the earlier file-backed `market-data-cache` proposal.

## Summary

Persist market-data refresh output in PostgreSQL as immutable snapshots. Each refresh run records execution metadata, one durable snapshot version, the historical quote rows needed by technical scoring, any symbols the provider failed to return, and snapshot-level freshness metadata. This is an intentional divergence from the Python system's live-download-per-run behavior, but it must preserve the same scoring rules and the same fail-safe default of `WAIT` on missing or stale data.

## Python Reference Behavior

- Python downloads approximately one year of daily OHLCV directly from Yahoo Finance during each signal-generation run.
- Python does not have a refresh worker, a cache, or a database-backed market-data store.
- Technical scoring still depends on daily historical bars, not intraday ticks.
- On any market-data failure, the Python system returns `WAIT` with `MARKET_DATA_ERROR`.
- This feature changes the storage and acquisition path, not the scoring logic or fail-safe behavior.

## Acceptance Criteria

- [ ] The schema defines `market_data_refresh_run`, `market_data_snapshot`, `market_data_snapshot_quote`, and `market_data_snapshot_missing_symbol`.
- [ ] Each refresh run persists execution status separately from the snapshot data it produced.
- [ ] Each snapshot is immutable and stores the full historical quote set needed by technical scoring for that refresh.
- [ ] Snapshot freshness is explicit through TTL metadata stored with the snapshot.
- [ ] Symbols missing from a refresh are tracked explicitly and never silently dropped.
- [ ] The design supports live reads of the newest usable snapshot and historical reads for `run-asof`.
- [ ] Missing, stale, partial, or failed refreshes still result in downstream `WAIT`, never a fabricated BUY or SELL.

## Inputs

| Input | Source | Format | Required |
|-------|--------|--------|----------|
| Watchlist symbols | `config.yaml` / refresh worker | string[] | Yes |
| Requested as-of date | refresh worker | `DateOnly` | Yes |
| Provider price history | Yahoo adapter or future provider | daily OHLCV bars per symbol | Yes |
| Snapshot TTL policy | app settings / worker config | integer seconds or duration | Yes |
| Refresh trigger | scheduler / manual operator / repair path | string or enum | Yes |

## Outputs

| Output | Format | Destination |
|--------|--------|-------------|
| Refresh run metadata | relational row | `market_data_refresh_run` |
| Snapshot metadata | relational row | `market_data_snapshot` |
| Historical quote rows | relational rows | `market_data_snapshot_quote` |
| Missing symbol records | relational rows | `market_data_snapshot_missing_symbol` |

## Domain Rules

1. Technical scoring semantics do not change. The persisted data must still support the existing SMA50, SMA200, and RSI14 calculations.
2. A snapshot is immutable. Do not update quote rows in place after a snapshot is committed.
3. A refresh run records execution state even when no usable snapshot is produced.
4. A snapshot stores the complete historical dataset fetched for that refresh, not just the latest close.
5. A symbol must appear in exactly one of these sets for a given snapshot: persisted quote rows or missing-symbol rows.
6. Snapshot TTL is evaluated at the snapshot level. All quote rows inside a snapshot inherit the same freshness window.
7. Missing or stale data remains fail-safe. Any symbol not backed by usable persisted data must resolve to `WAIT`.

## 1. Schema Design

### Design Choice

Use immutable, point-in-time snapshots instead of an in-place mutable cache table.

Why:

- It matches the refresh-worker mental model: each worker execution produces one durable version of the market-data set.
- It preserves replayability for `run-asof` and operational debugging.
- It avoids partial in-place mutation when a refresh fails halfway through.
- It makes TTL and completeness explicit on the snapshot rather than rediscovering freshness from individual rows.

### Relationship Model

- `market_data_refresh_run` -> one `market_data_snapshot` in Phase 1
- `market_data_snapshot` -> many `market_data_snapshot_quote`
- `market_data_snapshot` -> many `market_data_snapshot_missing_symbol`

Phase 1 should enforce one snapshot per run. Keeping separate run and snapshot tables still matters because execution metadata and durable data versioning are different concerns.

### Table: `market_data_refresh_run`

| Column | Type | Notes |
|--------|------|-------|
| `id` | `bigint` | Primary key, identity |
| `requested_as_of_date` | `date` | Date the worker intended the refresh to serve |
| `started_at_utc` | `timestamptz` | Worker start time |
| `completed_at_utc` | `timestamptz null` | Set on success, partial success, or failure |
| `trigger` | `text` | Recommended values: `scheduled`, `manual`, `repair` |
| `provider` | `text` | For now likely `yahoo-finance` |
| `status` | `text` | Recommended values: `Running`, `Succeeded`, `Partial`, `Failed` |
| `requested_symbol_count` | `integer` | Number of symbols requested from the provider |
| `persisted_symbol_count` | `integer` | Number of symbols with at least one persisted quote row |
| `missing_symbol_count` | `integer` | Number of symbols recorded as missing |
| `error_code` | `text null` | Short operational code such as `transport_error` |
| `error_message` | `text null` | Truncated diagnostic detail |

Purpose:

- Operational visibility
- retry/audit history
- failure tracking even when no snapshot was written

### Table: `market_data_snapshot`

| Column | Type | Notes |
|--------|------|-------|
| `id` | `bigint` | Primary key, identity |
| `refresh_run_id` | `bigint` | Foreign key to `market_data_refresh_run(id)` |
| `as_of_date` | `date` | Date this snapshot should satisfy for runtime/history reads |
| `captured_at_utc` | `timestamptz` | When the snapshot was committed |
| `freshness_ttl_seconds` | `integer` | Snapshot TTL in seconds |
| `fresh_until_utc` | `timestamptz` | `captured_at_utc + TTL` |
| `status` | `text` | Recommended values: `Complete`, `Partial` |
| `first_quote_date` | `date` | Earliest historical quote date in the snapshot |
| `last_quote_date` | `date` | Latest historical quote date in the snapshot |
| `quote_row_count` | `integer` | Total number of rows in `market_data_snapshot_quote` |
| `covered_symbol_count` | `integer` | Distinct symbols with persisted quotes |
| `missing_symbol_count` | `integer` | Mirrors child-row count for fast inspection |

Purpose:

- the durable read model for runtime and simulation
- freshness boundary for live use
- completeness summary without scanning quote rows

### Table: `market_data_snapshot_quote`

| Column | Type | Notes |
|--------|------|-------|
| `id` | `bigint` | Primary key, identity |
| `snapshot_id` | `bigint` | Foreign key to `market_data_snapshot(id)` |
| `symbol` | `varchar(16)` | Normalized uppercase ticker |
| `quote_date` | `date` | Trading date for this daily bar |
| `market_timestamp_utc` | `timestamptz` | Provider timestamp for the bar |
| `open` | `numeric(18,8)` | Decimal, not floating-point |
| `high` | `numeric(18,8)` | Decimal, not floating-point |
| `low` | `numeric(18,8)` | Decimal, not floating-point |
| `close` | `numeric(18,8)` | Decimal, not floating-point |
| `volume` | `bigint` | Daily volume |

Important note:

- One snapshot contains many quote rows per symbol.
- The uniqueness rule is `(snapshot_id, symbol, quote_date)`.
- This table stores the historical bars required by `TechnicalScorer`, not just a single latest quote.

### Table: `market_data_snapshot_missing_symbol`

| Column | Type | Notes |
|--------|------|-------|
| `id` | `bigint` | Primary key, identity |
| `snapshot_id` | `bigint` | Foreign key to `market_data_snapshot(id)` |
| `symbol` | `varchar(16)` | Normalized uppercase ticker |
| `reason` | `text` | Recommended values: `NotReturnedByProvider`, `EmptySeries`, `InvalidSymbol`, `ProviderError`, `ParseError` |
| `detail` | `text null` | Optional truncated detail for diagnostics |
| `is_retryable` | `boolean` | Whether a repair worker may retry this symbol |

Purpose:

- make incomplete refreshes explicit
- prevent silent data gaps
- support repair workflows without inventing data

### Integrity Rules

- `market_data_snapshot.refresh_run_id` should be unique in Phase 1.
- `market_data_snapshot_quote` should have a unique constraint on `(snapshot_id, symbol, quote_date)`.
- `market_data_snapshot_missing_symbol` should have a unique constraint on `(snapshot_id, symbol)`.
- Add check constraints for non-negative counts and `freshness_ttl_seconds > 0`.
- Use `DeleteBehavior.Cascade` from snapshot to quote and missing-symbol rows.
- Use `DeleteBehavior.Restrict` from refresh run to snapshot so operational history is not deleted accidentally.

## 2. EF Core Entities

Recommended location:

- `src/BlowingCandles.Infrastructure/Persistence/Entities`
- `src/BlowingCandles.Infrastructure/Persistence/Configurations`

Recommended entities:

```csharp
namespace BlowingCandles.Infrastructure.Persistence.Entities;

public enum MarketDataRefreshRunStatus
{
    Running,
    Succeeded,
    Partial,
    Failed
}

public enum MarketDataSnapshotStatus
{
    Complete,
    Partial
}

public enum MarketDataMissingSymbolReason
{
    NotReturnedByProvider,
    EmptySeries,
    InvalidSymbol,
    ProviderError,
    ParseError
}

public sealed class MarketDataRefreshRunEntity
{
    public long Id { get; set; }
    public DateOnly RequestedAsOfDate { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string Trigger { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public MarketDataRefreshRunStatus Status { get; set; }
    public int RequestedSymbolCount { get; set; }
    public int PersistedSymbolCount { get; set; }
    public int MissingSymbolCount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public MarketDataSnapshotEntity? Snapshot { get; set; }
}

public sealed class MarketDataSnapshotEntity
{
    public long Id { get; set; }
    public long RefreshRunId { get; set; }
    public DateOnly AsOfDate { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public int FreshnessTtlSeconds { get; set; }
    public DateTimeOffset FreshUntilUtc { get; set; }
    public MarketDataSnapshotStatus Status { get; set; }
    public DateOnly FirstQuoteDate { get; set; }
    public DateOnly LastQuoteDate { get; set; }
    public int QuoteRowCount { get; set; }
    public int CoveredSymbolCount { get; set; }
    public int MissingSymbolCount { get; set; }

    public MarketDataRefreshRunEntity RefreshRun { get; set; } = null!;
    public List<MarketDataSnapshotQuoteEntity> Quotes { get; } = [];
    public List<MarketDataSnapshotMissingSymbolEntity> MissingSymbols { get; } = [];
}

public sealed class MarketDataSnapshotQuoteEntity
{
    public long Id { get; set; }
    public long SnapshotId { get; set; }
    public string Symbol { get; set; } = null!;
    public DateOnly QuoteDate { get; set; }
    public DateTimeOffset MarketTimestampUtc { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }

    public MarketDataSnapshotEntity Snapshot { get; set; } = null!;
}

public sealed class MarketDataSnapshotMissingSymbolEntity
{
    public long Id { get; set; }
    public long SnapshotId { get; set; }
    public string Symbol { get; set; } = null!;
    public MarketDataMissingSymbolReason Reason { get; set; }
    public string? Detail { get; set; }
    public bool IsRetryable { get; set; }

    public MarketDataSnapshotEntity Snapshot { get; set; } = null!;
}
```

Entity guidance:

- Keep these entities in Infrastructure, not Domain.
- Map enums as strings with `HasConversion<string>()`.
- Use `DateOnly` for trading dates and `DateTimeOffset` for UTC timestamps.
- Use `decimal` for prices to avoid floating-point drift around SMA/RSI thresholds.

## 3. DbContext Configuration

This feature should be the point where the persistence layer commits to explicit snake_case table names, because the required table names are already snake_case.

Recommended `AppDbContext` additions:

```csharp
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<MarketDataRefreshRunEntity> MarketDataRefreshRuns => Set<MarketDataRefreshRunEntity>();
    public DbSet<MarketDataSnapshotEntity> MarketDataSnapshots => Set<MarketDataSnapshotEntity>();
    public DbSet<MarketDataSnapshotQuoteEntity> MarketDataSnapshotQuotes => Set<MarketDataSnapshotQuoteEntity>();
    public DbSet<MarketDataSnapshotMissingSymbolEntity> MarketDataSnapshotMissingSymbols => Set<MarketDataSnapshotMissingSymbolEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
```

Recommended configuration shape:

```csharp
public sealed class MarketDataRefreshRunConfiguration
    : IEntityTypeConfiguration<MarketDataRefreshRunEntity>
{
    public void Configure(EntityTypeBuilder<MarketDataRefreshRunEntity> builder)
    {
        builder.ToTable("market_data_refresh_run");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Trigger).HasMaxLength(32);
        builder.Property(x => x.Provider).HasMaxLength(64);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);

        builder.HasOne(x => x.Snapshot)
            .WithOne(x => x.RefreshRun)
            .HasForeignKey<MarketDataSnapshotEntity>(x => x.RefreshRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.Status, x.StartedAtUtc });
        builder.HasIndex(x => new { x.RequestedAsOfDate, x.StartedAtUtc });
    }
}

public sealed class MarketDataSnapshotConfiguration
    : IEntityTypeConfiguration<MarketDataSnapshotEntity>
{
    public void Configure(EntityTypeBuilder<MarketDataSnapshotEntity> builder)
    {
        builder.ToTable("market_data_snapshot");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(x => x.RefreshRunId).IsUnique();
        builder.HasIndex(x => new { x.AsOfDate, x.CapturedAtUtc });
        builder.HasIndex(x => x.FreshUntilUtc);
    }
}

public sealed class MarketDataSnapshotQuoteConfiguration
    : IEntityTypeConfiguration<MarketDataSnapshotQuoteEntity>
{
    public void Configure(EntityTypeBuilder<MarketDataSnapshotQuoteEntity> builder)
    {
        builder.ToTable("market_data_snapshot_quote");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Symbol).HasMaxLength(16);
        builder.Property(x => x.Open).HasPrecision(18, 8);
        builder.Property(x => x.High).HasPrecision(18, 8);
        builder.Property(x => x.Low).HasPrecision(18, 8);
        builder.Property(x => x.Close).HasPrecision(18, 8);

        builder.HasOne(x => x.Snapshot)
            .WithMany(x => x.Quotes)
            .HasForeignKey(x => x.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.SnapshotId, x.Symbol, x.QuoteDate }).IsUnique();
        builder.HasIndex(x => new { x.Symbol, x.QuoteDate });
    }
}

public sealed class MarketDataSnapshotMissingSymbolConfiguration
    : IEntityTypeConfiguration<MarketDataSnapshotMissingSymbolEntity>
{
    public void Configure(EntityTypeBuilder<MarketDataSnapshotMissingSymbolEntity> builder)
    {
        builder.ToTable("market_data_snapshot_missing_symbol");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Symbol).HasMaxLength(16);
        builder.Property(x => x.Reason).HasConversion<string>().HasMaxLength(32);

        builder.HasOne(x => x.Snapshot)
            .WithMany(x => x.MissingSymbols)
            .HasForeignKey(x => x.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.SnapshotId, x.Symbol }).IsUnique();
    }
}
```

Configuration guidance:

- Prefer Fluent configuration over data annotations.
- Explicitly map table names to the required snake_case names.
- If column naming is kept explicit rather than convention-based, map every property to snake_case in the configuration classes.
- Do not put persistence attributes on Domain records.

## 4. Recommended Indexes

| Table | Index | Why |
|------|-------|-----|
| `market_data_refresh_run` | `(status, started_at_utc desc)` | operational monitoring for active/failed runs |
| `market_data_refresh_run` | `(requested_as_of_date desc, started_at_utc desc)` | find the latest run for a target date |
| `market_data_snapshot` | unique `(refresh_run_id)` | enforce one snapshot per run in Phase 1 |
| `market_data_snapshot` | `(as_of_date desc, captured_at_utc desc)` | select the newest snapshot usable for live or as-of reads |
| `market_data_snapshot` | `(fresh_until_utc)` | freshness checks and cleanup jobs |
| `market_data_snapshot_quote` | unique `(snapshot_id, symbol, quote_date)` | prevent duplicate bars inside a snapshot |
| `market_data_snapshot_quote` | `(symbol, quote_date desc)` | optional read-side diagnostics and cross-snapshot investigations |
| `market_data_snapshot_missing_symbol` | unique `(snapshot_id, symbol)` | prevent duplicate missing-symbol entries |

Recommended live-read query shape:

```sql
select id
from market_data_snapshot
where as_of_date <= @requestedAsOfDate
  and status in ('Complete', 'Partial')
order by as_of_date desc, captured_at_utc desc
limit 1;
```

For live execution, the caller should also require `fresh_until_utc > now()`. For historical simulation, prefer `as_of_date` ordering over wall-clock freshness.

## 5. Migration Strategy

1. Land or reuse the PostgreSQL foundation from `docs/features/postgres-persistence.md`.
2. Add the entities and configurations under `Infrastructure/Persistence`.
3. Add an additive migration such as `AddMarketDataPersistence`.
4. Apply the migration to create the four tables, foreign keys, uniqueness constraints, and indexes.
5. Do not attempt a data backfill in the migration itself.

Recommended command:

```bash
dotnet ef migrations add AddMarketDataPersistence \
  --project src/BlowingCandles.Infrastructure/BlowingCandles.Infrastructure.csproj \
  --startup-project src/BlowingCandles.Cli/BlowingCandles.Cli.csproj \
  --output-dir Persistence/Migrations
```

Migration notes:

- If the baseline persistence migration has not shipped yet, fold these tables into the first real schema migration rather than creating extra churn.
- Keep enum storage as strings in the first cut. Native PostgreSQL enum types add migration coupling that is not yet justified.
- If a temporary file-backed cache already exists, import it with a one-off application command or worker path, not with a schema migration.

## 6. Data Lifecycle Considerations

- Snapshots are append-only. A new refresh creates a new snapshot instead of overwriting old rows.
- Delete old snapshots with hard deletes. Quote and missing-symbol rows should disappear via cascade delete from the snapshot.
- Keep refresh-run rows longer than snapshot rows so operators can still inspect failures after old quote data is pruned.
- Because each snapshot stores a full historical window, row volume grows linearly with `symbols x bars x snapshots`. With the current small watchlist this is acceptable, but retention must be explicit.
- Reasonable initial retention: keep the newest successful snapshot always, keep a short rolling window of successful snapshots for replay/debug, and keep recent failed/partial runs longer than the quote payloads they attempted to produce.
- Historical reads for `run-asof` should choose the latest snapshot whose `as_of_date` is not newer than the requested simulation date. They should not reject a historical snapshot only because its old TTL has elapsed in wall-clock time.
- Live reads should reject expired snapshots and fall back to `WAIT` for symbols that are missing or stale.

## 7. How the Refresh Worker Should Persist Snapshots

Recommended flow:

1. Normalize the requested watchlist to uppercase distinct symbols.
2. Create a `market_data_refresh_run` row with `status = Running`, counts based on the requested watchlist, and `requested_as_of_date`.
3. Fetch and normalize provider data in memory first.
4. Build two collections:
   - quote rows grouped by symbol and `quote_date`
   - missing-symbol rows for any requested symbol that returned no usable history
5. If no usable quotes exist at all, update the run to `Failed`, set `completed_at_utc`, capture `error_code`/`error_message`, and stop. Do not create an empty snapshot.
6. Start a database transaction.
7. Insert one `market_data_snapshot` row with `as_of_date`, `captured_at_utc`, TTL fields, and summary counts.
8. Bulk insert all `market_data_snapshot_quote` rows for that snapshot.
9. Bulk insert all `market_data_snapshot_missing_symbol` rows for that snapshot.
10. Update the run to `Succeeded` when there are no missing symbols, otherwise `Partial`.
11. Set `completed_at_utc` on the run and commit the transaction.
12. If the transaction fails after the run row exists, update the run to `Failed` in a best-effort follow-up save.

Persistence rules:

- Never mutate the previous snapshot in place.
- Never mark a symbol as both persisted and missing in the same snapshot.
- Deduplicate provider bars before insert so a symbol has at most one row per `quote_date` in a snapshot.
- Truncate `error_message` and `detail` fields to operationally useful sizes rather than storing arbitrarily large provider payload fragments.
- Use downstream fail-safe handling for partial snapshots: symbols present in the snapshot may still be read, while missing symbols must resolve to `WAIT`.

Illustrative worker pseudocode:

```csharp
var run = new MarketDataRefreshRunEntity
{
    RequestedAsOfDate = asOfDate,
    StartedAtUtc = clock.UtcNow,
    Trigger = trigger,
    Provider = "yahoo-finance",
    Status = MarketDataRefreshRunStatus.Running,
    RequestedSymbolCount = symbols.Count
};

db.MarketDataRefreshRuns.Add(run);
await db.SaveChangesAsync(ct);

var result = await provider.RefreshAsync(symbols, asOfDate, ct);
if (result.Quotes.Count == 0)
{
    run.Status = MarketDataRefreshRunStatus.Failed;
    run.CompletedAtUtc = clock.UtcNow;
    run.ErrorCode = result.ErrorCode;
    run.ErrorMessage = result.ErrorMessage;
    await db.SaveChangesAsync(ct);
    return;
}

await using var tx = await db.Database.BeginTransactionAsync(ct);

var snapshot = BuildSnapshot(run.Id, result, clock.UtcNow, ttlSeconds);
db.MarketDataSnapshots.Add(snapshot);
db.MarketDataSnapshotQuotes.AddRange(snapshot.Quotes);
db.MarketDataSnapshotMissingSymbols.AddRange(snapshot.MissingSymbols);

run.Status = snapshot.MissingSymbolCount == 0
    ? MarketDataRefreshRunStatus.Succeeded
    : MarketDataRefreshRunStatus.Partial;
run.PersistedSymbolCount = snapshot.CoveredSymbolCount;
run.MissingSymbolCount = snapshot.MissingSymbolCount;
run.CompletedAtUtc = clock.UtcNow;

await db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
```

## Error Handling

- Provider-wide failure: record a failed refresh run, write no snapshot, and keep downstream behavior fail-safe.
- Partial provider failure: write a partial snapshot plus missing-symbol rows, then let downstream logic emit `WAIT` for missing symbols.
- Invalid or duplicate bars: reject or deduplicate them before persistence rather than inserting ambiguous history.
- Database failure during snapshot write: roll back the snapshot transaction and mark the run failed if possible.

## Dependencies

- `docs/features/postgres-persistence.md`
- `AppDbContext` and EF Core migration support
- Market-data refresh worker or CLI command
- Existing Yahoo Finance adapter or a future `IMarketDataProvider` implementation

## Test Scenarios

| Scenario | Input | Expected Output |
|----------|-------|-----------------|
| Full refresh success | 4 symbols, each with 365 valid bars | Run status `Succeeded`, one `Complete` snapshot, quote rows inserted, no missing-symbol rows |
| Partial refresh | 4 symbols requested, 1 symbol returns no usable bars | Run status `Partial`, one `Partial` snapshot, 3 symbols persisted, 1 missing-symbol row |
| Provider total failure | Transport/auth/parsing failure for all symbols | Run status `Failed`, no snapshot row, no quote rows |
| Duplicate bar in provider payload | Same symbol and quote date appears twice | Worker deduplicates or rejects before insert; unique constraint is never violated in normal flow |
| Live read against expired snapshot | Snapshot exists but `fresh_until_utc <= now()` | Snapshot rejected for live execution; affected symbols remain `WAIT` |
| Historical read for `run-asof` | Snapshot with `as_of_date <= requestedDate` but old wall-clock TTL | Historical flow can still select it based on `as_of_date` ordering |
| Snapshot pruning | Old snapshot deleted | Child quote and missing-symbol rows are deleted automatically, refresh-run row can remain |

## Optional Improvements

- Store the exact requested symbol list on `market_data_refresh_run` as `jsonb` for replay tooling.
- Add a repair worker that retries only `is_retryable = true` missing symbols.
- Introduce bulk-loading optimizations or table partitioning if the watchlist expands materially.
- Split immutable quote history from snapshot manifests later if storage duplication becomes a real cost.
