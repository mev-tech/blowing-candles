using BlowingCandles.Application;
using BlowingCandles.Api.Services;
using BlowingCandles.Infrastructure.Config;
using Microsoft.AspNetCore.Hosting;

namespace BlowingCandles.Api;

public static class ApiHost
{
    private const string DefaultConfigPath = "config.yaml";
    private const string DefaultUrl = "http://localhost:5000";

    public static WebApplication Build(string[] args, string? configPath = null, string[]? urls = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        ConfigureUrls(builder, urls);

        var appConfig = new YamlConfigLoader().Load(configPath ?? DefaultConfigPath);
        var signalsFileReader = new SignalsFileReader(appConfig.Output.JsonFile);

        var app = builder.Build();

        app.MapGet("/api/signals", () => CreateSignalsResponse(signalsFileReader.ReadSignals()));
        app.MapGet("/api/signals/{ticker}", (string ticker) => CreateSignalResponse(ticker, signalsFileReader.ReadSignals()));

        return app;
    }

    private static void ConfigureUrls(WebApplicationBuilder builder, string[]? urls)
    {
        if (urls is { Length: > 0 })
        {
            builder.WebHost.UseUrls(urls);
            return;
        }

        if (string.IsNullOrWhiteSpace(builder.Configuration[WebHostDefaults.ServerUrlsKey]))
        {
            builder.WebHost.UseUrls(DefaultUrl);
        }
    }

    private static IResult CreateSignalsResponse(SignalsFileReadResult result)
    {
        return result.Status switch
        {
            SignalsFileReadStatus.Success => Results.Json(result.Signals, SignalsJsonSerializer.JsonOptions),
            SignalsFileReadStatus.NotFound => CreateServiceUnavailableResponse("Signals file not found. Run the signal pipeline first."),
            SignalsFileReadStatus.Corrupt => CreateServiceUnavailableResponse("Signals file is corrupt."),
            SignalsFileReadStatus.Unavailable => CreateServiceUnavailableResponse("Signals file is unavailable. Try again."),
            _ => throw new InvalidOperationException($"Unsupported signals read status '{result.Status}'.")
        };
    }

    private static IResult CreateSignalResponse(string ticker, SignalsFileReadResult result)
    {
        return result.Status switch
        {
            SignalsFileReadStatus.Success => CreateTickerResponse(ticker, result.Signals),
            SignalsFileReadStatus.NotFound => CreateServiceUnavailableResponse("Signals file not found. Run the signal pipeline first."),
            SignalsFileReadStatus.Corrupt => CreateServiceUnavailableResponse("Signals file is corrupt."),
            SignalsFileReadStatus.Unavailable => CreateServiceUnavailableResponse("Signals file is unavailable. Try again."),
            _ => throw new InvalidOperationException($"Unsupported signals read status '{result.Status}'.")
        };
    }

    private static IResult CreateTickerResponse(string ticker, IReadOnlyList<SignalFileEntry> signals)
    {
        var signal = signals.FirstOrDefault(candidate =>
            string.Equals(candidate.Ticker, ticker, StringComparison.OrdinalIgnoreCase));

        return signal is null
            ? Results.Json(
                new { error = "Ticker not found", ticker },
                SignalsJsonSerializer.JsonOptions,
                statusCode: StatusCodes.Status404NotFound)
            : Results.Json(signal, SignalsJsonSerializer.JsonOptions);
    }

    private static IResult CreateServiceUnavailableResponse(string error)
    {
        return Results.Json(
            new { error },
            SignalsJsonSerializer.JsonOptions,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
