using System.Globalization;
using System.Net;
using BlowingCandles.Infrastructure.MarketData;
using BlowingCandles.Infrastructure.Tests.Fixtures;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace BlowingCandles.Infrastructure.Tests;

public sealed class YahooFinanceContractTests : IClassFixture<WireMockServerFixture>
{
    private const string TransportUserAgent =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    private readonly WireMockServerFixture _fixture;

    public YahooFinanceContractTests(WireMockServerFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public void GetDailyPriceHistory_HappyPath_UsesExpectedRequestShapeAndMapsBars()
    {
        var nowUtc = new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);
        var asOfDate = new DateOnly(2026, 3, 7);
        StubHistoricalPrices(
            ticker: "AAPL",
            statusCode: HttpStatusCode.OK,
            body: ReadFixture("chart-aapl-success.json"),
            expectedStartUtc: CreateUtcStartOfDay(asOfDate.AddDays(-365)),
            expectedEndExclusiveUtc: CreateUtcStartOfDay(asOfDate.AddDays(1)));
        var adapter = CreateAdapter(nowUtc);

        var bars = adapter.GetDailyPriceHistory("AAPL", asOfDate);

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
    public void GetDailyPriceHistory_LowercaseTicker_IsNormalizedInRequestPath()
    {
        var nowUtc = new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);
        var asOfDate = new DateOnly(2026, 3, 7);
        StubHistoricalPrices(
            ticker: "AAPL",
            statusCode: HttpStatusCode.OK,
            body: ReadFixture("chart-aapl-success.json"),
            expectedStartUtc: CreateUtcStartOfDay(asOfDate.AddDays(-365)),
            expectedEndExclusiveUtc: CreateUtcStartOfDay(asOfDate.AddDays(1)));
        var adapter = CreateAdapter(nowUtc);

        var bars = adapter.GetDailyPriceHistory("aapl", asOfDate);

        Assert.Equal(2, bars.Count);
    }

    [Fact]
    public void GetDailyPriceHistory_FutureAsOfDate_ClampsPeriod2ToNowUtc()
    {
        var nowUtc = new DateTimeOffset(2026, 3, 8, 12, 30, 0, TimeSpan.Zero);
        var asOfDate = new DateOnly(2026, 3, 15);
        StubHistoricalPrices(
            ticker: "AAPL",
            statusCode: HttpStatusCode.OK,
            body: ReadFixture("chart-empty-result.json"),
            expectedStartUtc: CreateUtcStartOfDay(new DateOnly(2025, 3, 8)),
            expectedEndExclusiveUtc: nowUtc);
        var adapter = CreateAdapter(nowUtc);

        var bars = adapter.GetDailyPriceHistory("AAPL", asOfDate);

        Assert.Empty(bars);
    }

    [Fact]
    public void GetDailyPriceHistory_WeekendAsOfDate_UsesNextUtcDayBoundary()
    {
        var nowUtc = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var asOfDate = new DateOnly(2026, 3, 7);
        StubHistoricalPrices(
            ticker: "AAPL",
            statusCode: HttpStatusCode.OK,
            body: ReadFixture("chart-aapl-success.json"),
            expectedStartUtc: CreateUtcStartOfDay(asOfDate.AddDays(-365)),
            expectedEndExclusiveUtc: CreateUtcStartOfDay(asOfDate.AddDays(1)));
        var adapter = CreateAdapter(nowUtc);

        var bars = adapter.GetDailyPriceHistory("AAPL", asOfDate);

        Assert.Equal(2, bars.Count);
    }

    [Fact]
    public void GetDailyPriceHistory_EmptyChartResult_ReturnsEmptyList()
    {
        var nowUtc = new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);
        var asOfDate = new DateOnly(2026, 3, 7);
        StubHistoricalPrices(
            ticker: "AAPL",
            statusCode: HttpStatusCode.OK,
            body: ReadFixture("chart-empty-result.json"),
            expectedStartUtc: CreateUtcStartOfDay(asOfDate.AddDays(-365)),
            expectedEndExclusiveUtc: CreateUtcStartOfDay(asOfDate.AddDays(1)));
        var adapter = CreateAdapter(nowUtc);

        var bars = adapter.GetDailyPriceHistory("AAPL", asOfDate);

