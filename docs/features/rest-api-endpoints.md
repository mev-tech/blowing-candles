# Feature: REST API Endpoints

## Feature Name

rest-api-endpoints

## Purpose

Expose the latest generated trading signals over HTTP via a minimal ASP.NET Core Web API. The API is a thin read layer over the existing `signals.json` output file produced by the signal pipeline. It enables programmatic signal retrieval without parsing files directly, supporting local and operator use cases.

## Inputs

| Input | Source | Format | Required |
|-------|--------|--------|----------|
| `signals.json` | Filesystem (path from `AppConfig.Output.JsonFile`) | JSON array of `FinalSignal` objects with plain enum strings | Yes |
| `config.yaml` | Filesystem | YAML configuration | Yes |

## Outputs

| Output | Format | Destination |
|--------|--------|-------------|
| `GET /api/signals` response | JSON array of signal objects | HTTP response body |
| `GET /api/signals/{ticker}` response | JSON signal object or 404 | HTTP response body |

### Response schema (`GET /api/signals`)

```json
[
  {
    "ticker": "AAPL",
    "action": "BUY",
    "newsState": "TRADE_OK",
    "marketAction": "BUY",
    "reason": "",
    "timestamp": "2026-03-09T14:30:00+00:00"
  }
]
```

### Response schema (`GET /api/signals/{ticker}`)

Returns a single signal object matching the schema above, or HTTP 404 with:

```json
{
  "error": "Ticker not found",
  "ticker": "XYZ"
}
```

## Configuration

### New project

`src/BlowingCandles.Api/BlowingCandles.Api.csproj` — minimal ASP.NET Core Web API project targeting .NET 8.

### NuGet dependencies

| Package | Purpose |
|---------|---------|
| `Microsoft.AspNetCore.App` (framework reference) | Web API hosting |

### Startup configuration

- Reads `config.yaml` via `YamlConfigLoader` to resolve the `signals.json` path.
- No authentication or authorization middleware.
- Listens on `http://localhost:5000` by default (configurable via standard ASP.NET Core `--urls` or `ASPNETCORE_URLS`).
- Uses plain enum serialization consistent with `signals.json` (`"BUY"`, not `"Action.BUY"`).

### Solution file

Add `BlowingCandles.Api` to `BlowingCandles.sln`.

### Test project

`tests/BlowingCandles.Api.Tests/BlowingCandles.Api.Tests.csproj` — xUnit project with `Microsoft.AspNetCore.Mvc.Testing` for integration tests.

## Edge Cases

1. **`signals.json` does not exist.** Return HTTP 503 with `{ "error": "Signals file not found. Run the signal pipeline first." }`.

2. **`signals.json` is empty or contains an empty array.** Return HTTP 200 with an empty JSON array `[]`.

3. **`signals.json` contains malformed JSON.** Return HTTP 503 with `{ "error": "Signals file is corrupt." }`.

4. **Ticker not found in signals.** `GET /api/signals/{ticker}` returns HTTP 404 with error body.

5. **Ticker case insensitivity.** `GET /api/signals/aapl` matches `"AAPL"` in the signals file. Ticker lookup is case-insensitive.

6. **Concurrent file writes.** The pipeline may write `signals.json` while the API reads it. Use a single `File.ReadAllText` call to read atomically. If the read fails due to a concurrent write, return HTTP 503.

7. **Stale signals.** The API returns whatever is in the file. It does not validate freshness. Consumers are responsible for interpreting the `timestamp` field.

## Implementation Notes

### 1. Project structure

```
src/BlowingCandles.Api/
├── BlowingCandles.Api.csproj
├── Program.cs
├── Services/
│   └── SignalsFileReader.cs
└── Properties/
    └── launchSettings.json

tests/BlowingCandles.Api.Tests/
├── BlowingCandles.Api.Tests.csproj
└── SignalsEndpointTests.cs
```

### 2. Signal file reader

`SignalsFileReader` encapsulates reading and deserializing `signals.json`. It is registered as a scoped service so it reads the file on each request (no caching, always fresh).

```csharp
public class SignalsFileReader
{
    private readonly string _signalsJsonPath;

    public SignalsFileReader(string signalsJsonPath)
    {
        _signalsJsonPath = signalsJsonPath;
    }

    public List<FinalSignal>? ReadSignals()
    {
        if (!File.Exists(_signalsJsonPath))
            return null;

        var json = File.ReadAllText(_signalsJsonPath);
        return JsonSerializer.Deserialize<List<FinalSignal>>(json, options);
    }
}
```

### 3. Endpoint registration

Use minimal APIs for conciseness:

```csharp
app.MapGet("/api/signals", (SignalsFileReader reader) => { ... });
app.MapGet("/api/signals/{ticker}", (string ticker, SignalsFileReader reader) => { ... });
```

### 4. JSON serialization

Use `System.Text.Json` with the same `JsonSerializerOptions` as `OutputRenderer` for plain enum strings. Reuse the existing `JsonStringEnumConverter` configuration.

### 5. Integration tests

Use `WebApplicationFactory<Program>` from `Microsoft.AspNetCore.Mvc.Testing`. Tests write a temporary `signals.json` file and configure the app to read from it.

### 6. Docker

Update the `Dockerfile` to optionally expose the API port. The existing CLI entrypoint remains the default; the API is an alternative entrypoint.

## Test Scenarios

| Scenario | Method | Expected Outcome |
|----------|--------|-----------------|
| Signals file exists with valid data | `GET /api/signals` | HTTP 200, JSON array matching file contents |
| Signals file exists with valid data | `GET /api/signals/AAPL` | HTTP 200, single signal object for AAPL |
| Signals file exists, ticker not found | `GET /api/signals/XYZ` | HTTP 404, error body with ticker |
| Signals file does not exist | `GET /api/signals` | HTTP 503, error message |
| Signals file does not exist | `GET /api/signals/AAPL` | HTTP 503, error message |
| Signals file is empty array | `GET /api/signals` | HTTP 200, empty JSON array `[]` |
| Signals file is empty array | `GET /api/signals/AAPL` | HTTP 404, error body |
| Signals file is malformed JSON | `GET /api/signals` | HTTP 503, error message |
| Ticker lookup is case-insensitive | `GET /api/signals/aapl` | HTTP 200, returns AAPL signal |
| Multiple signals returned in order | `GET /api/signals` | HTTP 200, signals in same order as file |
