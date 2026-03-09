# Feature: Yahoo Finance Testcontainers Contract Suite

## Summary

Add a Testcontainers-backed mock HTTP server for Yahoo Finance integration tests. The suite should exercise the real `YahooFinanceHttpTransport`, `YahooFinanceRequestFactory`, `YahooFinanceResponseParser`, and `YahooFinanceAdapter` through an actual socket boundary using deterministic Yahoo-shaped responses. This closes the gap between pure fake-transport tests and later live Yahoo smoke validation, but it does not replace live validation against the real Yahoo endpoints.

## Python Reference Behavior

The Python implementation defines the behavioral contract that the C# adapter must preserve:

- Technical scoring downloads about one year of daily OHLCV history from Yahoo.
- Earnings fallback queries Yahoo only when a ticker is absent from the local earnings calendar.
- If Yahoo has no usable historical data, the scoring path returns `WAIT` with `MARKET_DATA_ERROR` only for true failure paths; legitimate empty data remains distinct from transport or parsing errors.
- If Yahoo has no usable earnings date, the earnings path returns `WAIT` with `DATA_UNAVAILABLE`; transport or parsing failures become `WAIT` with `DATA_ERROR`.
- Future or weekend as-of dates must still produce the latest valid trading data without inventing extra bars.

These are domain behaviors, not transport details. The Testcontainers suite validates that the C# HTTP integration preserves them when talking to a Yahoo-shaped HTTP server.

## Acceptance Criteria

- [ ] A shared xUnit fixture starts a mock HTTP server container and exposes its base URI to the Yahoo adapter tests.
- [ ] Tests drive the real HTTP transport over `HttpClient`; no fake `IYahooFinanceTransport` is used in the contract suite.
- [ ] Historical-price tests assert request path, query parameters, headers, chronological ordering, future-date clamping, and weekend boundary handling.
- [ ] Earnings-fallback tests assert local-calendar absence remains the only condition under which Yahoo is queried, and they cover success, no-date, and malformed-date responses.
- [ ] Error-path tests cover `401`/`403`, `429`, other non-success responses, connection failures, and malformed JSON payloads.
- [ ] The normal automated suite remains offline and deterministic; no live Yahoo dependency is introduced.
- [ ] Phase 9 remains open until a separate live Yahoo smoke validation passes against the future refresh workflow.

## Inputs

| Input | Source | Format | Required |
|-------|--------|--------|----------|
| Ticker | Test case | `string` | Yes |
| As-of date | Test case | `DateOnly` | For historical-price tests |
| As-of timestamp | Test case | `DateTimeOffset` | For earnings-fallback tests |
| Mock Yahoo responses | Checked-in fixtures or inline setup | JSON | Yes |
| Mock server base URI | Testcontainers fixture | `Uri` | Yes |

## Outputs

| Output | Format | Destination |
|--------|--------|-------------|
| Contract test results | xUnit output | CI / terminal |
| Captured request metadata | Mock-server logs or admin API | Test assertions |
| Deterministic adapter exceptions or parsed values | In-memory assertions | Test process |

## Domain Rules

- Local earnings calendar data keeps precedence over Yahoo fallback.
- Yahoo fallback is allowed only for tickers absent from the local calendar; expired local entries remain a calendar problem, not a Yahoo fallback case.
- Historical-price requests continue to target approximately 365 days of data ending at the clamped as-of boundary.
- Returned bars must stay in chronological order and preserve deliberate timestamp handling.
- The Testcontainers suite validates the adapter's HTTP contract only; it does not prove Yahoo production availability, auth-cookie behavior, or rate-limit tolerance in the wild.

## Error Handling

At the adapter boundary:

- `401` and `403` map to `YahooFinanceAuthenticationException`.
- `429` maps to `YahooFinanceRateLimitException`.
- Other non-success HTTP responses and connection failures map to `YahooFinanceTransportException`.
- Malformed or structurally invalid payloads map to `YahooFinanceParsingException`.

At the domain boundary:

- Technical scoring must still convert adapter failures to `WAIT / MARKET_DATA_ERROR`.
- Earnings fallback must still convert adapter failures to `WAIT / DATA_ERROR`.
- All error conditions remain fail-safe and must never produce `BUY` or `SELL`.

## Dependencies

- Existing Yahoo adapter seams in `src/BlowingCandles.Infrastructure/MarketData/`
- A test-only base-URI override for the request factory or transport so the adapter can target the mock container instead of live Yahoo
- Testcontainers test infrastructure already being introduced for PostgreSQL
- A mock HTTP server image such as WireMock or MockServer
- Checked-in Yahoo response fixtures for chart and earnings payloads
- Separate live Yahoo smoke validation; this feature is not sufficient to close Phase 9 on its own

## Test Scenarios

| Scenario | Input | Expected Output |
|----------|-------|-----------------|
| Historical prices happy path | Upper/lowercase ticker, valid chart JSON | Correct request shape; bars mapped and ordered chronologically |
| Future as-of date | `asOfDate > nowUtc` | Request end boundary clamped to `nowUtc` |
| Weekend as-of date | Saturday/Sunday date with previous Friday data | Request end boundary uses next UTC day exclusive rule; latest real bar returned |
| Empty chart result | Valid JSON with empty `result` | Empty history, no transport/parsing exception |
| Earnings happy path | Valid earnings JSON with future dates | Nearest future earnings date returned |
| Earnings no usable date | Valid JSON with missing/empty earnings data | `null` returned, no exception |
| Unauthorized response | HTTP `401` or `403` | `YahooFinanceAuthenticationException` |
| Rate-limited response | HTTP `429` | `YahooFinanceRateLimitException` |
| Other HTTP failure | HTTP `500` or similar | `YahooFinanceTransportException` |
| Malformed payload | Invalid JSON or schema mismatch | `YahooFinanceParsingException` |

## Open Questions

- Which mock server should be preferred: a generic `ContainerBuilder` with a plain image, or a package-specific helper if one is available and stable?
- Should request verification use the mock server's admin API, container logs, or both?
- Should a small set of real Yahoo payloads be captured once and checked in as fixtures for stronger schema realism?
- Should these tests run in the default CI path, or be isolated behind the same Docker-dependent category as PostgreSQL Testcontainers tests?

## Implementation Notes

- Keep the contract suite in `tests/BlowingCandles.Infrastructure.Tests/`; it belongs at the infrastructure boundary.
- Prefer a shared fixture plus checked-in JSON fixtures over embedding large payload literals in each test.
- The adapter will likely need a small test seam for base-address injection. Keep that seam internal and production-safe.
- Do not describe this feature as "testing Yahoo with Testcontainers." The container only emulates the HTTP contract; live Yahoo behavior still needs a separate smoke gate.
