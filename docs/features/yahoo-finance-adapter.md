# Feature: Yahoo Finance Adapter — Testcontainers Contract Suite

## Feature Name

yahoo-finance-adapter

## Purpose

Exercise the real `YahooFinanceHttpTransport` through an actual HTTP socket boundary against a Testcontainers-managed WireMock server returning deterministic Yahoo-shaped responses. This closes the gap between the existing 12 offline tests (which use fake `IYahooFinanceTransport` implementations) and the future live Yahoo smoke validation, without requiring network access or a running Yahoo endpoint.

## Inputs

| Input | Source | Format | Required |
|-------|--------|--------|----------|
| Ticker | Test case | `string` | Yes |
| As-of date | Test case | `DateOnly` | For historical-price tests |
| As-of timestamp | Test case | `DateTimeOffset` | For earnings-fallback tests |
| Mock Yahoo responses | Checked-in JSON fixture files under `tests/fixtures/yahoo-finance/` | JSON | Yes |
| Mock server base URI | WireMock Testcontainers fixture | `Uri` | Yes |

## Outputs

| Output | Format | Destination |
|--------|--------|-------------|
| Contract test results | xUnit output | CI / terminal |
| Captured request metadata | WireMock stub matching / verification API | Test assertions |
| Deterministic adapter exceptions or parsed values | In-memory assertions | Test process |

## Configuration

### Package additions to `BlowingCandles.Infrastructure.Tests.csproj`

| Package | Action |
|---------|--------|
| `WireMock.Net` | Add — provides an in-process WireMock HTTP stub server; no Docker image or Testcontainers package needed |

### Why WireMock.Net (in-process) instead of a Testcontainers-managed WireMock image

The Yahoo contract suite needs a programmable HTTP server that can return deterministic responses and verify request shape. `WireMock.Net` runs in-process as a `WireMockServer.Start()` call — no Docker dependency, no container startup latency, no image pull. This keeps the Yahoo contract tests runnable in CI environments where Docker may not be available (unlike the PostgreSQL Testcontainers tests from Phase 11, which genuinely need a real database engine). The term "Testcontainers" in the original plan described the intent (exercising real HTTP transport against a mock server), not a hard requirement for the `Testcontainers` NuGet package.

### Adapter base-URI injection seam

`YahooFinanceRequestFactory` currently hardcodes `https://query1.finance.yahoo.com` as the base URI. The contract tests need to redirect requests to the WireMock server. Add an `internal` constructor parameter (or an `internal` base-URI property) to `YahooFinanceRequestFactory` so tests can inject the mock server's base address. The production parameterless constructor continues to use the hardcoded Yahoo URI.

### Fixture location

- `tests/BlowingCandles.Infrastructure.Tests/Fixtures/WireMockServerFixture.cs` — shared xUnit class fixture that starts a `WireMockServer` on a random port
- `tests/fixtures/yahoo-finance/` — checked-in JSON fixture files for chart and earnings responses

### xUnit wiring

Tests use xUnit `IClassFixture<WireMockServerFixture>` to share a single WireMock server instance within the test class. The fixture resets all stub mappings between tests.

## Edge Cases

1. **WireMock port conflict.** `WireMockServer.Start()` uses a random available port. No fixed port assumption.

2. **Base-URI injection must not leak into production.** The `YahooFinanceRequestFactory` base-URI override is `internal` and only accessible from tests via `InternalsVisibleTo`. The public/production constructor remains unchanged.

3. **Request path matching.** WireMock stubs must match the exact URL path structure Yahoo uses (`/v8/finance/chart/{ticker}` and `/v10/finance/quoteSummary/{ticker}`) including query parameters. This validates that `YahooFinanceRequestFactory` builds correct URIs.

4. **Synchronous `HttpClient.Send`.** `YahooFinanceHttpTransport` calls `_httpClient.Send()` (synchronous). WireMock.Net supports both sync and async HTTP — no special handling needed.

5. **Parallel test isolation.** Each test resets WireMock stubs at the start. Tests within the same class run sequentially (xUnit default for non-parallel collections). No cross-test stub contamination.

6. **Existing offline tests remain unchanged.** The 12 existing `YahooFinanceAdapterTests` using `RecordingTransport` and `StubHttpMessageHandler` are not modified. The contract suite is a new test class that complements them.

## Implementation Notes

### 1. WireMock Server Fixture

```csharp
public sealed class WireMockServerFixture : IDisposable
{
    public WireMockServer Server { get; }
    public Uri BaseUri { get; }

    public WireMockServerFixture()
    {
        Server = WireMockServer.Start();
        BaseUri = new Uri(Server.Url!);
    }

    public void Reset() => Server.Reset();

    public void Dispose() => Server.Dispose();
}
```

### 2. Base-URI injection in YahooFinanceRequestFactory

Add an `internal` constructor that accepts base URIs:

