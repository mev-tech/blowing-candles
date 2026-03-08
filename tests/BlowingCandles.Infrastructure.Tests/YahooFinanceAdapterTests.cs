using System.Net;
using System.Net.Http;
using BlowingCandles.Infrastructure.MarketData;

namespace BlowingCandles.Infrastructure.Tests;

public sealed class YahooFinanceAdapterTests
{
    [Fact]
    public void GetDailyPriceHistory_MapsBarsAndOrdersChronologically()
    {
        var transport = new RecordingTransport(_ => """
            {
              "chart": {
                "result": [
                  {
                    "timestamp": [1700003600, 1700000000],
                    "indicators": {
                      "quote": [
                        {
                          "open": [11.0, 10.0],
                          "high": [12.0, 11.0],
                          "low": [10.5, 9.5],
                          "close": [11.5, 10.5],
                          "volume": [2000, 1000]
                        }
                      ]
                    }
                  }
                ],
                "error": null
              }
            }
            """);
        var adapter = CreateAdapter(transport);

        var bars = adapter.GetDailyPriceHistory("aapl", new DateOnly(2026, 3, 7));

        Assert.Collection(
            bars,
            bar =>
            {
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), bar.Timestamp);
                Assert.Equal(TimeSpan.Zero, bar.Timestamp.Offset);
                Assert.Equal(10.0m, bar.Open);
                Assert.Equal(11.0m, bar.High);
                Assert.Equal(9.5m, bar.Low);
                Assert.Equal(10.5m, bar.Close);
                Assert.Equal(1000L, bar.Volume);
            },
            bar =>
            {
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700003600), bar.Timestamp);
                Assert.Equal(11.0m, bar.Open);
                Assert.Equal(12.0m, bar.High);
                Assert.Equal(10.5m, bar.Low);
                Assert.Equal(11.5m, bar.Close);
                Assert.Equal(2000L, bar.Volume);
            });
    }

    [Fact]
    public void GetDailyPriceHistory_EmptyResult_ReturnsEmptyList()
    {
        var transport = new RecordingTransport(_ => """
            {
              "chart": {
                "result": [],
                "error": null
              }
            }
            """);
        var adapter = CreateAdapter(transport);

        var bars = adapter.GetDailyPriceHistory("AAPL", new DateOnly(2026, 3, 7));

        Assert.Empty(bars);
    }

    [Fact]
    public void GetDailyPriceHistory_InvalidJson_ThrowsParsingException()
    {
        var transport = new RecordingTransport(_ => "{ definitely-not-json");
        var adapter = CreateAdapter(transport);

        var exception = Assert.Throws<YahooFinanceParsingException>(
            () => adapter.GetDailyPriceHistory("AAPL", new DateOnly(2026, 3, 7)));

        Assert.Contains("historical prices", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetDailyPriceHistory_FutureAsOfDate_ClampsRequestEndToCurrentUtc()
    {
        var nowUtc = new DateTimeOffset(2026, 3, 8, 12, 30, 0, TimeSpan.Zero);
        var transport = new RecordingTransport(_ => """
            {
              "chart": {
                "result": [],
                "error": null
              }
            }
            """);
        var adapter = CreateAdapter(transport, nowUtc);

        _ = adapter.GetDailyPriceHistory("AAPL", new DateOnly(2026, 3, 15));

        var request = Assert.Single(transport.Requests);
        Assert.Equal("historical prices", request.OperationName);
        Assert.Equal(nowUtc, request.EndExclusiveUtc);
        Assert.Equal(new DateTimeOffset(2025, 3, 8, 0, 0, 0, TimeSpan.Zero), request.StartUtc);
    }

    [Fact]
    public void GetDailyPriceHistory_PastWeekendAsOfDate_UsesNextUtcDayExclusiveBoundary()
    {
        var nowUtc = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var transport = new RecordingTransport(_ => """
            {
              "chart": {
                "result": [
                  {
                    "timestamp": [1772755200],
                    "indicators": {
                      "quote": [
                        {
                          "open": [25.0],
                          "high": [26.0],
                          "low": [24.5],
                          "close": [25.5],
                          "volume": [500]
                        }
                      ]
                    }
                  }
                ],
                "error": null
              }
            }
            """);
        var adapter = CreateAdapter(transport, nowUtc);

        var bars = adapter.GetDailyPriceHistory("AAPL", new DateOnly(2026, 3, 7));

        var request = Assert.Single(transport.Requests);
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero), request.EndExclusiveUtc);
        Assert.Single(bars);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1772755200), bars[0].Timestamp);
    }

    [Fact]
    public void GetNextEarningsDate_ReturnsNearestFutureDate()
    {
        var asOfUtc = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero);
        var transport = new RecordingTransport(_ => """
            {
              "quoteSummary": {
                "result": [
                  {
                    "calendarEvents": {
                      "earnings": {
                        "earningsDate": [
                          { "raw": 1773350400, "fmt": "2026-03-12" },
                          { "raw": 1774560000, "fmt": "2026-03-26" }
                        ]
                      }
                    }
                  }
                ],
                "error": null
              }
            }
            """);
        var adapter = CreateAdapter(transport);

        var nextEarningsDate = adapter.GetNextEarningsDate("AAPL", asOfUtc);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1773350400), nextEarningsDate);
    }

    [Fact]
    public void GetNextEarningsDate_DateOnlyString_IsTreatedAsUtc()
    {
        var transport = new RecordingTransport(_ => """
            {
              "quoteSummary": {
                "result": [
                  {
                    "calendarEvents": {
                      "earnings": {
                        "earningsDate": [
                          { "fmt": "2026-03-12" }
                        ]
                      }
                    }
                  }
                ],
                "error": null
              }
            }
            """);
        var adapter = CreateAdapter(transport);

        var nextEarningsDate = adapter.GetNextEarningsDate(
            "AAPL",
            new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 3, 12, 0, 0, 0, TimeSpan.Zero), nextEarningsDate);
    }

    [Fact]
    public void GetNextEarningsDate_NoUsableDate_ReturnsNull()
    {
        var transport = new RecordingTransport(_ => """
            {
              "quoteSummary": {
                "result": [
                  {
                    "calendarEvents": {}
                  }
                ],
                "error": null
              }
            }
            """);
        var adapter = CreateAdapter(transport);

        var nextEarningsDate = adapter.GetNextEarningsDate(
            "AAPL",
            new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

        Assert.Null(nextEarningsDate);
    }

    [Fact]
    public void GetNextEarningsDate_InvalidDate_ThrowsParsingException()
    {
        var transport = new RecordingTransport(_ => """
            {
              "quoteSummary": {
                "result": [
                  {
                    "calendarEvents": {
                      "earnings": {
                        "earningsDate": [
                          { "fmt": "not-a-date" }
                        ]
                      }
                    }
                  }
                ],
                "error": null
              }
            }
            """);
        var adapter = CreateAdapter(transport);

        var exception = Assert.Throws<YahooFinanceParsingException>(
            () => adapter.GetNextEarningsDate(
                "AAPL",
                new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero)));

        Assert.Contains("earnings calendar", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Transport_AddsJsonAndUserAgentHeaders()
    {
        HttpRequestMessage? capturedRequest = null;
        var httpClient = CreateHttpClient(requestMessage =>
        {
            capturedRequest = requestMessage;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        });
        var transport = new YahooFinanceHttpTransport(httpClient);

        var body = transport.GetString(new YahooFinanceRequest(
            "historical prices",
            new Uri("https://query1.finance.yahoo.com/v8/finance/chart/AAPL")));

        Assert.Equal("{}", body);
        Assert.NotNull(capturedRequest);
        Assert.Contains(capturedRequest!.Headers.Accept, value => value.MediaType == "application/json");
        Assert.NotEmpty(capturedRequest.Headers.UserAgent);
    }

    [Fact]
    public void Transport_UnauthorizedStatus_ThrowsAuthenticationException()
    {
        var httpClient = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            ReasonPhrase = "Unauthorized",
            Content = new StringContent(string.Empty)
        });
        var transport = new YahooFinanceHttpTransport(httpClient);

        var exception = Assert.Throws<YahooFinanceAuthenticationException>(
            () => transport.GetString(new YahooFinanceRequest(
                "historical prices",
                new Uri("https://query1.finance.yahoo.com/v8/finance/chart/AAPL"))));

        Assert.Contains("401", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Transport_TooManyRequests_ThrowsRateLimitException()
    {
        var httpClient = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            ReasonPhrase = "Too Many Requests",
            Content = new StringContent(string.Empty)
        });
        var transport = new YahooFinanceHttpTransport(httpClient);

        var exception = Assert.Throws<YahooFinanceRateLimitException>(
            () => transport.GetString(new YahooFinanceRequest(
                "historical prices",
                new Uri("https://query1.finance.yahoo.com/v8/finance/chart/AAPL"))));

        Assert.Contains("429", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Transport_RequestFailure_ThrowsTransportException()
    {
        var httpClient = CreateHttpClient(_ => throw new HttpRequestException("network down"));
        var transport = new YahooFinanceHttpTransport(httpClient);

        var exception = Assert.Throws<YahooFinanceTransportException>(
            () => transport.GetString(new YahooFinanceRequest(
                "historical prices",
                new Uri("https://query1.finance.yahoo.com/v8/finance/chart/AAPL"))));

        Assert.Contains("failed before a response was received", exception.Message, StringComparison.Ordinal);
    }

    private static YahooFinanceAdapter CreateAdapter(
        RecordingTransport transport,
        DateTimeOffset? nowUtc = null)
    {
        return new YahooFinanceAdapter(
            new YahooFinanceRequestFactory(),
            transport,
            new YahooFinanceResponseParser(),
            nowUtc is null
                ? TimeProvider.System
                : new StubTimeProvider(nowUtc.Value));
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        return new HttpClient(new StubHttpMessageHandler(handler))
        {
            BaseAddress = new Uri("https://query1.finance.yahoo.com")
        };
    }

    private sealed class RecordingTransport : IYahooFinanceTransport
    {
        private readonly Func<YahooFinanceRequest, string> _responseFactory;

        public RecordingTransport(Func<YahooFinanceRequest, string> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<YahooFinanceRequest> Requests { get; } = [];

        public string GetString(YahooFinanceRequest request)
        {
            Requests.Add(request);
            return _responseFactory(request);
        }
    }

    private sealed class StubTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public StubTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override HttpResponseMessage Send(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _handler(request);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
