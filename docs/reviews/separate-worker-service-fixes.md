# Fix Specification: separate-worker-service

## Summary of Issues

1. **Portainer admin setup timeout.** Portainer CE enforces a 5-minute timeout on the initial admin account creation page. If the container has been running longer than 5 minutes before first access, Portainer locks itself and refuses connections. This is an operational issue, not a code bug, but it affects the usability of the portainer-management feature that this feature depends on for visibility.

2. **Environment variable configuration pipeline ambiguity.** `ApiHost.ConfigureInfrastructureConfiguration` (lines 98–104) does not call `.AddEnvironmentVariables()`. The `Worker__Enabled`, `Worker__IntervalMinutes`, and `ConnectionStrings__AppDb` environment variables set in `docker-compose.yml` are picked up by the default `WebApplication.CreateBuilder()` pipeline, which calls `.AddEnvironmentVariables()` internally. However, the custom `ConfigureInfrastructureConfiguration` method adds additional JSON file sources *after* the default pipeline — these JSON sources could theoretically override environment variables if they contain matching keys (e.g., a `Worker:Enabled = true` in an `appsettings.json`). Adding an explicit `.AddEnvironmentVariables()` call at the end of the custom method ensures environment variables always win, which is the expected behavior for Docker container configuration.

## Required Changes

### 1. Add `.AddEnvironmentVariables()` to `ConfigureInfrastructureConfiguration`

Append `.AddEnvironmentVariables()` after the JSON file sources in `ApiHost.ConfigureInfrastructureConfiguration` to ensure Docker environment variables take precedence over any JSON file values.

**File:** `src/BlowingCandles.Api/ApiHost.cs`

**Before:**
```csharp
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddJsonFile(Path.Combine("src", "BlowingCandles.Api", "appsettings.json"), optional: true)
    .AddJsonFile(Path.Combine("src", "BlowingCandles.Api", "appsettings.Development.json"), optional: true)
    .AddJsonFile(Path.Combine("src", "BlowingCandles.Cli", "appsettings.json"), optional: true)
    .AddJsonFile(Path.Combine("src", "BlowingCandles.Cli", "appsettings.Development.json"), optional: true);
```

**After:**
```csharp
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddJsonFile(Path.Combine("src", "BlowingCandles.Api", "appsettings.json"), optional: true)
    .AddJsonFile(Path.Combine("src", "BlowingCandles.Api", "appsettings.Development.json"), optional: true)
    .AddJsonFile(Path.Combine("src", "BlowingCandles.Cli", "appsettings.json"), optional: true)
    .AddJsonFile(Path.Combine("src", "BlowingCandles.Cli", "appsettings.Development.json"), optional: true)
    .AddEnvironmentVariables();
```

This ensures that the configuration precedence order is: defaults → JSON files → environment variables → in-memory overrides (test-time). Last source wins in the Microsoft.Extensions.Configuration pipeline.

### 2. Document Portainer admin setup timeout in feature spec

Add a note to `docs/features/portainer-management.md` under the Error Handling section documenting the 5-minute admin setup timeout and the recovery procedure.

**File:** `docs/features/portainer-management.md`

**Add to Error Handling section:**
```markdown
- Portainer CE enforces a 5-minute timeout on the initial admin account creation page. If the container runs longer than 5 minutes before first access, Portainer locks itself. Recovery: restart the container (`docker compose restart portainer`) or reset its data (`docker volume rm <project>_portainer_data`) and navigate to `https://localhost:9443` immediately.
```

## Affected Modules or Files

| File | Action |
|------|--------|
| `src/BlowingCandles.Api/ApiHost.cs` | Add `.AddEnvironmentVariables()` to `ConfigureInfrastructureConfiguration` |
| `docs/features/portainer-management.md` | Document admin setup timeout under Error Handling |

## Edge Cases to Address

- **JSON file overriding environment variables.** If any `appsettings.json` file (Api or Cli project) contains `Worker:Enabled = true`, the current pipeline could silently override `Worker__Enabled=false` from docker-compose on the `app` service, causing the worker to run inside the API container despite the intent to disable it. Adding `.AddEnvironmentVariables()` at the end prevents this.
- **Test-time configuration overrides.** The existing `configurationOverrides` parameter (`.AddInMemoryCollection()`) is added *after* `.AddEnvironmentVariables()`, so test-time overrides will still take highest precedence. No test behavior change expected.

## Test Updates Required

- Verify existing `BlowingCandles.Api.Tests` still pass after adding `.AddEnvironmentVariables()`. Tests use `configurationOverrides` (in-memory collection) which is added after environment variables, so they should be unaffected.
- No new tests required — this is a configuration precedence fix, not a behavior change.