```csharp
internal sealed class YahooFinanceRequestFactory
{
    private readonly Uri _historicalPricesBaseUri;
    private readonly Uri _earningsBaseUri;

    public YahooFinanceRequestFactory()
        : this(
            new Uri("https://query1.finance.yahoo.com/v8/finance/chart/"),
            new Uri("https://query1.finance.yahoo.com/v10/finance/quoteSummary/"))
    {
    }

    internal YahooFinanceRequestFactory(Uri historicalPricesBaseUri, Uri earningsBaseUri)
    {
        _historicalPricesBaseUri = historicalPricesBaseUri;
        _earningsBaseUri = earningsBaseUri;
    }

    // ... existing methods use _historicalPricesBaseUri and _earningsBaseUri
    //     instead of the static readonly fields
}
```

### 3. Contract test class structure

```csharp
public sealed class YahooFinanceContractTests : IClassFixture<WireMockServerFixture>
{
    private readonly WireMockServerFixture _fixture;

    public YahooFinanceContractTests(WireMockServerFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private YahooFinanceAdapter CreateAdapterPointingAtMock(DateTimeOffset? nowUtc = null)
    {
        var requestFactory = new YahooFinanceRequestFactory(
            new Uri($"{_fixture.BaseUri}v8/finance/chart/"),
            new Uri($"{_fixture.BaseUri}v10/finance/quoteSummary/"));
        var httpClient = new HttpClient { BaseAddress = _fixture.BaseUri };
        var transport = new YahooFinanceHttpTransport(httpClient);
        var timeProvider = nowUtc is null
            ? TimeProvider.System
            : new StubTimeProvider(nowUtc.Value);
        return new YahooFinanceAdapter(requestFactory, transport, new YahooFinanceResponseParser(), timeProvider);
    }
}
```

### 4. Checked-in fixture files

Place representative Yahoo JSON payloads under `tests/fixtures/yahoo-finance/`:

- `chart-aapl-success.json` — valid chart response with multiple bars
- `chart-empty-result.json` — valid chart response with empty `result` array
- `earnings-aapl-success.json` — valid earnings response with future dates
- `earnings-no-date.json` — valid earnings response with no usable date
- `earnings-malformed-date.json` — earnings response with unparseable date field

These files are loaded at test time and registered as WireMock stubs.

### 5. Relationship to Phase 10 (Market Data Persistence)

Phase 10 is complete. The Yahoo adapter is the upstream data source for the refresh workflow that persists snapshots. This contract suite validates that the adapter correctly shapes requests and parses responses, which is a prerequisite for the refresh workflow to produce valid snapshots. The contract suite does not directly interact with PostgreSQL or persistence services.

### 6. Relationship to Phase 11 (PostgreSQL Testcontainers)

Phase 11 introduces `Testcontainers.PostgreSql` for real-database integration tests. The Yahoo contract suite uses `WireMock.Net` (in-process) instead, because it needs a programmable HTTP stub, not a database. Both test suites can coexist in `BlowingCandles.Infrastructure.Tests` without conflict. If Phase 11 lands first, the Yahoo contract suite simply adds `WireMock.Net` alongside the existing Testcontainers package.

### 7. What this does NOT prove

- Live Yahoo endpoint availability, auth-cookie behavior, or rate-limit tolerance.
- End-to-end refresh workflow producing a valid persisted snapshot with real Yahoo data.
- Phase 9 cannot close until those live validations also pass.

## Test Scenarios

| Scenario | Input | Expected Output |
|----------|-------|-----------------|
| Historical prices happy path | Ticker `AAPL`, valid chart JSON fixture, `200 OK` | Correct request path `/v8/finance/chart/AAPL` with `period1`, `period2`, `interval=1d` query params; bars mapped to `PriceBar[]` in chronological order |
| Ticker normalization in request | Ticker `aapl` (lowercase) | Request path uses `AAPL` (uppercase) |
| Future as-of date clamping | `asOfDate` in the future, `nowUtc` fixed | `period2` query param does not exceed `nowUtc` unix timestamp |
| Weekend as-of date | Saturday date with Friday trading data | `period2` uses next-UTC-day exclusive boundary; bars returned correctly |
| Empty chart result | Valid JSON with empty `result` array | Empty `PriceBar[]`, no exception |
| Null timestamps in chart | Valid chart JSON where `timestamp` property is null | Empty `PriceBar[]`, no exception |
| Earnings happy path | Valid earnings JSON with future `earningsDate` entries | Nearest future `DateTimeOffset` returned |
| Earnings date-only format | Earnings JSON with `"fmt": "2026-03-12"` only (no `raw`) | `DateTimeOffset` at midnight UTC for that date |
| Earnings no usable date | Valid earnings JSON with empty `calendarEvents` | `null` returned, no exception |
| Earnings malformed date | Earnings JSON with `"fmt": "not-a-date"` | `YahooFinanceParsingException` thrown |
| HTTP 401 Unauthorized | WireMock returns `401` | `YahooFinanceAuthenticationException` with `401` in message |
| HTTP 403 Forbidden | WireMock returns `403` | `YahooFinanceAuthenticationException` with `403` in message |
| HTTP 429 Too Many Requests | WireMock returns `429` | `YahooFinanceRateLimitException` with `429` in message |
| HTTP 500 Server Error | WireMock returns `500` | `YahooFinanceTransportException` |
| Malformed JSON body | WireMock returns `200` with `"{ not-json"` | `YahooFinanceParsingException` |
| Yahoo error object in response | Valid JSON with non-null `chart.error` object | `YahooFinanceTransportException` with error description |
| Request headers verification | Any successful request | WireMock verifies `Accept: application/json` and `User-Agent` headers present |

