# Feature: PostgreSQL Persistence Infrastructure

## Feature Name

postgres-persistence

## Summary

Introduce the PostgreSQL persistence foundation needed for future database-backed features. This phase adds the PostgreSQL provider, `AppDbContext`, migration support, dependency injection registration, and database health checks, but it does **not** add any business tables, repositories, or domain-specific persistence behavior.

## Current C# Behavior Constraints

The current C# application has no database layer. Its persistent behavior is file-based only:

- `state.json` stores trade-governor state
- `logs/decisions.jsonl` stores append-only audit history
- `signals.txt` and `signals.json` store pipeline output

This feature is infrastructure-only and must remain additive:

- Do not change signal-generation behavior.
- Do not change output formats, exit codes, or file contracts.
- Do not replace existing file-backed state or audit storage in this phase.
- Preserve the fail-safe invariant that all errors default to `WAIT`.

## Constraint Relaxation

The current `docs/requirements.md` lists two constraints that this feature intentionally relaxes:

| Constraint | Current | After This Feature |
|---|---|---|
| "No database — All persistence is file-based" | Enforced | Relaxed — PostgreSQL is available as an infrastructure dependency; file-based persistence remains intact |
| "No DI container — Manual wiring only" | Enforced | Relaxed — A minimal `ServiceCollection`-based composition root is introduced to register `AppDbContext` and health checks |

These constraints were appropriate when the system was a pure CLI with no external runtime dependencies. Introducing PostgreSQL requires service registration and configuration binding that manual `new` calls cannot cleanly support. The relaxation is scoped: Domain and Application remain free of DI or EF Core dependencies, and the existing manual wiring for non-database services is preserved until a future feature migrates them.

Update `docs/requirements.md` and `docs/architecture.md` to reflect these changes once this feature lands.

## Acceptance Criteria

- [ ] PostgreSQL configuration is loaded from `appsettings.json`.
- [ ] `AppDbContext` exists under `Infrastructure/Persistence`.
- [ ] EF Core migrations can be created and applied successfully.
- [ ] Persistence registration is centralized behind a single infrastructure DI module.
- [ ] Basic PostgreSQL health checks are registered.
- [ ] No domain-specific tables or `DbSet` properties are introduced yet.
- [ ] Domain and Application projects remain free of EF Core dependencies.
- [ ] `docker-compose.yml` includes a PostgreSQL service.
- [ ] Existing CLI behavior is unchanged — all commands produce identical output.

## 1. Feature Overview

This feature establishes a clean persistence module in the Infrastructure layer so later features can store business data in PostgreSQL without reworking application startup, configuration, or tooling. It is intentionally infrastructure-only.

In scope:

- PostgreSQL provider wiring through EF Core and Npgsql
- `AppDbContext`
- migrations support
- DI registration via `ServiceCollection` (not a full generic host)
- `appsettings.json` connection configuration
- basic health checks
- `docker-compose.yml` PostgreSQL service

Out of scope:

- business entities
- business tables
- repositories for future aggregates
- data seeding
- replacing current JSON or JSONL persistence paths
- changing any existing command behavior
- migrating existing manual wiring to DI (deferred to a future feature)

## 2. Architecture Impact

The current application is a synchronous CLI with manual object construction in `Program.cs`. Introducing PostgreSQL cleanly requires configuration binding and service registration that pure `new` calls cannot support.

Recommended impact:

- Keep Domain and Application unchanged.
- Add all EF Core and PostgreSQL code to Infrastructure only.
- Introduce a minimal `ServiceCollection`-based composition root in the CLI for persistence services only.
- Keep existing manual wiring for non-database services (`YamlConfigLoader`, `OutputRenderer`, handlers) unchanged.
- Use `Microsoft.Extensions.Configuration` for `appsettings.json` binding without adopting the full generic host.
- Keep `config.yaml` for current trading configuration and introduce `appsettings.json` only for infrastructure/runtime services such as PostgreSQL.

Important boundaries:

- `config.yaml` remains the source of truth for current signal-generation inputs.
- `appsettings.json` is added as a new configuration source for database infrastructure only.
- No existing YAML contracts should be removed or renamed in this phase.
- The full generic host (`Host.CreateApplicationBuilder`) is deferred until a future feature (such as the REST API or a refresh worker) justifies it.

### DI Transition Path

The current `Program.cs` constructs all services manually:

