# Testcontainers Integration — Fix Specification

## Summary of Issues

1. **`PostgreSqlBuilder` constructor style.** The fixture passes the image name as a constructor argument (`new PostgreSqlBuilder("postgres:16-alpine")`) instead of using the canonical builder method (`.WithImage("postgres:16-alpine")`). Functionally equivalent but deviates from the spec and the documented Testcontainers API.

2. **Missing `ResetAsync()` in `Fixture_StartsContainerAndAppliesMigration`.** The spec requires every test in the collection to call `await _fixture.ResetAsync()` at the top. This read-only test omits it. While safe today, it breaks the universal convention and could mask ordering issues if the test evolves.

## Required Changes

### 1. Use canonical `PostgreSqlBuilder` API

Replace the constructor-argument style with the builder-method style.

**Before:**
```csharp
private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
    .WithDatabase("blowing_candles_tests")
    .WithUsername("postgres")
    .WithPassword("postgres")
    .Build();
```

**After:**
```csharp
private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
    .WithImage("postgres:16-alpine")
    .WithDatabase("blowing_candles_tests")
    .WithUsername("postgres")
    .WithPassword("postgres")
    .Build();
```

### 2. Add `ResetAsync()` to `Fixture_StartsContainerAndAppliesMigration`

**Before:**
```csharp
[Fact]
public async Task Fixture_StartsContainerAndAppliesMigration()
{
    await using var dbContext = _fixture.CreateDbContext();
    // ...
}
```

**After:**
```csharp
[Fact]
public async Task Fixture_StartsContainerAndAppliesMigration()
{
    await _fixture.ResetAsync();
    await using var dbContext = _fixture.CreateDbContext();
    // ...
}
```

## Affected Modules or Files

| File | Change |
|------|--------|
| `tests/BlowingCandles.Infrastructure.Tests/Fixtures/PostgresContainerFixture.cs` | Builder API style |
| `tests/BlowingCandles.Infrastructure.Tests/AppDbContextModelTests.cs` | Add `ResetAsync()` call |

## Edge Cases to Address

None. Both changes are cosmetic and do not alter test behavior or introduce new code paths.

## Test Updates Required

No new tests needed. Existing tests must continue to pass after the changes. Run:

```
dotnet test tests/BlowingCandles.Infrastructure.Tests/BlowingCandles.Infrastructure.Tests.csproj --no-restore -v minimal
```
