namespace BlowingCandles.Infrastructure.MarketData;

internal abstract class YahooFinanceException : Exception
{
    protected YahooFinanceException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

internal sealed class YahooFinanceAuthenticationException : YahooFinanceException
{
    public YahooFinanceAuthenticationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

internal sealed class YahooFinanceRateLimitException : YahooFinanceException
{
    public YahooFinanceRateLimitException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

internal sealed class YahooFinanceTransportException : YahooFinanceException
{
    public YahooFinanceTransportException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

internal sealed class YahooFinanceParsingException : YahooFinanceException
{
    public YahooFinanceParsingException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
