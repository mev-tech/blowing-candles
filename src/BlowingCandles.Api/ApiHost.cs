using BlowingCandles.Application;
using BlowingCandles.Application.Services;
using BlowingCandles.Api.Workers;
using BlowingCandles.Infrastructure.Config;
using BlowingCandles.Infrastructure.Persistence;
using BlowingCandles.Infrastructure.Persistence.Models;
using BlowingCandles.Infrastructure.Persistence.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BlowingCandles.Api;

public static class ApiHost
{
    private const string DefaultConfigPath = "config.yaml";
    private const string DefaultUrl = "http://localhost:5000";
    private const string ApiTrigger = "api";
    private const string RunDataUnavailableError = "Signal run data is unavailable. Try again.";

    public static WebApplication Build(
        string[] args,
        string? configPath = null,
        string[]? urls = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        ConfigureUrls(builder, urls);
        ConfigureInfrastructureConfiguration(builder, configurationOverrides);

        var resolvedConfigPath = configPath ?? DefaultConfigPath;
        var appConfig = new YamlConfigLoader().Load(resolvedConfigPath);

        builder.Services.AddSingleton(appConfig);
        builder.Services.AddPersistence(builder.Configuration);
        builder.Services.Configure<WorkerOptions>(
            builder.Configuration.GetSection(WorkerOptions.SectionName));
        builder.Services.AddScoped<ISignalRunExecutionService>(serviceProvider =>
            CreateExecutionService(serviceProvider, appConfig));

        var workerOptions = builder.Configuration
            .GetSection(WorkerOptions.SectionName)
            .Get<WorkerOptions>()
            ?? new WorkerOptions();

        if (workerOptions.Enabled)
        {
            builder.Services.AddHostedService<SignalGenerationWorker>();
        }

        var app = builder.Build();

        app.MapGet("/api/signals", (SignalRunReadService readService) => CreateSignalsResponse(readService, app.Logger));
        app.MapGet(
            "/api/signals/{ticker}",
            (string ticker, SignalRunReadService readService) => CreateSignalResponse(ticker, readService, app.Logger));
        app.MapGet("/api/runs", (SignalRunReadService readService) => CreateRunsResponse(readService, app.Logger));
        app.MapGet(
            "/api/runs/{runId:long}",
            (long runId, SignalRunReadService readService) => CreateRunResponse(runId, readService, app.Logger));
        app.MapPost(
            "/api/runs/realtime",
            (ISignalRunExecutionService executionService) =>
                CreateRealtimeRunResponse(executionService, app.Logger));
        app.MapPost(
            "/api/runs/asof",
            (AsOfRunRequest? request, ISignalRunExecutionService executionService) =>
                CreateAsOfRunResponse(request, executionService, app.Logger));
        app.MapPost(
            "/api/runs/range",
            (RangeRunRequest? request, ISignalRunExecutionService executionService) =>
                CreateRangeRunResponse(request, executionService, app.Logger));
        app.MapGet(
            "/health/live",
            () => Results.Json(new StatusResponse("Healthy"), SignalsJsonSerializer.JsonOptions));
        app.MapGet("/health/ready", CreateReadinessResponse);

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

    private static void ConfigureInfrastructureConfiguration(
        WebApplicationBuilder builder,
        IReadOnlyDictionary<string, string?>? configurationOverrides)
    {
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddJsonFile(Path.Combine("src", "BlowingCandles.Api", "appsettings.json"), optional: true)
            .AddJsonFile(Path.Combine("src", "BlowingCandles.Api", "appsettings.Development.json"), optional: true)
            .AddJsonFile(Path.Combine("src", "BlowingCandles.Cli", "appsettings.json"), optional: true)
            .AddJsonFile(Path.Combine("src", "BlowingCandles.Cli", "appsettings.Development.json"), optional: true)
            .AddEnvironmentVariables();

        if (configurationOverrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(configurationOverrides);
        }
    }

    private static ISignalRunExecutionService CreateExecutionService(
        IServiceProvider serviceProvider,
        AppConfig appConfig)
    {
        var signalRunPersistence = serviceProvider.GetRequiredService<SignalRunPersistenceService>();
        var governorStateStoreFactory = serviceProvider.GetRequiredService<TradeGovernorDbStateStoreFactory>();
        var logger = serviceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("SignalRunExecutionService");

        return new SignalRunExecutionService(
            appConfig,
            signalRunPersistence.PersistRun,
            governorStateStoreFactory.Create,
            diagnosticWriter: message => logger.LogError("{Message}", message));
    }

    private static IResult CreateSignalsResponse(SignalRunReadService readService, ILogger logger)
    {
        try
        {
            var latestRun = readService.GetLatestLiveRun();
            var signals = latestRun?.Signals.Select(ToSignalFileEntry).ToArray() ?? [];
            return Results.Json(signals, SignalsJsonSerializer.JsonOptions);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Reading the latest live signal run failed.");
            return CreateServiceUnavailableResponse(RunDataUnavailableError);
        }
    }

    private static IResult CreateSignalResponse(string ticker, SignalRunReadService readService, ILogger logger)
    {
        try
        {
            var latestRun = readService.GetLatestLiveRun();
            var signal = latestRun?.Signals.FirstOrDefault(candidate =>
                string.Equals(candidate.Ticker, ticker, StringComparison.OrdinalIgnoreCase));

            return signal is null
                ? CreateTickerNotFoundResponse(ticker)
                : Results.Json(ToSignalFileEntry(signal), SignalsJsonSerializer.JsonOptions);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Reading the latest live signal run for ticker {Ticker} failed.", ticker);
            return CreateServiceUnavailableResponse(RunDataUnavailableError);
        }
    }

    private static IResult CreateRunsResponse(SignalRunReadService readService, ILogger logger)
    {
        try
        {
            var runs = readService.GetRecentRuns()
                .Select(ToRunSummaryResponse)
                .ToArray();

            return Results.Json(runs, SignalsJsonSerializer.JsonOptions);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Reading recent signal runs failed.");
            return CreateServiceUnavailableResponse(RunDataUnavailableError);
        }
    }

    private static IResult CreateRunResponse(long runId, SignalRunReadService readService, ILogger logger)
    {
        try
        {
            var run = readService.GetRunById(runId);
            return run is null
                ? CreateNotFoundResponse("Run not found")
                : Results.Json(run, SignalsJsonSerializer.JsonOptions);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Reading signal run {RunId} failed.", runId);
            return CreateServiceUnavailableResponse(RunDataUnavailableError);
        }
    }

    private static IResult CreateRealtimeRunResponse(
        ISignalRunExecutionService executionService,
        ILogger logger)
    {
        try
        {
            var result = executionService.RunRealtime(ApiTrigger);
            return CreateAcceptedRunResponse(result, logger, "realtime");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Starting a realtime signal run failed.");
            return CreateServiceUnavailableResponse(RunDataUnavailableError);
        }
    }

    private static IResult CreateAsOfRunResponse(
        AsOfRunRequest? request,
        ISignalRunExecutionService executionService,
        ILogger logger)
    {
        if (request?.AsOfDate is null)
        {
            return CreateBadRequestResponse("asOfDate is required.");
        }

        try
        {
            var result = executionService.RunAsOf(ApiTrigger, request.AsOfDate.Value);
            return CreateAcceptedRunResponse(result, logger, $"as-of {request.AsOfDate.Value:yyyy-MM-dd}");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Starting an as-of signal run for {AsOfDate} failed.", request.AsOfDate.Value);
            return CreateServiceUnavailableResponse(RunDataUnavailableError);
        }
    }

    private static IResult CreateRangeRunResponse(
        RangeRunRequest? request,
        ISignalRunExecutionService executionService,
        ILogger logger)
    {
        if (request?.StartDate is null || request.EndDate is null)
        {
            return CreateBadRequestResponse("startDate and endDate are required.");
        }

        if (request.EndDate.Value < request.StartDate.Value)
        {
            return CreateBadRequestResponse("startDate must be on or before endDate.");
        }

        try
        {
            var results = executionService.RunRange(ApiTrigger, request.StartDate.Value, request.EndDate.Value);
            return CreateAcceptedRangeRunResponse(
                results,
                logger,
                request.StartDate.Value,
                request.EndDate.Value);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Starting a range signal run for {StartDate} through {EndDate} failed.",
                request.StartDate.Value,
                request.EndDate.Value);
            return CreateServiceUnavailableResponse(RunDataUnavailableError);
        }
    }

