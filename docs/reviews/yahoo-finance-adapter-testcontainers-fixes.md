# Fix Specification: yahoo-finance-adapter (Testcontainers migration)

**Status:** DEFERRED TO PHASE 11
**Reviewed:** 2026-03-09

## Deferral Rationale

All changes in this document are deferred to Phase 11 for the following reasons:

1. **The current implementation is correct and complete.** In-process WireMock exercises real HTTP transport (real `HttpClient` over real TCP to localhost). The contract suite goal — "exercise the real `YahooFinanceHttpTransport` through an actual HTTP socket boundary" — is already met.
2. **The migration is a consistency preference, not a correctness fix.** The only benefit is alignment with Phase 11's Testcontainers-first approach. No test scenario changes, no behavioral difference.
3. **Phase 11 is the right home.** The architecture doc describes Phase 11 as replacing InMemory and hardcoded-localhost tests with Testcontainers. Migrating WireMock to Testcontainers is the same category of work.
4. **Premature Docker dependency.** Applying the fixes now would require Docker for Yahoo contract tests before the PostgreSQL Testcontainers infrastructure exists. Phase 11 introduces Docker as a test dependency holistically.
5. **Added complexity for no gain.** The migration changes sync stubs to async admin API calls, `IClassFixture` to `[Collection]`, and `IDisposable` to `IAsyncLifetime` — mechanical churn with no functional improvement. Better to do this once when Phase 11 lands all Testcontainers changes together.

## Summary of Issues

The current feature spec (`docs/features/yahoo-finance-adapter.md`) uses `WireMock.Net` in-process (`WireMockServer.Start()`) for the Yahoo Finance contract suite. This works but diverges from the project's Testcontainers-first testing strategy established in Phase 11. The contract suite should use `WireMock.Net.Testcontainers` to run WireMock inside a Docker container managed by Testcontainers for .NET, consistent with how PostgreSQL tests use `Testcontainers.PostgreSql`.

1. **Package choice.** The spec lists `WireMock.Net` (in-process). It should use `WireMock.Net.Testcontainers` instead, which pulls in `WireMock.Net` transitively for the admin client API.

2. **Fixture lifecycle.** The spec uses `IClassFixture<WireMockServerFixture>` with `WireMockServer.Start()`. It should use an `IAsyncLifetime` fixture that starts a `WireMockContainer` via Testcontainers, matching the `PostgresContainerFixture` pattern from Phase 11.

3. **Stub configuration approach.** In-process WireMock uses the fluent C# API directly on the `WireMockServer` instance. With Testcontainers, stubs are configured via the WireMock admin REST API through the `WireMock.Net` `IWireMockAdminApi` or the container's `CreateMappingAsync` / admin HTTP calls.

4. **xUnit wiring.** The spec uses `IClassFixture` (synchronous). The Testcontainers fixture requires `IAsyncLifetime`, and contract tests should use `[Collection]` to share a single container across test classes (same pattern as PostgreSQL).

5. **Docker dependency acknowledgement.** The spec explicitly avoided Docker. With this migration, the Yahoo contract tests join the PostgreSQL Testcontainers tests in requiring Docker, which is acceptable since CI already needs Docker for Phase 11.

## Required Changes

### RC1. Replace `WireMock.Net` package with `WireMock.Net.Testcontainers`

**File:** `tests/BlowingCandles.Infrastructure.Tests/BlowingCandles.Infrastructure.Tests.csproj`

```xml
<!-- Before -->
<PackageReference Include="WireMock.Net" Version="1.25.0" />

<!-- After -->
<PackageReference Include="WireMock.Net.Testcontainers" Version="1.25.0" />
```

`WireMock.Net.Testcontainers` transitively includes `WireMock.Net` (for the admin API client) and `Testcontainers` (for container lifecycle).

### RC2. Replace `WireMockServerFixture` with `WireMockContainerFixture`

**File:** `tests/BlowingCandles.Infrastructure.Tests/Fixtures/WireMockContainerFixture.cs` (rename from `WireMockServerFixture.cs`)

