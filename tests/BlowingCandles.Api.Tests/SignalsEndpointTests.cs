using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BlowingCandles.Api;
using BlowingCandles.Domain.Enums;
using BlowingCandles.Infrastructure.Persistence;
using BlowingCandles.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TradingAction = BlowingCandles.Domain.Enums.Action;

namespace BlowingCandles.Api.Tests;

[Collection(ApiPostgresCollection.Name)]
public sealed class SignalsEndpointTests
{
    private readonly ApiPostgresFixture _fixture;

    public SignalsEndpointTests(ApiPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetSignals_ReturnsLatestLiveRunSignalsInTickerOrder()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        await SeedRunAsync(
            dbContext,
            SignalRunType.Realtime,
            false,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 8),
            new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 8, 20, 1, 0, TimeSpan.Zero),
            "MSFT");
        await SeedRunAsync(
            dbContext,
            SignalRunType.AsOf,
            true,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 9),
            new DateTimeOffset(2026, 3, 9, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 9, 10, 1, 0, TimeSpan.Zero),
            "TSLA");
        await SeedRunAsync(
            dbContext,
            SignalRunType.Realtime,
            false,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 9),
            new DateTimeOffset(2026, 3, 9, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 9, 20, 2, 0, TimeSpan.Zero),
            "MSFT",
            "AAPL");

        using var workspace = new TestWorkspace();
        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"), _fixture.ConnectionString);

        var response = await host.Client.GetAsync("/api/signals");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var signals = document.RootElement;
        Assert.Equal(JsonValueKind.Array, signals.ValueKind);
        Assert.Equal(2, signals.GetArrayLength());
        Assert.Equal("AAPL", signals[0].GetProperty("ticker").GetString());
        Assert.Equal("MSFT", signals[1].GetProperty("ticker").GetString());
        Assert.Equal("BUY", signals[0].GetProperty("action").GetString());
        Assert.Equal("BUY", signals[1].GetProperty("action").GetString());
    }

    [Fact]
    public async Task GetSignal_ReturnsMatchingTickerCaseInsensitively()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        await SeedRunAsync(
            dbContext,
            SignalRunType.Realtime,
            false,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 9),
            new DateTimeOffset(2026, 3, 9, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 9, 20, 2, 0, TimeSpan.Zero),
            "AAPL");

        using var workspace = new TestWorkspace();
        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"), _fixture.ConnectionString);

        var response = await host.Client.GetAsync("/api/signals/aapl");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AAPL", document.RootElement.GetProperty("ticker").GetString());
        Assert.Equal("BUY", document.RootElement.GetProperty("action").GetString());
        Assert.Equal("TRADE_OK", document.RootElement.GetProperty("newsState").GetString());
        Assert.Equal("BUY", document.RootElement.GetProperty("marketAction").GetString());
    }

    [Fact]
    public async Task NoCompletedLiveRuns_ReturnsEmptySignalsAndTicker404()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        await SeedRunAsync(
            dbContext,
            SignalRunType.AsOf,
            true,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 9),
            new DateTimeOffset(2026, 3, 9, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 9, 10, 1, 0, TimeSpan.Zero),
            "AAPL");
        await SeedRunAsync(
            dbContext,
            SignalRunType.Realtime,
            false,
            SignalRunStatus.Failed,
            new DateOnly(2026, 3, 9),
            new DateTimeOffset(2026, 3, 9, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 9, 20, 1, 0, TimeSpan.Zero));

        using var workspace = new TestWorkspace();
        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"), _fixture.ConnectionString);

        var listResponse = await host.Client.GetAsync("/api/signals");
        var tickerResponse = await host.Client.GetAsync("/api/signals/AAPL");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using (var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(0, listDocument.RootElement.GetArrayLength());
        }

        Assert.Equal(HttpStatusCode.NotFound, tickerResponse.StatusCode);
        using var tickerDocument = JsonDocument.Parse(await tickerResponse.Content.ReadAsStringAsync());
        Assert.Equal("Ticker not found", tickerDocument.RootElement.GetProperty("error").GetString());
        Assert.Equal("AAPL", tickerDocument.RootElement.GetProperty("ticker").GetString());
    }

    [Fact]
    public async Task GetRuns_ReturnsSummariesOrderedByStartedAtDescending_AndGetRunByIdReturnsSignals()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        var olderRunId = await SeedRunAsync(
            dbContext,
            SignalRunType.Realtime,
            false,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 8),
            new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 8, 20, 1, 0, TimeSpan.Zero),
            "AAPL");
        var newerRunId = await SeedRunAsync(
            dbContext,
            SignalRunType.AsOf,
            true,
            SignalRunStatus.Completed,
            new DateOnly(2026, 3, 9),
            new DateTimeOffset(2026, 3, 9, 21, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 9, 21, 1, 0, TimeSpan.Zero),
            "MSFT");

        using var workspace = new TestWorkspace();
        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"), _fixture.ConnectionString);

        var runsResponse = await host.Client.GetAsync("/api/runs");
        var runResponse = await host.Client.GetAsync($"/api/runs/{olderRunId}");
        var missingRunResponse = await host.Client.GetAsync($"/api/runs/{newerRunId + 1000}");

        Assert.Equal(HttpStatusCode.OK, runsResponse.StatusCode);
        using (var runsDocument = JsonDocument.Parse(await runsResponse.Content.ReadAsStringAsync()))
        {
            var runs = runsDocument.RootElement;
            Assert.Equal(2, runs.GetArrayLength());
            Assert.Equal("2026-03-09", runs[0].GetProperty("asOfDate").GetString());
            Assert.Equal("AsOf", runs[0].GetProperty("runType").GetString());
            Assert.False(runs[0].TryGetProperty("signals", out _));
            Assert.Equal("2026-03-08", runs[1].GetProperty("asOfDate").GetString());
            Assert.Equal("Realtime", runs[1].GetProperty("runType").GetString());
        }

        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);
        using (var runDocument = JsonDocument.Parse(await runResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Realtime", runDocument.RootElement.GetProperty("runType").GetString());
            var signals = runDocument.RootElement.GetProperty("signals");
            Assert.Single(signals.EnumerateArray());
            Assert.Equal("AAPL", signals[0].GetProperty("ticker").GetString());
            Assert.Equal("BUY", signals[0].GetProperty("action").GetString());
        }

        Assert.Equal(HttpStatusCode.NotFound, missingRunResponse.StatusCode);
        using var missingDocument = JsonDocument.Parse(await missingRunResponse.Content.ReadAsStringAsync());
        Assert.Equal("Run not found", missingDocument.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task PostRealtimeRun_ReturnsAcceptedAndPersistsCompletedRun()
    {
        await _fixture.ResetAsync();
        using var workspace = new TestWorkspace();
        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"), _fixture.ConnectionString);

        var response = await host.Client.PostAsync("/api/runs/realtime", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = document.RootElement.GetProperty("runId").GetInt64();
        Assert.True(runId > 0);
        Assert.Equal("Realtime", document.RootElement.GetProperty("runType").GetString());
        Assert.Equal("api", document.RootElement.GetProperty("trigger").GetString());
        Assert.Equal("Completed", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("tickerCount").GetInt32());
        Assert.Equal(0, document.RootElement.GetProperty("signals").GetArrayLength());

        await using var dbContext = _fixture.CreateDbContext();
        var persistedRun = await dbContext.SignalRuns.SingleAsync(run => run.Id == runId);
        Assert.Equal(SignalRunType.Realtime, persistedRun.RunType);
        Assert.Equal("api", persistedRun.Trigger);
        Assert.Equal(SignalRunStatus.Completed, persistedRun.Status);
    }

    [Fact]
    public async Task PostAsOfRun_WithMissingDate_Returns400()
    {
        await _fixture.ResetAsync();
        using var workspace = new TestWorkspace();
        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"), _fixture.ConnectionString);

        var response = await host.Client.PostAsync(
            "/api/runs/asof",
            JsonContent.Create(new { }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostAsOfRun_WithValidDate_ReturnsAccepted()
    {
        await _fixture.ResetAsync();
        using var workspace = new TestWorkspace();
        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"), _fixture.ConnectionString);

        var response = await host.Client.PostAsync(
            "/api/runs/asof",
            JsonContent.Create(new { asOfDate = "2026-03-01" }));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AsOf", document.RootElement.GetProperty("runType").GetString());
        Assert.Equal("2026-03-01", document.RootElement.GetProperty("asOfDate").GetString());
        Assert.True(document.RootElement.GetProperty("isSimulation").GetBoolean());
    }

    [Fact]
    public async Task PostRangeRun_WithInvalidRange_Returns400()
    {
        await _fixture.ResetAsync();
        using var workspace = new TestWorkspace();
        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"), _fixture.ConnectionString);

        var response = await host.Client.PostAsync(
            "/api/runs/range",
            JsonContent.Create(new { startDate = "2026-03-05", endDate = "2026-03-01" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostRangeRun_WithValidRange_ReturnsAcceptedArray()
    {
        await _fixture.ResetAsync();
        using var workspace = new TestWorkspace();
        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"), _fixture.ConnectionString);

        var response = await host.Client.PostAsync(
            "/api/runs/range",
            JsonContent.Create(new { startDate = "2026-03-01", endDate = "2026-03-03" }));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(3, document.RootElement.GetArrayLength());
        Assert.All(document.RootElement.EnumerateArray(), result =>
        {
            Assert.Equal("Range", result.GetProperty("runType").GetString());
            Assert.Equal("Completed", result.GetProperty("status").GetString());
        });
    }

    [Fact]
    public async Task HealthEndpoints_ReportHealthyWhenDatabaseIsAvailable()
    {
        await _fixture.ResetAsync();
        using var workspace = new TestWorkspace();
        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"), _fixture.ConnectionString);

        var liveResponse = await host.Client.GetAsync("/health/live");
        var readyResponse = await host.Client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);

        using (var liveDocument = JsonDocument.Parse(await liveResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Healthy", liveDocument.RootElement.GetProperty("status").GetString());
        }

        using var readyDocument = JsonDocument.Parse(await readyResponse.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", readyDocument.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("/api/signals")]
    [InlineData("/api/signals/AAPL")]
    [InlineData("/api/runs")]
    [InlineData("/api/runs/1")]
    public async Task ReadEndpoints_Return503WhenDatabaseIsUnavailable(string path)
    {
        using var workspace = new TestWorkspace();
        await using var host = await ApiTestHost.StartAsync(
            workspace.GetPath("config.yaml"),
            "Host=127.0.0.1;Port=1;Database=missing;Username=postgres;Password=postgres");

        var response = await host.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Signal run data is unavailable. Try again.", document.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("/api/runs/realtime", null)]
    [InlineData("/api/runs/asof", "{ \"asOfDate\": \"2026-03-01\" }")]
    [InlineData("/api/runs/range", "{ \"startDate\": \"2026-03-01\", \"endDate\": \"2026-03-03\" }")]
    public async Task PostEndpoints_Return503WhenDatabaseIsUnavailable(string path, string? jsonBody)
    {
        using var workspace = new TestWorkspace();
        await using var host = await ApiTestHost.StartAsync(
            workspace.GetPath("config.yaml"),
            "Host=127.0.0.1;Port=1;Database=missing;Username=postgres;Password=postgres");

        using var response = await host.Client.PostAsync(path, CreateJsonContent(jsonBody));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Signal run data is unavailable. Try again.", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetRun_WithNonNumericRunId_Returns404()
    {
        await _fixture.ResetAsync();
        using var workspace = new TestWorkspace();
        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"), _fixture.ConnectionString);

        var response = await host.Client.GetAsync("/api/runs/abc");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_Returns503WhenDatabaseIsUnavailable()
    {
        using var workspace = new TestWorkspace();
        await using var host = await ApiTestHost.StartAsync(
            workspace.GetPath("config.yaml"),
            "Host=127.0.0.1;Port=1;Database=missing;Username=postgres;Password=postgres");

        var response = await host.Client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
    }

    private sealed class ApiTestHost : IAsyncDisposable
    {
        private ApiTestHost(WebApplication app, HttpClient client)
        {
            App = app;
            Client = client;
        }

        private WebApplication App { get; }

        public HttpClient Client { get; }

        public static async Task<ApiTestHost> StartAsync(string configPath, string connectionString)
        {
            var app = ApiHost.Build(
                [],
                configPath,
                ["http://127.0.0.1:0"],
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:AppDb"] = connectionString
                });
            await app.StartAsync();

            var addresses = app.Services.GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()?
                .Addresses
                ?? throw new InvalidOperationException("No server addresses were registered.");

            var address = addresses.Single(candidate =>
                candidate.StartsWith("http://127.0.0.1:", StringComparison.Ordinal));

            return new ApiTestHost(app, new HttpClient { BaseAddress = new Uri(address) });
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"blowing-candles-api-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
            WriteFile("config.yaml",
                """
                watchlist: []
                news:
                  local_earnings_calendar: earnings_calendar.json
                audit:
                  jsonl_path: logs/decisions.jsonl
                """);
            WriteFile("earnings_calendar.json", "{}" + Environment.NewLine);
        }

        public string RootPath { get; }

        public string GetPath(string relativePath)
        {
            return Path.Combine(RootPath, relativePath);
        }

        public void WriteFile(string relativePath, string contents)
        {
            var path = GetPath(relativePath);
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private static HttpContent? CreateJsonContent(string? jsonBody)
    {
        return jsonBody is null
            ? null
            : new StringContent(jsonBody, Encoding.UTF8, "application/json");
    }

    private static async Task<long> SeedRunAsync(
        AppDbContext dbContext,
        SignalRunType runType,
        bool isSimulation,
        SignalRunStatus status,
        DateOnly asOfDate,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        params string[] tickers)
    {
        var run = new SignalRunEntity
        {
            RunType = runType,
            Trigger = "api",
            Status = status,
            AsOfDate = asOfDate,
            IsSimulation = isSimulation,
            TickerCount = tickers.Length,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            ErrorMessage = status == SignalRunStatus.Failed ? "failed" : null
        };

        foreach (var ticker in tickers)
        {
            run.Results.Add(
                new SignalRunResultEntity
                {
                    Run = run,
                    Ticker = ticker,
                    Action = TradingAction.BUY,
                    NewsState = NewsState.TRADE_OK,
                    MarketAction = TradingAction.BUY,
                    Reason = string.Empty,
                    Timestamp = completedAtUtc
                });
        }

        dbContext.SignalRuns.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    public sealed class ApiPostgresFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("blowing_candles_api_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        public string ConnectionString => _container.GetConnectionString();

        public async Task InitializeAsync()
        {
            await _container.StartAsync();

            await using var dbContext = CreateDbContext();
            await dbContext.Database.MigrateAsync();
        }

        public async Task DisposeAsync()
        {
            await _container.DisposeAsync();
        }

        public AppDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(ConnectionString)
                .Options;

            return new AppDbContext(options);
        }

        public async Task ResetAsync()
        {
            await using var dbContext = CreateDbContext();
            await dbContext.Database.ExecuteSqlRawAsync(
                "TRUNCATE TABLE signal_run, trade_governor_state, market_data_refresh_run RESTART IDENTITY CASCADE");
        }
    }

    public static class ApiPostgresCollection
    {
        public const string Name = "ApiPostgresCollection";
    }

    [CollectionDefinition(ApiPostgresCollection.Name)]
    public sealed class ApiPostgresCollectionDefinition : ICollectionFixture<ApiPostgresFixture>;
}
