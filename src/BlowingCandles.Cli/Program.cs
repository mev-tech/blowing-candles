using System.Globalization;
using BlowingCandles.Application.Services;
using BlowingCandles.Cli.Handlers;
using BlowingCandles.Domain.Services;
using BlowingCandles.Infrastructure.Clock;
using BlowingCandles.Infrastructure.Config;
using BlowingCandles.Infrastructure.Persistence;
using BlowingCandles.Infrastructure.Persistence.Services;
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
            // Non-run commands can still execute without DB persistence configuration.
        }

        using var serviceProvider = services.BuildServiceProvider();

        var configLoader = new YamlConfigLoader();
        var checkCalendarHandler = new CheckCalendarHandler(configLoader, new SystemClock());
        var statsPeriodsHandler = new StatsPeriodsHandler(configLoader, new SystemClock(), new HoldingPeriodCalculator());

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
                "run-realtime" => ExecuteRunRealtime(serviceProvider, configLoader, DefaultConfigPath),
                "run-asof" when args.Length == 2 && TryParseDate(args[1], out var asOfDate) =>
                    ExecuteRunAsOf(serviceProvider, configLoader, DefaultConfigPath, asOfDate),
                "run-range" when args.Length == 3
                    && TryParseDate(args[1], out var startDate)
                    && TryParseDate(args[2], out var endDate) =>
                    ExecuteRunRange(serviceProvider, configLoader, DefaultConfigPath, startDate, endDate),
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

    private static int ExecuteRunRealtime(IServiceProvider serviceProvider, YamlConfigLoader configLoader, string configPath)
    {
        using var scope = serviceProvider.CreateScope();
        var handler = new RunRealtimeHandler(CreateExecutionService(scope.ServiceProvider, configLoader, configPath));
        return handler.Handle();
    }

    private static int ExecuteRunAsOf(
        IServiceProvider serviceProvider,
        YamlConfigLoader configLoader,
        string configPath,
        DateOnly asOfDate)
    {
        using var scope = serviceProvider.CreateScope();
        var handler = new RunAsOfHandler(CreateExecutionService(scope.ServiceProvider, configLoader, configPath));
        return handler.Handle(asOfDate);
    }

    private static int ExecuteRunRange(
        IServiceProvider serviceProvider,
        YamlConfigLoader configLoader,
        string configPath,
        DateOnly startDate,
        DateOnly endDate)
    {
        using var scope = serviceProvider.CreateScope();
        var handler = new RunRangeHandler(CreateExecutionService(scope.ServiceProvider, configLoader, configPath));
        return handler.Handle(startDate, endDate);
    }

    private static ISignalRunExecutionService CreateExecutionService(
        IServiceProvider serviceProvider,
        YamlConfigLoader configLoader,
        string configPath)
    {
        var config = configLoader.Load(configPath);
        var signalRunPersistence = serviceProvider.GetService<SignalRunPersistenceService>();
        var governorStateStoreFactory = serviceProvider.GetService<TradeGovernorDbStateStoreFactory>();

        if (signalRunPersistence is null || governorStateStoreFactory is null)
        {
            throw new InvalidOperationException(
                "Signal run commands require database persistence configuration. Configure connection string 'AppDb'.");
        }

        return new SignalRunExecutionService(
            config,
            signalRunPersistence.PersistRun,
            governorStateStoreFactory.Create,
            diagnosticWriter: Console.Error.WriteLine);
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