Replace the in-process `WireMockServer.Start()` fixture with a Testcontainers-managed container:

```csharp
using WireMock.Net.Testcontainers;
using WireMock.Client;
using WireMock.Client.Extensions;

public sealed class WireMockContainerFixture : IAsyncLifetime
{
    private readonly WireMockContainer _container = new WireMockContainerBuilder()
        .WithImage("wiremock/wiremock:latest")
        .Build();

    public string Url => _container.GetPublicUrl();
    public Uri BaseUri => new(Url);
    public IWireMockAdminApi AdminClient => _container.CreateWireMockAdminClient();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public async Task ResetAsync()
    {
        await AdminClient.ResetMappingsAsync();
    }
}

[CollectionDefinition(Name)]
public class WireMockContainerCollection : ICollectionFixture<WireMockContainerFixture>
{
    public const string Name = "WireMockContainer";
}
```

Key differences from the in-process fixture:
- `IAsyncLifetime` instead of `IDisposable` (container start/stop is async)
- `WireMockContainer` instead of `WireMockServer`
- `GetPublicUrl()` provides the container's mapped port URI
- `CreateWireMockAdminClient()` provides the `IWireMockAdminApi` for stub management
- `ResetMappingsAsync()` instead of `Server.Reset()`

### RC3. Update contract test class to use collection fixture and async stubs

**File:** `tests/BlowingCandles.Infrastructure.Tests/YahooFinanceContractTests.cs`

```csharp
// Before
public sealed class YahooFinanceContractTests : IClassFixture<WireMockServerFixture>
{
    private readonly WireMockServerFixture _fixture;

    public YahooFinanceContractTests(WireMockServerFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }
    // ...
}

// After
[Collection(WireMockContainerCollection.Name)]
public sealed class YahooFinanceContractTests
{
    private readonly WireMockContainerFixture _fixture;

    public YahooFinanceContractTests(WireMockContainerFixture fixture)
    {
        _fixture = fixture;
    }
    // ...
}
```

Changes:
- `[Collection(WireMockContainerCollection.Name)]` replaces `IClassFixture<>` to share a single container across all WireMock-dependent test classes.
- Each test method calls `await _fixture.ResetAsync()` at the top (same pattern as PostgreSQL tests calling `await _fixture.ResetAsync()` for table truncation).
- Test methods change from `void` to `async Task` because stub registration and reset are async.

### RC4. Migrate stub registration from fluent API to admin client

In-process WireMock uses the synchronous fluent API:

```csharp
// Before (in-process)
_fixture.Server
    .Given(Request.Create().WithPath("/v8/finance/chart/AAPL").UsingGet())
    .RespondWith(Response.Create().WithStatusCode(200).WithBody(chartJson));
```

With the Testcontainers admin client, use `IWireMockAdminApi`:

```csharp
// After (Testcontainers admin API)
var adminClient = _fixture.AdminClient;
await adminClient.PostMappingAsync(new MappingModel
{
    Request = new RequestModel
    {
        Path = new PathModel { Matchers = new[] { new MatcherModel { Name = "WildcardMatcher", Pattern = "/v8/finance/chart/AAPL*" } } },
        Methods = new[] { "GET" }
    },
    Response = new ResponseModel
    {
        StatusCode = 200,
        Body = chartJson,
        Headers = new Dictionary<string, object> { ["Content-Type"] = "application/json" }
    }
});
```

Alternatively, use the `WithMapping` builder on the container itself if the version supports it, or use the `CreateMapping` extension methods from `WireMock.Client.Extensions`.

### RC5. Update adapter wiring to point at container URL

**No change to the base-URI injection seam.** The `YahooFinanceRequestFactory` internal constructor already accepts custom base URIs. The only difference is the source of the URI:

```csharp
// Before
new Uri($"{_fixture.BaseUri}v8/finance/chart/")

// After (identical shape, different fixture property name)
new Uri($"{_fixture.BaseUri}v8/finance/chart/")
```

The `BaseUri` property exists on both fixtures. No production code change required beyond RC2 of the original spec (the internal constructor addition to `YahooFinanceRequestFactory`).