## Dependencies

- `YahooFinanceAdapter`, `YahooFinanceRequestFactory`, `YahooFinanceHttpTransport`, `YahooFinanceResponseParser` — existing adapter seams in `src/BlowingCandles.Infrastructure/MarketData/`
- `InternalsVisibleTo` from `BlowingCandles.Infrastructure` to `BlowingCandles.Infrastructure.Tests` — already configured
- `WireMock.Net` NuGet package (test-only)
- Checked-in Yahoo JSON fixture files

## Files

### New files

- `tests/BlowingCandles.Infrastructure.Tests/Fixtures/WireMockServerFixture.cs` — WireMock server lifecycle
- `tests/BlowingCandles.Infrastructure.Tests/YahooFinanceContractTests.cs` — contract test class
- `tests/fixtures/yahoo-finance/chart-aapl-success.json` — chart happy-path fixture
- `tests/fixtures/yahoo-finance/chart-empty-result.json` — empty chart fixture
- `tests/fixtures/yahoo-finance/earnings-aapl-success.json` — earnings happy-path fixture
- `tests/fixtures/yahoo-finance/earnings-no-date.json` — no-earnings-date fixture
- `tests/fixtures/yahoo-finance/earnings-malformed-date.json` — bad-date fixture

### Modified files

- `src/BlowingCandles.Infrastructure/MarketData/YahooFinanceRequestFactory.cs` — add `internal` constructor for base-URI injection
- `tests/BlowingCandles.Infrastructure.Tests/BlowingCandles.Infrastructure.Tests.csproj` — add `WireMock.Net` package reference

### Unchanged files

- `tests/BlowingCandles.Infrastructure.Tests/YahooFinanceAdapterTests.cs` — existing offline tests remain as-is

## Validation

- `dotnet build BlowingCandles.sln` succeeds with zero warnings.
- All 12 existing `YahooFinanceAdapterTests` continue to pass unchanged.
- All new `YahooFinanceContractTests` pass without network access.
- All existing Domain, Application, Infrastructure, CrossValidation, and persistence tests pass.
- WireMock stubs verify correct request paths, query parameters, and headers.
- The contract suite exercises the real `YahooFinanceHttpTransport` (real `HttpClient` over real TCP) — no fake transport.

## Review

**Reviewed:** 2026-03-09
**Verdict:** OK

### Architecture Compliance

The implementation correctly follows the layered architecture. `YahooFinanceAdapter` is in Infrastructure and implements `IMarketDataProvider` from Domain. Internal seams (`YahooFinanceRequestFactory`, `IYahooFinanceTransport`, `YahooFinanceResponseParser`) are `internal`, not leaking to other layers. The base-URI injection constructor is `internal` and accessible via `InternalsVisibleTo`. No DI container — manual constructor wiring. Synchronous `HttpClient.Send()` matches the synchronous CLI architecture. Adapter throws typed exceptions which calling domain services catch and convert to WAIT, preserving the fail-safe invariant.

### Behavior vs Specification

All 17 test scenarios from the spec are implemented and pass. All 5 fixture JSON files are present. The `WireMockServerFixture`, base-URI injection seam, and contract test class structure all match the spec. Fixture files are correctly linked into the test project via `CopyToOutputDirectory`.

### Missing Edge Cases

The `RequestIncludesRequiredHeaders` test implicitly verifies headers via WireMock stub matching (if headers are wrong, the stub won't match and the request fails). The test body only asserts `Assert.Empty(bars)`, which reads as a "returns empty" test rather than a "headers present" test. Not a correctness issue since the stub's `WithHeader` matchers enforce the contract, but the test name is slightly misleading. Not blocking.

### Unnecessary Complexity

None found. Three focused internal classes behind one public adapter. No retry logic, no caching, no over-abstraction.

### Deferred Fixes (see `docs/reviews/yahoo-finance-adapter-testcontainers-fixes.md`)

A fixes document proposes migrating from `WireMock.Net` (in-process) to `WireMock.Net.Testcontainers` (Docker container) for consistency with Phase 11's Testcontainers-first approach. **All fixes are deferred to Phase 11.** The current in-process WireMock implementation is correct, exercises real HTTP transport over real TCP, and meets the stated contract suite goal. The migration is a consistency preference, not a correctness fix. See the fixes document for rationale.
