# Fix Specification: market-data-persistence

## Summary of Issues

The nine infrastructure prerequisite changes (RC1–RC9) from the previous fix specification have been implemented correctly. All 65 tests pass and the solution builds with zero warnings. Two minor spec divergences remain in entity navigation property declarations that weaken the immutability guarantee on snapshot collections.

1. **Entity navigation setters weaken immutability.** `MarketDataSnapshotEntity.Quotes` and `MarketDataSnapshotEntity.MissingSymbols` use `{ get; set; } = [];` instead of the spec-recommended `{ get; } = [];`. This allows callers to replace the entire collection reference after construction, contradicting the immutable-snapshot design intent. EF Core works with either pattern, but the getter-only form is the correct defensive choice.

## Required Changes

### RC1. Remove setters from `MarketDataSnapshotEntity` navigation properties

**File:** `src/BlowingCandles.Infrastructure/Persistence/Entities/MarketDataSnapshotEntity.cs`

Change both navigation collection properties from `{ get; set; }` to `{ get; }`:

```csharp
// Before
public List<MarketDataSnapshotQuoteEntity> Quotes { get; set; } = [];
public List<MarketDataSnapshotMissingSymbolEntity> MissingSymbols { get; set; } = [];

// After
public List<MarketDataSnapshotQuoteEntity> Quotes { get; } = [];
public List<MarketDataSnapshotMissingSymbolEntity> MissingSymbols { get; } = [];
```

This matches the recommended entity shape in `docs/features/market-data-persistence.md` section 2 and prevents accidental collection replacement after entity construction.

## Affected Modules or Files

| File | Change Type |
|------|-------------|
| `src/BlowingCandles.Infrastructure/Persistence/Entities/MarketDataSnapshotEntity.cs` | Modify — remove setters from two navigation properties |

## Edge Cases to Address

1. **EF Core collection materialization.** EF Core populates getter-only collection navigation properties via the backing field when loading related entities with `Include()` or lazy loading. The `= []` initializer ensures the backing field is never null. No behavioral change expected — EF Core 8.x supports this pattern natively.

2. **Persistence service `BuildSnapshot` method.** The `MarketDataSnapshotPersistenceService.BuildSnapshot` method already adds items via `snapshot.Quotes.Add(...)` and `snapshot.MissingSymbols.Add(...)`, which works identically with getter-only properties. No code change needed in the service.

## Test Updates Required

1. **No test changes needed.** All existing tests add to the collections via `.Add()` or seed entities with explicit `Id` assignments. No test assigns a new list to `Quotes` or `MissingSymbols`. All 65 tests should continue to pass after the change.

2. **Verification step.** Run `dotnet build` and `dotnet test` to confirm zero compilation errors and all tests pass after removing the setters.