### RC6. Update feature spec sections

**File:** `docs/features/yahoo-finance-adapter.md`

Update the following sections:

| Section | Change |
|---------|--------|
| **Configuration > Package additions** | Replace `WireMock.Net` with `WireMock.Net.Testcontainers` |
| **Configuration > "Why WireMock.Net (in-process)"** | Remove. Replace with a note that the contract suite uses `WireMock.Net.Testcontainers` to run WireMock in a Docker container, consistent with Phase 11's Testcontainers-first approach. Docker is required. |
| **Configuration > Fixture location** | Rename `WireMockServerFixture.cs` to `WireMockContainerFixture.cs` |
| **Configuration > xUnit wiring** | Change from `IClassFixture` to `[Collection]` with `ICollectionFixture` |
| **Edge Cases > item 1** | Replace "port conflict" with "Docker not available" — same edge case as PostgreSQL Testcontainers |
| **Implementation Notes > §1** | Replace `WireMockServer.Start()` fixture with `WireMockContainerFixture` using `IAsyncLifetime` |
| **Implementation Notes > §3** | Update test class structure to `[Collection]` pattern and async stubs |
| **Implementation Notes > §6** | Update relationship to Phase 11 — both suites now use Testcontainers, sharing the Docker dependency |
| **Dependencies** | Replace `WireMock.Net NuGet package` with `WireMock.Net.Testcontainers NuGet package` |
| **Files > Modified** | `WireMockServerFixture.cs` → `WireMockContainerFixture.cs` |
| **Validation** | Add "Docker daemon must be running for contract tests" |

### RC7. Update `yahoo-finance-testcontainers.md` open questions

**File:** `docs/features/yahoo-finance-testcontainers.md`

The open question "Which mock server should be preferred: a generic `ContainerBuilder` with a plain image, or a package-specific helper if one is available and stable?" is now resolved: use `WireMock.Net.Testcontainers` with `WireMockContainerBuilder`.

## Affected Modules or Files

| File | Change Type |
|------|-------------|
| `tests/BlowingCandles.Infrastructure.Tests/BlowingCandles.Infrastructure.Tests.csproj` | Modify — swap `WireMock.Net` for `WireMock.Net.Testcontainers` |
| `tests/BlowingCandles.Infrastructure.Tests/Fixtures/WireMockContainerFixture.cs` | New or rename — Testcontainers-based fixture replacing in-process fixture |
| `tests/BlowingCandles.Infrastructure.Tests/YahooFinanceContractTests.cs` | Modify — `[Collection]` wiring, async stubs, async reset |
| `docs/features/yahoo-finance-adapter.md` | Modify — update per RC6 |
| `docs/features/yahoo-finance-testcontainers.md` | Modify — resolve open question per RC7 |

No production code changes. The `YahooFinanceRequestFactory` internal constructor (base-URI injection seam) from the original spec is still required and unchanged.

## Edge Cases to Address

1. **Docker not available.** Same as PostgreSQL Testcontainers — all container-dependent tests fail with a clear Testcontainers error. Non-container tests are unaffected. Both PostgreSQL and WireMock containers share this dependency, so CI must have Docker regardless.

2. **Container startup time.** WireMock container starts in ~2–4 seconds. Using `[Collection]` + `ICollectionFixture` ensures one container per test run, not per test class. Acceptable overhead.

3. **Admin API latency.** Stub registration via the admin REST API adds ~5–15ms per call compared to in-process fluent API. Negligible for a test suite with ~17 scenarios.

4. **WireMock image version pinning.** Pin `wiremock/wiremock:latest` to a specific tag (e.g., `wiremock/wiremock:3.x.x`) in the fixture to avoid CI flakiness from upstream image changes.

5. **Async test migration.** All contract test methods must return `async Task` instead of `void`. Assertion logic is unchanged — only stub setup and reset become async.

## Test Updates Required

No test scenarios are added or removed. All 17 scenarios from the original spec remain identical. The only change is mechanical: synchronous stub setup becomes async, and `[Collection]` replaces `IClassFixture`.
