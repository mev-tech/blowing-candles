# Fix Specification: update-docs-readme-api-first-fixes

## Summary of Deviations

Six deviations between the spec and the implementation were found during review. Two are spec inaccuracies where the implementation is correct. Four are additional changes made beyond the spec's scope to fully eliminate dead `output`/`state` references from tests, fixtures, and application code that the spec did not cover.

1. **Spec used wrong property names for API request bodies.** The spec (change #4, API Endpoints table) listed `{ "date": "yyyy-MM-dd" }` for the as-of endpoint and `{ "start": "yyyy-MM-dd", "end": "yyyy-MM-dd" }` for the range endpoint. The actual API code in `ApiHost.cs` uses `asOfDate`, `startDate`, and `endDate`. The README correctly reflects the actual API.

2. **Spec used em-dashes where README uses hyphens.** The spec used `—` (em-dash) in the intro paragraph and prerequisites section. The README uses `-` (hyphen) consistently throughout, matching the pre-existing style of the document.

3. **Cross-validation fixture configs still had dead `output` and `state` sections.** The spec (change #10) only addressed the root `config.yaml`. Three test fixture configs under `tests/fixtures/cross-validation/` also contained dead `output` and `state` sections. The implementation removed them.

4. **`YamlConfigLoaderTests.cs` still tested dead `output` and `state` parsing.** The spec (changes #11–12) removed `OutputConfig`/`StateConfig` from `AppConfig.cs` and `YamlConfigLoader.cs` but did not address the corresponding test file. Three tests included `output`/`state` config YAML and asserted against `config.Output.*` and `config.State.*` properties. The implementation removed the dead config YAML, dead assertions, and renamed two tests to reflect the narrower scope.

5. **`SignalsEndpointTests.cs` test workspace config had vestigial `output` and `state` sections.** The spec did not address this file. The implementation removed the dead config sections from the test workspace setup.

6. **`SignalsJsonSerializer.cs` had a stale error message referencing `signals.json`.** The `DeserializeSignals` method threw `"signals.json did not contain a JSON array."` The implementation updated it to `"Signal payload did not contain a JSON array."` since the method now deserializes API/DB payloads, not files.

## Deviation Details

### 1. API request body property names (spec inaccuracy — no change needed)

Spec (change #4) specified:

| Endpoint | Spec body | Actual API body (README is correct) |
|----------|-----------|-------------------------------------|
| `POST /api/runs/asof` | `{ "date": "yyyy-MM-dd" }` | `{ "asOfDate": "yyyy-MM-dd" }` |
| `POST /api/runs/range` | `{ "start": "yyyy-MM-dd", "end": "yyyy-MM-dd" }` | `{ "startDate": "yyyy-MM-dd", "endDate": "yyyy-MM-dd" }` |

Verified against `src/BlowingCandles.Api/ApiHost.cs`:
- Line 219: `"asOfDate is required."`
- Line 241: `"startDate and endDate are required."`

The README (lines 86–87) already uses the correct property names. No change needed.

### 2. Em-dash vs hyphen (style — no change needed)

| Location | Spec | README |
|----------|------|--------|
| Intro (line 6) | `manually — there` | `manually - there` |
| Prerequisites (line 13) | `(recommended) — Docker` | `(recommended) - Docker` |
| Prerequisites (line 14) | `**Local** — .NET 8 SDK` | `**Local** - .NET 8 SDK` |

The README uses hyphens consistently throughout. This is the pre-existing document style. No change needed.

### 3. Cross-validation fixture configs (beyond spec scope — already applied)

Files: `tests/fixtures/cross-validation/{all-wait,empty-watchlist,mixed-actions}/config.yaml`

Removed from each:
```yaml
output:
  text_file: signals.txt
  json_file: signals.json
state:
  path: data/state.json
```

These dead sections caused no runtime errors (due to `IgnoreUnmatchedProperties()` in the YAML deserializer) but were inconsistent with the removal of `OutputConfig`/`StateConfig` from the codebase.

### 4. `YamlConfigLoaderTests.cs` (beyond spec scope — already applied)

File: `tests/BlowingCandles.Infrastructure.Tests/YamlConfigLoaderTests.cs`

Changes:
- `Load_FullConfig_LoadsAllSectionsAndResolvesRelativePaths` renamed to `Load_FullConfig_LoadsConsumedSectionsAndResolvesRelativePaths`. Removed `output`/`state` YAML from config input and removed three assertions against `config.Output.*` and `config.State.*`.
- `Load_MinimalConfig_UsesDefaults` — removed three assertions against `config.Output.*` and `config.State.*`.
- `Load_BlankPathValues_FallBackToDefaultsOrNull` renamed to `Load_BlankOptionalPathValues_FallBackToNull`. Removed `output`/`state` YAML from config input and removed three assertions against `config.Output.*` and `config.State.*`.

### 5. `SignalsEndpointTests.cs` (beyond spec scope — already applied)

File: `tests/BlowingCandles.Api.Tests/SignalsEndpointTests.cs`

Removed from test workspace config string:
```yaml
                output:
                  text_file: output/live.signals.txt
                  json_file: output/live.signals.json
                state:
                  path: data/state.json
```

### 6. `SignalsJsonSerializer.cs` error message (beyond spec scope — already applied)

File: `src/BlowingCandles.Application/SignalsJsonSerializer.cs`

Replaced:
```csharp
?? throw new JsonException("signals.json did not contain a JSON array.");
```
With:
```csharp
?? throw new JsonException("Signal payload did not contain a JSON array.");
```

## Required Changes

None. All deviations are either spec inaccuracies (1–2) where the implementation is correct, or additional changes beyond spec scope (3–6) that are already applied and correct.

## Validation

- [x] `dotnet build` — no errors
- [x] `dotnet test` — all 200 tests pass (59 Domain + 13 Application + 97 Infrastructure + 25 Api + 6 CrossValidation)
- [x] README accurately reflects current API-first architecture
- [x] `config.yaml` contains only consumed config sections
- [x] No references to `OutputConfig`, `StateConfig`, `signals.txt`, `signals.json`, or `state.json` remain in source code (excluding docs, review specs, and test golden files)
