using System.Globalization;
using BlowingCandles.Domain.Interfaces;
using BlowingCandles.Domain.Models;
using BlowingCandles.Domain.Services;
using BlowingCandles.Infrastructure.Audit;
using BlowingCandles.Infrastructure.Config;

namespace BlowingCandles.Cli.Handlers;

public sealed class StatsPeriodsHandler
{
    private readonly YamlConfigLoader _configLoader;
    private readonly IClock _clock;
    private readonly HoldingPeriodCalculator _holdingPeriodCalculator;

    public StatsPeriodsHandler(YamlConfigLoader configLoader, IClock clock, HoldingPeriodCalculator holdingPeriodCalculator)
    {
        _configLoader = configLoader;
        _clock = clock;
        _holdingPeriodCalculator = holdingPeriodCalculator;
    }

    public int Handle(string configPath)
    {
        var config = _configLoader.Load(configPath);
        var configuredAuditPath = config.Audit.JsonlPath;
        var resolvedAuditPath = config.Audit.ResolvedJsonlPath;

        if (string.IsNullOrWhiteSpace(configuredAuditPath) || string.IsNullOrWhiteSpace(resolvedAuditPath))
        {
            Console.WriteLine("ERROR: config.yaml -> audit.jsonl_path missing");
            return 1;
        }

        AuditReadResult auditReadResult;

        try
        {
            auditReadResult = new JsonlAuditReader(resolvedAuditPath).ReadAll();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"ERROR: unable to read audit file: {configuredAuditPath}");
            return 1;
        }

        var analysis = _holdingPeriodCalculator.Calculate(auditReadResult.BuySellRecords);

        WriteReport(configuredAuditPath, auditReadResult.TotalRecords, analysis);
        return 0;
    }

    private void WriteReport(string configuredAuditPath, int totalRecords, HoldingPeriodAnalysis analysis)
    {
        Console.WriteLine($"Audit file: {configuredAuditPath}");
        Console.WriteLine($"Total records: {totalRecords}");

        if (analysis.CompletedPeriods.Count == 0 && analysis.OpenPositions.Count == 0)
        {
            return;
        }

        Console.WriteLine();

        if (analysis.CompletedPeriods.Count > 0)
        {
            Console.WriteLine("Completed holding periods:");
            foreach (var period in analysis.CompletedPeriods)
            {
                Console.WriteLine(
                    $"  {period.Ticker}: BUY {FormatUtc(period.BuyTimestamp)} -> SELL {FormatUtc(period.SellTimestamp)} ({FormatDurationDays(period.Duration)})");
            }

            Console.WriteLine();
        }

        if (analysis.OpenPositions.Count > 0)
        {
            Console.WriteLine("Open positions (BUY without SELL):");
            foreach (var position in analysis.OpenPositions)
            {
                var openDuration = _clock.UtcNow - position.BuyTimestamp;
                Console.WriteLine(
                    $"  {position.Ticker}: BUY {FormatUtc(position.BuyTimestamp)} (open for {FormatDurationDays(openDuration)})");
            }

            Console.WriteLine();
        }

        var averageHoldingPeriod = analysis.CompletedPeriods.Count == 0
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(Convert.ToInt64(analysis.CompletedPeriods.Average(period => period.Duration.Ticks)));

        Console.WriteLine("Summary:");
        Console.WriteLine($"  Completed periods: {analysis.CompletedPeriods.Count}");
        Console.WriteLine($"  Average holding period: {FormatDurationDays(averageHoldingPeriod)}");
        Console.WriteLine($"  Open positions: {analysis.OpenPositions.Count}");
    }

    private static string FormatDurationDays(TimeSpan duration)
    {
        return $"{duration.TotalDays.ToString("0.0", CultureInfo.InvariantCulture)} days";
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var wholeSeconds = utc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var microseconds = (utc.Ticks % TimeSpan.TicksPerSecond) / 10;

        return microseconds == 0
            ? $"{wholeSeconds}Z"
            : $"{wholeSeconds}.{microseconds:000000}Z";
    }
}
