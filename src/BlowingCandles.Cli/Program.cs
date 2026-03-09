using System.Globalization;
using BlowingCandles.Application;
using BlowingCandles.Cli.Handlers;
using BlowingCandles.Domain.Services;
using BlowingCandles.Infrastructure.Clock;
using BlowingCandles.Infrastructure.Config;
using BlowingCandles.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlowingCandles.Cli;

internal static class Program
{
    private const string DefaultConfigPath = "config.yaml";

    public static int Main(string[] args)
    {
        var configuration = BuildInfrastructureConfiguration();
        var services = new ServiceCollection();

        try
        {
            services.AddPersistence(configuration);
        }
        catch (InvalidOperationException exception) when (IsMissingAppDbConnectionString(exception))
        {
            // Existing commands are still file-backed; missing DB config should not block them.
        }

        using var serviceProvider = services.BuildServiceProvider();

        var configLoader = new YamlConfigLoader();
        var outputRenderer = new OutputRenderer();

        var checkCalendarHandler = new CheckCalendarHandler(configLoader, new SystemClock());
        var statsPeriodsHandler = new StatsPeriodsHandler(configLoader, new SystemClock(), new HoldingPeriodCalculator());
        var runRealtimeHandler = new RunRealtimeHandler(configLoader, outputRenderer);
        var runAsOfHandler = new RunAsOfHandler(configLoader, outputRenderer);
        var runRangeHandler = new RunRangeHandler(configLoader, outputRenderer);

        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintUsage();
                return 0;
            }

            return args[0] switch
            {
                "check-calendar" => checkCalendarHandler.Handle(DefaultConfigPath),
                "stats-periods" => statsPeriodsHandler.Handle(DefaultConfigPath),
                "run-realtime" => runRealtimeHandler.Handle(DefaultConfigPath),
                "run-asof" when args.Length == 2 && TryParseDate(args[1], out var asOfDate) =>
                    runAsOfHandler.Handle(DefaultConfigPath, asOfDate),
                "run-range" when args.Length == 3
                    && TryParseDate(args[1], out var startDate)
                    && TryParseDate(args[2], out var endDate) =>
                    runRangeHandler.Handle(DefaultConfigPath, startDate, endDate),
                _ => PrintUsageAndFail()
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Unhandled error: {exception.Message}");
            return 1;
        }
    }

    private static IConfiguration BuildInfrastructureConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddJsonFile(Path.Combine("src", "BlowingCandles.Cli", "appsettings.json"), optional: true)
            .AddJsonFile(Path.Combine("src", "BlowingCandles.Cli", "appsettings.Development.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static bool IsMissingAppDbConnectionString(InvalidOperationException exception)
    {
        return exception.Message.Contains("Connection string 'AppDb' is missing", StringComparison.Ordinal);
    }

    private static bool IsHelp(string arg)
    {
        return arg is "--help" or "-h" or "help";
    }

    private static bool TryParseDate(string value, out DateOnly date)
    {
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static int PrintUsageAndFail()
    {
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("BlowingCandles CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  check-calendar");
        Console.WriteLine("  stats-periods");
        Console.WriteLine("  run-realtime");
        Console.WriteLine("  run-asof <yyyy-MM-dd>");
        Console.WriteLine("  run-range <yyyy-MM-dd> <yyyy-MM-dd>");
        Console.WriteLine();
        Console.WriteLine($"Config file: {DefaultConfigPath}");
    }
}
