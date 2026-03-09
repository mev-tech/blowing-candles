# Feature: Testcontainers Integration

## Feature Name

testcontainers-integration

## Purpose

Replace the EF Core InMemory provider and hardcoded localhost Npgsql connections in the infrastructure test suite with Testcontainers for PostgreSQL. A single `postgres:16-alpine` container is spun up once per test run via an xUnit collection fixture, the EF Core migration is applied against it, and all persistence-related tests execute against real PostgreSQL. This eliminates behavioral gaps between InMemory and Npgsql (check constraints, unique indexes, cascade deletes, `DateOnly`/`DateTimeOffset` mapping, decimal precision) and validates the migration itself as part of every test run.

## Inputs

| Input | Source | Format | Required |
|-------|--------|--------|----------|
| Docker daemon | Host environment | Docker socket | Yes |
| EF Core migration assembly | `BlowingCandles.Infrastructure` | compiled migration classes | Yes |

## Outputs

| Output | Format | Destination |
|--------|--------|-------------|
| Ephemeral PostgreSQL container | Docker container | localhost (random port, assigned by Testcontainers) |
| Migrated database schema | four `market_data_*` tables | container instance |
| Test results | xUnit output | CI / terminal |

## Configuration

### Package changes to `BlowingCandles.Infrastructure.Tests.csproj`

| Package | Action |
|---------|--------|
| `Testcontainers.PostgreSql` | Add (v4.x) |
| `Microsoft.EntityFrameworkCore.InMemory` | Remove |

### Fixture location

`tests/BlowingCandles.Infrastructure.Tests/Fixtures/PostgresContainerFixture.cs`

### Container image

`postgres:16-alpine` — must match the production image in `docker-compose.yml`.

### xUnit collection

All test classes that need the container use `[Collection(PostgresContainerCollection.Name)]` and accept `PostgresContainerFixture` via constructor injection.

## Edge Cases

1. **Docker not available.** Testcontainers throws during fixture initialization. All container-dependent tests fail with a clear error. Non-container tests (`PersistenceOptions_DefaultValuesMatchSpecification`, `AddPersistence_MissingConnectionString_ThrowsInvalidOperationException`) are unaffected.

2. **Health check test must stay unreachable.** `PostgreSqlHealthCheck_UnreachableDatabase_ReturnsUnhealthy` intentionally connects to `127.0.0.1:1`. It must not use the Testcontainers instance.

3. **Test ordering.** Each test calls `await _fixture.ResetAsync()` at the top to truncate tables. Tests must not depend on data left by previous tests.

4. **Synchronous to async migration.** Current test methods are synchronous (`void` return). Methods that call `ResetAsync()` must change to `async Task`. Assertion logic remains unchanged.

5. **Identity column resets.** `TRUNCATE ... RESTART IDENTITY CASCADE` resets auto-increment sequences so ID-dependent assertions in seeded data remain predictable.

## Implementation Notes

### 1. Shared Container Fixture

```csharp
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    public async Task ResetAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE market_data_refresh_run RESTART IDENTITY CASCADE");
    }
}

[CollectionDefinition(Name)]
public class PostgresContainerCollection : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "PostgresContainer";
}
```

### 2. Test class migration pattern

Each test class that currently uses `CreateInMemoryContext()` or `CreateRelationalContext()`:

1. Add `[Collection(PostgresContainerCollection.Name)]` to the class.
2. Accept `PostgresContainerFixture` via constructor injection.
3. Replace `CreateInMemoryContext()` / `CreateRelationalContext()` calls with `_fixture.CreateDbContext()`.
4. Remove the private `CreateInMemoryContext()` / `CreateRelationalContext()` helper methods.
5. Add `await _fixture.ResetAsync()` at the top of each test.
6. Change method return type from `void` to `async Task`.

### 3. Tests that stay unchanged

These tests do not need a real database and must not depend on the container:

- `PersistenceOptions_DefaultValuesMatchSpecification` — tests a POCO default.
- `AddPersistence_MissingConnectionString_ThrowsInvalidOperationException` — tests DI validation logic.
- `PostgreSqlHealthCheck_UnreachableDatabase_ReturnsUnhealthy` — intentionally uses an unreachable connection.

### 4. DI resolution test update

`AddPersistence_ValidConnectionString_ResolvesAppDbContext` currently uses a hardcoded localhost connection string in an in-memory configuration. Replace the connection string value with `_fixture.ConnectionString` so the resolved `AppDbContext` can actually connect. This test joins the `[Collection]`.

## Test Scenarios

| Scenario | Expected Outcome |
|----------|-----------------|
| Fixture starts container and applies migration | All four `market_data_*` tables exist, `__EFMigrationsHistory` has one row |
| `PersistRefresh_FullSuccess` against real PostgreSQL | Run and snapshot rows persisted with correct statuses, quote rows queryable |
| `PersistRefresh_PartialSuccess` against real PostgreSQL | Missing-symbol row persisted, `Partial` statuses set |
| `PersistRefresh_TotalFailure` against real PostgreSQL | Failed run row, no snapshot, no quotes |
| `PersistRefresh_DeduplicatesBarsBySymbolAndQuoteDate` | Single quote row per `(symbol, quote_date)` enforced by real unique index |
| `PersistRefresh_SymbolCannotBePersistedAndMissing` | `InvalidOperationException` raised before DB write |
| `GetLivePriceHistory_ExpiredSnapshot_ReturnsEmpty` | Freshness gating works with real `timestamptz` comparison |
| `GetHistoricalPriceHistory_ExpiredSnapshotStillSelectable` | As-of-date ordering works with real `date` type |
| `GetHistoricalPriceHistory_MissingSymbol_ReturnsEmpty` | Missing-symbol exclusion via real foreign key join |
| `LiveSnapshotProvider_ReadsFromFreshSnapshot` | End-to-end read through provider with real PostgreSQL |
| `HistoricalSnapshotProvider_IgnoresExpiredTtl` | Historical read ignores TTL with real `timestamptz` |
| `Model_UsesRequiredSnakeCaseTablesAndConstraints` | Real Npgsql metadata shows correct table names and check constraints |
| `Model_EnforcesRequiredUniqueIndexes` | Real Npgsql metadata shows correct unique indexes |
| `AddPersistence_ValidConnectionString_ResolvesAppDbContext` | `AppDbContext` resolves against live Testcontainers PostgreSQL |
| Table truncation resets state between tests | Two tests writing overlapping IDs both succeed independently |
| Check constraint violation (negative `freshness_ttl_seconds`) | PostgreSQL raises `DbUpdateException` (InMemory would silently allow) |
| Cascade delete from snapshot | Deleting a snapshot removes child quote and missing-symbol rows |