```csharp
var configLoader = new YamlConfigLoader();
var outputRenderer = new OutputRenderer();
var handler = new RunRealtimeHandler(configLoader, outputRenderer);
```

This feature introduces a `ServiceCollection` alongside the existing manual wiring:

```csharp
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .Build();

var services = new ServiceCollection();
services.AddPersistence(configuration);
var serviceProvider = services.BuildServiceProvider();

// Existing manual wiring remains unchanged
var configLoader = new YamlConfigLoader();
var outputRenderer = new OutputRenderer();
```

Future features that need `AppDbContext` resolve it from `serviceProvider`. Non-database services stay manually wired until a future migration.

## 3. Folder Structure

```text
src/
  BlowingCandles.Infrastructure/
    Persistence/
      AppDbContext.cs
      DependencyInjection.cs
      Options/
        PersistenceOptions.cs
      DesignTime/
        AppDbContextFactory.cs
      HealthChecks/
        PostgreSqlHealthCheck.cs
      Migrations/
        ...
  BlowingCandles.Cli/
    Program.cs
    appsettings.json
    appsettings.Development.json
```

Notes:

- All persistence code lives under `Infrastructure/Persistence`, including migrations.
- `appsettings.json` belongs to the startup project because it is a runtime concern.
- `appsettings.Development.json` is optional but useful for local overrides.
- Do not add `Entities/` or `Configurations/` folders until the first real table is introduced (Phase 10 — Market Data Persistence).

## 4. DbContext Design

`AppDbContext` should be minimal at this stage:

- Inherit from `DbContext`.
- Accept `DbContextOptions<AppDbContext>` through the constructor.
- Expose no `DbSet<T>` properties yet.
- Apply future entity configurations through `ApplyConfigurationsFromAssembly(...)`.
- Contain no domain logic, migration commands, or connection-string lookup.

```csharp
namespace BlowingCandles.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
```

Design decisions:

- **Snake_case naming convention.** The downstream consumer (`docs/features/market-data-persistence.md`) already commits to snake_case table names (`market_data_refresh_run`, `market_data_snapshot`, etc.). Adopt snake_case from day one through explicit Fluent API configuration per entity. Do not use a global naming convention package — explicit table and column names in `IEntityTypeConfiguration<T>` classes give the most control and avoid surprise when adding new entities.
- Keep the context empty until the first persistence-backed feature needs tables.
- Do not add placeholder entities just to make migrations "look useful".
- Avoid EF attributes in Domain models. If persistence models are later needed, keep them in Infrastructure.

## 5. Dependency Injection Configuration

Create a single infrastructure registration entry point:

```csharp
namespace BlowingCandles.Infrastructure.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PersistenceOptions>(
            configuration.GetSection(PersistenceOptions.SectionName));

        var connectionString = configuration.GetConnectionString("AppDb")
            ?? throw new InvalidOperationException(
                "Connection string 'AppDb' is missing from configuration.");

        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var persistenceOptions = serviceProvider
                .GetRequiredService<IOptions<PersistenceOptions>>()
                .Value;

            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.CommandTimeout(persistenceOptions.CommandTimeoutSeconds);
            });

            if (persistenceOptions.EnableDetailedErrors)
            {
                options.EnableDetailedErrors();
            }

            if (persistenceOptions.EnableSensitiveDataLogging)
            {
                options.EnableSensitiveDataLogging();
            }
        });

        services.AddHealthChecks()
            .AddCheck<PostgreSqlHealthCheck>(
                "postgresql",
                tags: new[] { "ready", "db" });

        return services;
    }
}
```

Startup guidance:

- Use `ServiceCollection` directly — do not introduce the full generic host yet.
- Keep `IServiceProvider` usage at the composition root only.
- Do not spread service locator patterns into handlers or domain services.
- If command handlers need database access later, resolve them through DI instead of constructing them manually.

## 6. Connection Configuration

Use `appsettings.json` for database configuration.

### appsettings.json

```json
{
  "ConnectionStrings": {
    "AppDb": "Host=localhost;Port=5432;Database=blowingcandles;Username=postgres;Password=postgres"
  },
  "Persistence": {
    "CommandTimeoutSeconds": 30,
    "EnableDetailedErrors": false,
    "EnableSensitiveDataLogging": false
  }
}
```

### appsettings.Development.json

```json
{
  "ConnectionStrings": {
    "AppDb": "Host=localhost;Port=5432;Database=blowingcandles_dev;Username=postgres;Password=postgres"
  },
  "Persistence": {
    "EnableDetailedErrors": true,
    "EnableSensitiveDataLogging": true
  }
}
```

