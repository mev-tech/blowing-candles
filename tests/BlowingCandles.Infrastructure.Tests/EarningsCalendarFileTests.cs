using BlowingCandles.Infrastructure.Calendar;

namespace BlowingCandles.Infrastructure.Tests;

public sealed class EarningsCalendarFileTests
{
    [Fact]
    public void Load_ParsesSingleStringsDeduplicatesDatesAndSkipsInvalidEntries()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("earnings_calendar.json", """
{
  "aapl": [
    "2026-05-07T20:00:00Z",
    "2026-05-07T20:00:00Z",
    "not-a-date",
    "2026-04-01T20:00:00Z"
  ],
  "msft": "2026-04-28T20:00:00Z",
  "tsla": [],
  "goog": ["bad-date"]
}
""");

        var calendar = new EarningsCalendarFile(workspace.GetPath("earnings_calendar.json"));

        var loaded = calendar.Load();

        Assert.Equal(["AAPL", "MSFT"], loaded.Keys.OrderBy(ticker => ticker, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            [
                new DateTimeOffset(2026, 4, 1, 20, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 7, 20, 0, 0, TimeSpan.Zero)
            ],
            loaded["AAPL"]);
        Assert.Equal(
            new DateTimeOffset(2026, 4, 1, 20, 0, 0, TimeSpan.Zero),
            calendar.GetNextFutureEarningsDate("aapl", new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero)));
        Assert.Null(calendar.GetNextFutureEarningsDate("MSFT", new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Load_ThrowsWhenJsonIsInvalid()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("earnings_calendar.json", "{ invalid json");

        var calendar = new EarningsCalendarFile(workspace.GetPath("earnings_calendar.json"));

        Assert.Throws<InvalidDataException>(() => calendar.Load());
    }
}