    private static IResult CreateAcceptedRunResponse(
        SignalRunExecutionResult result,
        ILogger logger,
        string runDescription)
    {
        if (IsUnpersistedFailure(result))
        {
            logger.LogError(
                "Starting a {RunDescription} signal run could not persist its result. Error: {ErrorMessage}",
                runDescription,
                result.ErrorMessage);
            return CreateServiceUnavailableResponse(RunDataUnavailableError);
        }

        return Results.Json(
            result,
            SignalsJsonSerializer.JsonOptions,
            statusCode: StatusCodes.Status202Accepted);
    }

    private static IResult CreateAcceptedRangeRunResponse(
        IReadOnlyList<SignalRunExecutionResult> results,
        ILogger logger,
        DateOnly startDate,
        DateOnly endDate)
    {
        var unpersistedFailure = results.FirstOrDefault(IsUnpersistedFailure);
        if (unpersistedFailure is not null)
        {
            logger.LogError(
                "Starting a range signal run for {StartDate} through {EndDate} could not persist its result. Error: {ErrorMessage}",
                startDate,
                endDate,
                unpersistedFailure.ErrorMessage);
            return CreateServiceUnavailableResponse(RunDataUnavailableError);
        }

        return Results.Json(
            results,
            SignalsJsonSerializer.JsonOptions,
            statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> CreateReadinessResponse(HealthCheckService healthCheckService)
    {
        var report = await healthCheckService.CheckHealthAsync(registration =>
            registration.Tags.Contains("ready", StringComparer.Ordinal));

        return report.Status == HealthStatus.Healthy
            ? Results.Json(new StatusResponse("Healthy"), SignalsJsonSerializer.JsonOptions)
            : Results.Json(
                new StatusResponse("Unhealthy"),
                SignalsJsonSerializer.JsonOptions,
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static bool IsUnpersistedFailure(SignalRunExecutionResult result)
    {
        return result.Status == BlowingCandles.Infrastructure.Persistence.Entities.SignalRunStatus.Failed
            && result.RunId == 0L;
    }

    private static SignalFileEntry ToSignalFileEntry(SignalRunSignalResult signal)
    {
        return new SignalFileEntry(
            signal.Ticker,
            signal.Action.ToString(),
            signal.NewsState.ToString(),
            signal.MarketAction.ToString(),
            signal.Reason,
            signal.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:sszzz"));
    }

    private static RunSummaryResponse ToRunSummaryResponse(SignalRunReadResult run)
    {
        return new RunSummaryResponse(
            run.RunId,
            run.RunType,
            run.Trigger,
            run.Status,
            run.AsOfDate,
            run.IsSimulation,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.TickerCount,
            run.ErrorMessage);
    }

    private static IResult CreateTickerNotFoundResponse(string ticker)
    {
        return Results.Json(
            new { error = "Ticker not found", ticker },
            SignalsJsonSerializer.JsonOptions,
            statusCode: StatusCodes.Status404NotFound);
    }

    private static IResult CreateNotFoundResponse(string error)
    {
        return Results.Json(
            new { error },
            SignalsJsonSerializer.JsonOptions,
            statusCode: StatusCodes.Status404NotFound);
    }

    private static IResult CreateBadRequestResponse(string error)
    {
        return Results.Json(
            new { error },
            SignalsJsonSerializer.JsonOptions,
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static IResult CreateServiceUnavailableResponse(string error)
    {
        return Results.Json(
            new { error },
            SignalsJsonSerializer.JsonOptions,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private sealed record AsOfRunRequest(DateOnly? AsOfDate);

    private sealed record RangeRunRequest(DateOnly? StartDate, DateOnly? EndDate);

    private sealed record RunSummaryResponse(
        long RunId,
        BlowingCandles.Infrastructure.Persistence.Entities.SignalRunType RunType,
        string Trigger,
        BlowingCandles.Infrastructure.Persistence.Entities.SignalRunStatus Status,
        DateOnly AsOfDate,
        bool IsSimulation,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        int TickerCount,
        string? ErrorMessage);

    private sealed record StatusResponse(string Status);
}
