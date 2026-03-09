using System.Net;
using System.Text.Json;
using BlowingCandles.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace BlowingCandles.Api.Tests;

public sealed class SignalsEndpointTests
{
    [Fact]
    public async Task GetSignals_ReturnsSignalsInFileOrder()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml",
            """
            watchlist: [AAPL, MSFT]
            output:
              json_file: signals.json
            """);
        workspace.WriteFile("signals.json",
            """
            [
              {
                "ticker": "MSFT",
                "action": "WAIT",
                "newsState": "TRADE_OK",
                "marketAction": "WAIT",
                "reason": "NO_SIGNAL",
                "timestamp": "2026-03-09T14:30:00+00:00"
              },
              {
                "ticker": "AAPL",
                "action": "BUY",
                "newsState": "TRADE_OK",
                "marketAction": "BUY",
                "reason": "SCORE_80",
                "timestamp": "2026-03-09T14:31:00+00:00"
              }
            ]
            """);

        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"));

        var response = await host.Client.GetAsync("/api/signals");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var signals = document.RootElement;
        Assert.Equal(JsonValueKind.Array, signals.ValueKind);
        Assert.Equal(2, signals.GetArrayLength());
        Assert.Equal("MSFT", signals[0].GetProperty("ticker").GetString());
        Assert.Equal("AAPL", signals[1].GetProperty("ticker").GetString());
        Assert.Equal("WAIT", signals[0].GetProperty("action").GetString());
        Assert.Equal("BUY", signals[1].GetProperty("action").GetString());
    }

    [Fact]
    public async Task GetSignal_ReturnsMatchingTickerCaseInsensitively()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml",
            """
            watchlist: [AAPL]
            output:
              json_file: signals.json
            """);
        workspace.WriteFile("signals.json",
            """
            [
              {
                "ticker": "AAPL",
                "action": "BUY",
                "newsState": "TRADE_OK",
                "marketAction": "BUY",
                "reason": "SCORE_80",
                "timestamp": "2026-03-09T14:30:00+00:00"
              }
            ]
            """);

        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"));

        var response = await host.Client.GetAsync("/api/signals/aapl");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AAPL", document.RootElement.GetProperty("ticker").GetString());
        Assert.Equal("BUY", document.RootElement.GetProperty("action").GetString());
        Assert.Equal("TRADE_OK", document.RootElement.GetProperty("newsState").GetString());
        Assert.Equal("BUY", document.RootElement.GetProperty("marketAction").GetString());
    }

    [Fact]
    public async Task GetSignal_UnknownTicker_Returns404()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml",
            """
            watchlist: [AAPL]
            output:
              json_file: signals.json
            """);
        workspace.WriteFile("signals.json",
            """
            [
              {
                "ticker": "AAPL",
                "action": "BUY",
                "newsState": "TRADE_OK",
                "marketAction": "BUY",
                "reason": "SCORE_80",
                "timestamp": "2026-03-09T14:30:00+00:00"
              }
            ]
            """);

        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"));

        var response = await host.Client.GetAsync("/api/signals/XYZ");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Ticker not found", document.RootElement.GetProperty("error").GetString());
        Assert.Equal("XYZ", document.RootElement.GetProperty("ticker").GetString());
    }

    [Theory]
    [InlineData("/api/signals")]
    [InlineData("/api/signals/AAPL")]
    public async Task MissingSignalsFile_Returns503(string path)
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml",
            """
            watchlist: [AAPL]
            output:
              json_file: signals.json
            """);

        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"));

        var response = await host.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "Signals file not found. Run the signal pipeline first.",
            document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task EmptySignalsArray_ReturnsEmptyListAndTicker404()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml",
            """
            watchlist: [AAPL]
            output:
              json_file: signals.json
            """);
        workspace.WriteFile("signals.json", "[]" + Environment.NewLine);

        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"));

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

    [Theory]
    [InlineData("/api/signals")]
    [InlineData("/api/signals/AAPL")]
    public async Task MalformedSignalsFile_Returns503(string path)
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("config.yaml",
            """
            watchlist: [AAPL]
            output:
              json_file: signals.json
            """);
        workspace.WriteFile("signals.json", "{ not json }");

        await using var host = await ApiTestHost.StartAsync(workspace.GetPath("config.yaml"));

        var response = await host.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Signals file is corrupt.", document.RootElement.GetProperty("error").GetString());
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

        public static async Task<ApiTestHost> StartAsync(string configPath)
        {
            var app = ApiHost.Build([], configPath, ["http://127.0.0.1:0"]);
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
}