        Assert.Empty(bars);
    }

    [Fact]
    public void GetDailyPriceHistory_NullTimestamps_ReturnsEmptyList()
    {
        var nowUtc = new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);
        var asOfDate = new DateOnly(2026, 3, 7);
        StubHistoricalPrices(
            ticker: "AAPL",
            statusCode: HttpStatusCode.OK,
            body: """
                {
                  "chart": {
                    "result": [
                      {
                        "timestamp": null,
                        "indicators": {
                          "quote": [
                            {
                              "open": [10.0],
                              "high": [11.0],
                              "low": [9.5],
                              "close": [10.5],
                              "volume": [1000]
                            }
                          ]
                        }
                      }
                    ],
                    "error": null
                  }
                }
                """,
            expectedStartUtc: CreateUtcStartOfDay(asOfDate.AddDays(-365)),
            expectedEndExclusiveUtc: CreateUtcStartOfDay(asOfDate.AddDays(1)));
        var adapter = CreateAdapter(nowUtc);

        var bars = adapter.GetDailyPriceHistory("AAPL", asOfDate);

        Assert.Empty(bars);
    }

    [Fact]
    public void GetNextEarningsDate_HappyPath_ReturnsNearestFutureDate()
    {
        StubNextEarnings(
            ticker: "AAPL",
            statusCode: HttpStatusCode.OK,
            body: ReadFixture("earnings-aapl-success.json"));
        var adapter = CreateAdapter();

        var nextEarningsDate = adapter.GetNextEarningsDate(
            "AAPL",
            new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1773350400), nextEarningsDate);
    }

    [Fact]
    public void GetNextEarningsDate_DateOnlyFormat_ReturnsMidnightUtc()
    {
        StubNextEarnings(
            ticker: "AAPL",
            statusCode: HttpStatusCode.OK,
            body: """
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
        var adapter = CreateAdapter();

        var nextEarningsDate = adapter.GetNextEarningsDate(
            "AAPL",
            new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 3, 12, 0, 0, 0, TimeSpan.Zero), nextEarningsDate);
    }

    [Fact]
    public void GetNextEarningsDate_NoUsableDate_ReturnsNull()
    {
        StubNextEarnings(
            ticker: "AAPL",
            statusCode: HttpStatusCode.OK,
            body: ReadFixture("earnings-no-date.json"));
        var adapter = CreateAdapter();

        var nextEarningsDate = adapter.GetNextEarningsDate(
            "AAPL",
            new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

        Assert.Null(nextEarningsDate);
    }

    [Fact]
    public void GetNextEarningsDate_MalformedDate_ThrowsParsingException()
    {
        StubNextEarnings(
            ticker: "AAPL",
            statusCode: HttpStatusCode.OK,
            body: ReadFixture("earnings-malformed-date.json"));
        var adapter = CreateAdapter();

        var exception = Assert.Throws<YahooFinanceParsingException>(
            () => adapter.GetNextEarningsDate(
                "AAPL",
                new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero)));

        Assert.Contains("earnings calendar", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, typeof(YahooFinanceAuthenticationException), "401")]
    [InlineData(HttpStatusCode.Forbidden, typeof(YahooFinanceAuthenticationException), "403")]
    [InlineData(HttpStatusCode.TooManyRequests, typeof(YahooFinanceRateLimitException), "429")]
    [InlineData(HttpStatusCode.InternalServerError, typeof(YahooFinanceTransportException), "500")]
    public void GetDailyPriceHistory_HttpErrors_ThrowExpectedExceptions(
        HttpStatusCode statusCode,
        Type expectedExceptionType,
        string expectedMessageFragment)
    {
        var nowUtc = new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);
        var asOfDate = new DateOnly(2026, 3, 7);
        StubHistoricalPrices(
            ticker: "AAPL",
            statusCode: statusCode,
            body: "{}",
            expectedStartUtc: CreateUtcStartOfDay(asOfDate.AddDays(-365)),
            expectedEndExclusiveUtc: CreateUtcStartOfDay(asOfDate.AddDays(1)));
        var adapter = CreateAdapter(nowUtc);

        var exception = Assert.Throws(
            expectedExceptionType,
            () => adapter.GetDailyPriceHistory("AAPL", asOfDate));

        Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetDailyPriceHistory_MalformedJson_ThrowsParsingException()
    {
        var nowUtc = new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);
        var asOfDate = new DateOnly(2026, 3, 7);
        StubHistoricalPrices(
            ticker: "AAPL",
            statusCode: HttpStatusCode.OK,
            body: "{ not-json",
            expectedStartUtc: CreateUtcStartOfDay(asOfDate.AddDays(-365)),
            expectedEndExclusiveUtc: CreateUtcStartOfDay(asOfDate.AddDays(1)));
        var adapter = CreateAdapter(nowUtc);

        var exception = Assert.Throws<YahooFinanceParsingException>(
            () => adapter.GetDailyPriceHistory("AAPL", asOfDate));

        Assert.Contains("historical prices", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetDailyPriceHistory_YahooErrorObject_ThrowsTransportException()
    {
        var nowUtc = new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);
        var asOfDate = new DateOnly(2026, 3, 7);
        StubHistoricalPrices(
            ticker: "AAPL",
            statusCode: HttpStatusCode.OK,
            body: """
                {
                  "chart": {
                    "result": null,
                    "error": {
                      "code": "Not Found",
                      "description": "No data found, symbol may be delisted"
                    }
                  }
                }
                """,
            expectedStartUtc: CreateUtcStartOfDay(asOfDate.AddDays(-365)),
            expectedEndExclusiveUtc: CreateUtcStartOfDay(asOfDate.AddDays(1)));
        var adapter = CreateAdapter(nowUtc);

        var exception = Assert.Throws<YahooFinanceTransportException>(
            () => adapter.GetDailyPriceHistory("AAPL", asOfDate));

        Assert.Contains("No data found, symbol may be delisted", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetDailyPriceHistory_RequestIncludesRequiredHeaders()
    {
        var nowUtc = new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);
        var asOfDate = new DateOnly(2026, 3, 7);
        StubHistoricalPrices(
            ticker: "AAPL",
            statusCode: HttpStatusCode.OK,
            body: ReadFixture("chart-empty-result.json"),
            expectedStartUtc: CreateUtcStartOfDay(asOfDate.AddDays(-365)),
            expectedEndExclusiveUtc: CreateUtcStartOfDay(asOfDate.AddDays(1)));
        var adapter = CreateAdapter(nowUtc);

        var bars = adapter.GetDailyPriceHistory("AAPL", asOfDate);

        Assert.Empty(bars);
    }

    private YahooFinanceAdapter CreateAdapter(DateTimeOffset? nowUtc = null)
    {
        var requestFactory = new YahooFinanceRequestFactory(
            historicalPricesBaseUri: new Uri(_fixture.BaseUri, "v8/finance/chart/"),
            earningsBaseUri: new Uri(_fixture.BaseUri, "v10/finance/quoteSummary/"));
        var transport = new YahooFinanceHttpTransport(new HttpClient());

        return new YahooFinanceAdapter(
            requestFactory,
            transport,
            new YahooFinanceResponseParser(),
            nowUtc is null
                ? TimeProvider.System
                : new StubTimeProvider(nowUtc.Value));
    }

    private void StubHistoricalPrices(
        string ticker,
        HttpStatusCode statusCode,
        string body,
        DateTimeOffset expectedStartUtc,
        DateTimeOffset expectedEndExclusiveUtc)
    {
        _fixture.Server
            .Given(
                Request.Create()
                    .UsingGet()
                    .WithPath($"/v8/finance/chart/{ticker}")
                    .WithParam("interval", "1d")
                    .WithParam("includePrePost", "false")
                    .WithParam("events", "div,splits")
                    .WithParam("period1", ToUnixString(expectedStartUtc))
                    .WithParam("period2", ToUnixString(expectedEndExclusiveUtc))
                    .WithHeader("Accept", "application/json")
                    .WithHeader("User-Agent", TransportUserAgent))
            .RespondWith(
                Response.Create()
                    .WithStatusCode((int)statusCode)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(body));
    }

    private void StubNextEarnings(
        string ticker,
        HttpStatusCode statusCode,
        string body)
    {
        _fixture.Server
            .Given(
                Request.Create()
                    .UsingGet()
                    .WithPath($"/v10/finance/quoteSummary/{ticker}")
                    .WithParam("modules", "calendarEvents")
                    .WithParam("formatted", "false")
                    .WithHeader("Accept", "application/json")
                    .WithHeader("User-Agent", TransportUserAgent))
            .RespondWith(
                Response.Create()
                    .WithStatusCode((int)statusCode)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(body));
    }

    private static string ReadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "yahoo-finance", fileName);
        return File.ReadAllText(path);
    }

    private static string ToUnixString(DateTimeOffset value)
    {
        return value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset CreateUtcStartOfDay(DateOnly date)
    {
        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
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
}