### PersistenceOptions

```csharp
namespace BlowingCandles.Infrastructure.Persistence.Options;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public int CommandTimeoutSeconds { get; init; } = 30;
    public bool EnableDetailedErrors { get; init; }
    public bool EnableSensitiveDataLogging { get; init; }
}
```

Configuration rules:

- Store the connection string under `ConnectionStrings:AppDb`.
- Store provider behavior flags under `Persistence`.
- Do not commit production credentials to source control.
- Support environment-variable overrides such as `ConnectionStrings__AppDb`.
- For local development, prefer `appsettings.Development.json` or user secrets for passwords.
- The `appsettings.json` connection string should match the `docker-compose.yml` PostgreSQL service defaults for zero-config local development.

## 7. Docker Compose Integration

Extend the existing `docker-compose.yml` with a PostgreSQL service:

```yaml
services:
  app:
    build: .
    image: blowing-candles
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      - ConnectionStrings__AppDb=Host=postgres;Port=5432;Database=blowingcandles;Username=postgres;Password=postgres
    volumes:
      - ./config.yaml:/app/config.yaml:ro
      - ./earnings_calendar.json:/app/earnings_calendar.json:ro
      - ./data:/app/data
      - ./logs:/app/logs

  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: blowingcandles
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d blowingcandles"]
      interval: 5s
      timeout: 3s
      retries: 5

volumes:
  pgdata:
```

Notes:

- The `app` service overrides `ConnectionStrings__AppDb` via environment variable so it can reach the `postgres` service by hostname.
- The `depends_on` with `service_healthy` ensures PostgreSQL is ready before the app starts.
- `postgres:16-alpine` is chosen for small image size and PostgreSQL 16 compatibility with Npgsql 8.x.
- The named volume `pgdata` persists data across container restarts.

## 8. Migration Workflow

Migrations must be enabled from day one even though there are no business tables yet. This validates tooling and gives future features a stable path to evolve the schema.

### Required Packages

Add to `BlowingCandles.Infrastructure.csproj`:

```xml
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.11" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.11">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

Add to `BlowingCandles.Cli.csproj` (needed for `dotnet ef` startup project):

```xml
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.1" />
<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" Version="8.0.11" />
```

### Design-Time Factory

```csharp
namespace BlowingCandles.Infrastructure.Persistence.DesignTime;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(FindStartupProjectPath())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("AppDb")
            ?? throw new InvalidOperationException(
                "Connection string 'AppDb' is missing from configuration.");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
        });

        return new AppDbContext(optionsBuilder.Options);
    }

    private static string FindStartupProjectPath()
    {
        // Walk up from the current directory to find the CLI project
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "src", "BlowingCandles.Cli");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
```

### Migration Commands

Create the initial (empty) migration:

```bash
dotnet ef migrations add InitialPersistence \
  --project src/BlowingCandles.Infrastructure/BlowingCandles.Infrastructure.csproj \
  --startup-project src/BlowingCandles.Cli/BlowingCandles.Cli.csproj \
  --output-dir Persistence/Migrations
```

Apply migrations:

```bash
dotnet ef database update \
  --project src/BlowingCandles.Infrastructure/BlowingCandles.Infrastructure.csproj \
  --startup-project src/BlowingCandles.Cli/BlowingCandles.Cli.csproj
```

Workflow expectations:

- The initial migration may be effectively empty. That is acceptable.
- Applying the initial migration should validate the provider, configuration path, and migration history infrastructure (`__EFMigrationsHistory` table).
- Future table-adding features should append migrations under the same `Persistence/Migrations` folder.
- Do not hand-edit migration snapshots unless there is a concrete migration bug to fix.

## 9. Health Check Integration

Implement a custom `IHealthCheck` to avoid adding another package dependency:

```csharp
namespace BlowingCandles.Infrastructure.Persistence.HealthChecks;

public sealed class PostgreSqlHealthCheck : IHealthCheck
{
    private readonly AppDbContext _dbContext;

