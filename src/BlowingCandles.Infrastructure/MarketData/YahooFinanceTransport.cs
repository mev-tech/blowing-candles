using System.Net;
using System.Net.Http.Headers;

namespace BlowingCandles.Infrastructure.MarketData;

internal interface IYahooFinanceTransport
{
    string GetString(YahooFinanceRequest request);
}

internal sealed class YahooFinanceHttpTransport : IYahooFinanceTransport
{
    private const string UserAgent =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private readonly HttpClient _httpClient;

    public YahooFinanceHttpTransport(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public string GetString(YahooFinanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = new HttpRequestMessage(HttpMethod.Get, request.Uri);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.UserAgent.ParseAdd(UserAgent);

        HttpResponseMessage response;

        try
        {
            response = _httpClient.Send(message);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            throw new YahooFinanceTransportException(
                $"Yahoo {request.OperationName} request failed before a response was received.",
                exception);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new YahooFinanceAuthenticationException(
                    $"Yahoo {request.OperationName} request was rejected with {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new YahooFinanceRateLimitException(
                    $"Yahoo {request.OperationName} request was rate-limited with {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new YahooFinanceTransportException(
                    $"Yahoo {request.OperationName} request returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            try
            {
                return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
            {
                throw new YahooFinanceTransportException(
                    $"Yahoo {request.OperationName} response body could not be read.",
                    exception);
            }
        }
    }

    private static HttpClient CreateHttpClient()
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }
}
