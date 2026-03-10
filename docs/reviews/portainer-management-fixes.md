# Fix Specification: portainer-management

## Summary of Issues

1. **Unrelated change bundled into feature scope.** The `ApiHost.cs` change adding `.AddEnvironmentVariables()` is not part of the portainer-management feature and should be removed from this branch's scope.
2. **Potential architectural constraint violation.** The architecture doc's "What Not to Build" section states "No environment variable overrides for configuration." The `.AddEnvironmentVariables()` addition serves ASP.NET infrastructure config (connection strings, worker options) rather than domain `AppConfig`, but this distinction is not documented. If the change is intentional, it belongs in a separate feature with an architecture doc clarification.

## Required Changes

### 1. Revert `ApiHost.cs` from this feature branch

Revert the `.AddEnvironmentVariables()` addition in `src/BlowingCandles.Api/ApiHost.cs`. This change is unrelated to Portainer container management and must be handled separately.

**Before (current):**
```csharp
.AddJsonFile(Path.Combine("src", "BlowingCandles.Cli", "appsettings.Development.json"), optional: true)
.AddEnvironmentVariables();
```

**After (reverted):**
```csharp
.AddJsonFile(Path.Combine("src", "BlowingCandles.Cli", "appsettings.Development.json"), optional: true);
```

### 2. Create a separate feature for environment variable configuration (if needed)

If `.AddEnvironmentVariables()` is intentionally required (e.g., for `docker-compose.yml` environment variable injection of `ConnectionStrings__AppDb` and `Worker__*`), it should be:
- Tracked as its own feature (e.g., `environment-variable-config`)
- Accompanied by an architecture doc update clarifying that environment variable overrides apply to ASP.NET infrastructure configuration only, not to domain `AppConfig` loaded from `config.yaml`

## Affected Modules or Files

| File | Action |
|------|--------|
| `src/BlowingCandles.Api/ApiHost.cs` | Revert `.AddEnvironmentVariables()` addition |
| `docker-compose.yml` | No changes needed — implementation is correct |

## Edge Cases to Address

- **`docker-compose.yml` environment variables without `.AddEnvironmentVariables()`.** After reverting, verify that `ConnectionStrings__AppDb`, `Worker__Enabled`, and `Worker__IntervalMinutes` defined in `docker-compose.yml` are still picked up by the app. ASP.NET's `WebApplication.CreateBuilder()` calls `.AddEnvironmentVariables()` by default on the root configuration, so the explicit call may be redundant. If the default pipeline already loads environment variables, the revert is safe. If not, this confirms the change belongs in a separate feature that must land before or alongside portainer-management.

## Test Updates Required

- No test changes needed for the docker-compose portainer addition (infrastructure-only, no application code).
- If the `.AddEnvironmentVariables()` revert breaks the default configuration pipeline, any existing `ApiHost` integration tests that rely on environment variable injection should be checked. Verify `BlowingCandles.Api.Tests` still passes after the revert.