    public PostgreSqlHealthCheck(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                "SELECT 1", cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL connection is available.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "PostgreSQL connection failed.",
                exception: ex);
        }
    }
}
```

Guidance:

- Register the health check through `services.AddHealthChecks()` in the `AddPersistence` method.
- Tag it with `ready` and `db` for future filtering.
- Do not add an HTTP health endpoint in this feature. The health check is available through `HealthCheckService` so a future CLI command, worker, or API can consume it.
- The health check uses `AppDbContext` directly rather than opening a raw `NpgsqlConnection`, keeping it consistent with how the application accesses the database.

## 10. Testing Strategy

Do not rely on EF Core's in-memory provider for PostgreSQL infrastructure testing. It does not validate PostgreSQL behavior, migrations, or SQL translation.

### Unit Tests

- `PersistenceOptions` binding and defaults
- Missing `ConnectionStrings:AppDb` fails fast with `InvalidOperationException`
- DI registration resolves `AppDbContext`
- Design-time factory creates the context successfully
- Health check returns unhealthy for an invalid or unreachable connection string

### Integration Tests

- Run against a real PostgreSQL instance (via docker-compose or Testcontainers)
- Apply migrations with `Database.Migrate()`
- Verify the connection opens successfully
- Verify no business tables are created in this phase
- Verify only EF migration metadata (`__EFMigrationsHistory`) appears after the baseline migration

### Test Approach

- Keep unit tests in `tests/BlowingCandles.Infrastructure.Tests`
- For integration tests, prefer `Testcontainers.PostgreSql` (NuGet) to spin up a disposable PostgreSQL container per test class
- If container-based testing is deferred, document a manual smoke test:
  1. `docker compose up postgres -d`
  2. `dotnet ef database update --project src/BlowingCandles.Infrastructure --startup-project src/BlowingCandles.Cli`
  3. Verify `__EFMigrationsHistory` table exists with one row
  4. Verify no other application tables exist
  5. Run `dotnet run --project src/BlowingCandles.Cli -- run-realtime` and confirm identical behavior

## 11. Future Extension Points

This feature should make later database-backed work straightforward without forcing premature abstractions.

Expected future additions:

- Entity type configurations under `Infrastructure/Persistence/Configurations` (first consumer: Phase 10 — Market Data Persistence)
- Persistence models or mappings for real aggregates
- Transaction boundaries for multi-write workflows
- Query services for read models
- Migration bundles or startup migration application
- Richer health reporting and operational diagnostics
- Full generic host adoption when the REST API or refresh worker is introduced

Guardrails for future work:

- Add tables only when a feature requires them.
- Avoid introducing generic repository abstractions by default.
- Keep EF Core concerns out of Domain.
- Snake_case naming convention is decided — use explicit Fluent API table/column names.
- Keep file-based contracts intact until a future feature explicitly replaces them.

## Dependencies

### Infrastructure project packages

| Package | Version | Purpose |
|---|---|---|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 8.0.11 | EF Core PostgreSQL provider |
| `Microsoft.EntityFrameworkCore.Design` | 8.0.11 | Design-time migration tooling |

### CLI project packages

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Extensions.Configuration.Json` | 8.0.1 | `appsettings.json` binding |
| `Microsoft.Extensions.Diagnostics.HealthChecks` | 8.0.11 | Health check infrastructure |

### Runtime dependencies

| Dependency | Version | Purpose |
|---|---|---|
| PostgreSQL | 16 | Database server (via docker-compose) |

## Resolved Design Questions

| Question | Resolution | Rationale |
|---|---|---|
| Generic host or minimal ServiceCollection? | Minimal `ServiceCollection` | The CLI is not yet a hosted service. A full generic host is deferred until the REST API or refresh worker is introduced. |
| Snake_case or EF defaults for naming? | Snake_case via explicit Fluent API | The downstream market-data-persistence feature already commits to snake_case table names. Deciding now avoids migration churn later. |
| Migrations applied manually or at startup? | Manually via `dotnet ef` | Auto-migration at startup risks unintended schema changes in production. A future worker or API may revisit this. |
| Persistence maps to Domain models or Infrastructure entities? | Dedicated Infrastructure entities | Domain models are `sealed record` value objects. EF Core entities need mutable properties and navigation properties. Keep them separate. |

## Implementation Notes

- This feature is intentionally additive. It introduces infrastructure only and should not affect current signal generation.
- The cleanest implementation path is to centralize all persistence registration in Infrastructure and keep the CLI as the only composition root.
- The first follow-up feature should be the first real persistence-backed capability (Phase 10 — Market Data Persistence), not a placeholder table.
- When this feature lands, update `docs/architecture.md` to reflect the new Infrastructure/Persistence module and the relaxed DI/database constraints.
