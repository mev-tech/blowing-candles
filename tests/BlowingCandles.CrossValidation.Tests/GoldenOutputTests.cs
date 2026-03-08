using BlowingCandles.Application;
using BlowingCandles.Domain.Services;
using BlowingCandles.Infrastructure.Audit;
using BlowingCandles.Infrastructure.Calendar;
using BlowingCandles.Infrastructure.Clock;
using BlowingCandles.Infrastructure.Config;
using BlowingCandles.Infrastructure.State;

namespace BlowingCandles.CrossValidation.Tests;

public sealed class GoldenOutputTests
{
    private static readonly DateOnly MixedActionsAsOfDate = new(2026, 1, 15);
    private static readonly DateOnly MixedActionsRangeStart = new(2026, 1, 13);
    private static readonly DateOnly MixedActionsRangeEnd = new(2026, 1, 15);
    private static readonly DateOnly AllWaitAsOfDate = new(2026, 1, 15);
    private static readonly DateOnly EmptyWatchlistAsOfDate = new(2026, 1, 15);

    [Fact]
    public void RunAsOf_SignalsTxt_MatchesPythonReference()
    {
        using var workspace = new ScenarioWorkspace("mixed-actions");

        var result = RunAsOf(workspace, MixedActionsAsOfDate);

        ComparisonHelpers.AssertTextMatches(
            workspace.GetGoldenPath("golden/run-asof/signals.txt"),
            result.TextPath);
    }

    [Fact]
    public void RunAsOf_SignalsJson_MatchesPythonReference()
    {
        using var workspace = new ScenarioWorkspace("mixed-actions");

        var result = RunAsOf(workspace, MixedActionsAsOfDate);

        ComparisonHelpers.AssertJsonMatches(
            workspace.GetGoldenPath("golden/run-asof/signals.json"),
            result.JsonPath);
    }

    [Fact]
    public void RunAsOf_AuditJsonl_MatchesPythonReference()
    {
        using var workspace = new ScenarioWorkspace("mixed-actions");

        var result = RunAsOf(workspace, MixedActionsAsOfDate);

        ComparisonHelpers.AssertJsonlMatches(
            workspace.GetGoldenPath("golden/run-asof/decisions.jsonl"),
            result.AuditPath);
    }

    [Fact]
    public void RunRange_MultiDay_MatchesPythonReference()
    {
        using var workspace = new ScenarioWorkspace("mixed-actions");

        var result = RunRange(workspace, MixedActionsRangeStart, MixedActionsRangeEnd);

        foreach (var day in EachDate(MixedActionsRangeStart, MixedActionsRangeEnd))
        {
            ComparisonHelpers.AssertTextMatches(
                workspace.GetGoldenPath($"golden/run-range/asof_{day:yyyy-MM-dd}.signals.txt"),
                result.GetTextPath(day));
            ComparisonHelpers.AssertJsonMatches(
                workspace.GetGoldenPath($"golden/run-range/asof_{day:yyyy-MM-dd}.signals.json"),
                result.GetJsonPath(day));
        }

        ComparisonHelpers.AssertJsonlMatches(
            workspace.GetGoldenPath("golden/run-range/decisions.jsonl"),
            result.AuditPath);
    }

    [Fact]
    public void AllWaitScenario_MatchesPythonReference()
    {
        using var workspace = new ScenarioWorkspace("all-wait");

        var result = RunAsOf(workspace, AllWaitAsOfDate);

        ComparisonHelpers.AssertTextMatches(
            workspace.GetGoldenPath("golden/run-asof/signals.txt"),
            result.TextPath);
        ComparisonHelpers.AssertJsonMatches(
            workspace.GetGoldenPath("golden/run-asof/signals.json"),
            result.JsonPath);
        ComparisonHelpers.AssertJsonlMatches(
            workspace.GetGoldenPath("golden/run-asof/decisions.jsonl"),
            result.AuditPath);
    }

    [Fact]
    public void EmptyWatchlist_MatchesPythonReference()
    {
        using var workspace = new ScenarioWorkspace("empty-watchlist");

        var result = RunAsOf(workspace, EmptyWatchlistAsOfDate);

        ComparisonHelpers.AssertTextMatches(
            workspace.GetGoldenPath("golden/run-asof/signals.txt"),
            result.TextPath);
        ComparisonHelpers.AssertJsonMatches(
            workspace.GetGoldenPath("golden/run-asof/signals.json"),
            result.JsonPath);
        ComparisonHelpers.AssertJsonlMatches(
            workspace.GetGoldenPath("golden/run-asof/decisions.jsonl"),
            result.AuditPath);
    }

    private static RunArtifacts RunAsOf(ScenarioWorkspace workspace, DateOnly asOfDate)
    {
        var config = workspace.LoadConfig();
        var provider = new FixtureMarketDataProvider(workspace.GetFixturePath("market-data"));
        var textPath = workspace.ResolvePath(config.Output.TextFile);
        var jsonPath = workspace.ResolvePath(config.Output.JsonFile);
        var auditPath = workspace.ResolveOptionalPath(config.Audit.ResolvedJsonlPath ?? config.Audit.JsonlPath)
            ?? workspace.GetOutputPath("decisions.jsonl");
        var pipeline = CreatePipeline(config, provider, workspace.ResolvePath(config.State.Path));
        var clock = new FixedClock(new DateTimeOffset(asOfDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        var signals = pipeline.Run(config.Watchlist, clock);

        new OutputRenderer().WriteSignals(textPath, jsonPath, signals);
        new JsonlAuditWriter(auditPath).Append(signals);

        return new RunArtifacts(textPath, jsonPath, auditPath);
    }

    private static RangeRunArtifacts RunRange(ScenarioWorkspace workspace, DateOnly startDate, DateOnly endDate)
    {
        var config = workspace.LoadConfig();
        var provider = new FixtureMarketDataProvider(workspace.GetFixturePath("market-data"));
        var renderer = new OutputRenderer();
        var liveTextPath = workspace.ResolvePath(config.Output.TextFile);
        var liveJsonPath = workspace.ResolvePath(config.Output.JsonFile);
        var liveAuditPath = workspace.ResolveOptionalPath(config.Audit.ResolvedJsonlPath ?? config.Audit.JsonlPath)
            ?? workspace.GetOutputPath("decisions.jsonl");
        var auditPath = BuildSimulationSiblingPath(liveAuditPath, "sim_");
        var statePath = BuildSimulationSiblingPath(workspace.ResolvePath(config.State.Path), "sim_");
        var pipeline = CreatePipeline(config, provider, statePath);

        foreach (var day in EachDate(startDate, endDate))
        {
            var clock = new FixedClock(new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
            var signals = pipeline.Run(config.Watchlist, clock);
            var textPath = BuildSimulationOutputPath(liveTextPath, day, ".txt");
            var jsonPath = BuildSimulationOutputPath(liveJsonPath, day, ".json");

            renderer.WriteSignals(textPath, jsonPath, signals);
            new JsonlAuditWriter(auditPath).Append(signals);
        }

        return new RangeRunArtifacts(
            Path.GetDirectoryName(liveTextPath) ?? workspace.RootPath,
            Path.GetDirectoryName(liveJsonPath) ?? workspace.RootPath,
            auditPath);
    }

    private static SignalPipeline CreatePipeline(AppConfig config, FixtureMarketDataProvider provider, string statePath)
    {
        var calendarPath = config.News.ResolvedLocalEarningsCalendar
            ?? throw new InvalidOperationException("Cross-validation fixtures require a local earnings calendar.");

        return new SignalPipeline(
            new EarningsGate(new EarningsCalendarFile(calendarPath), provider, config.News.BlockWindowHours),
            new TechnicalScorer(provider),
            new TradeGovernor(
                config.Policy.MaxBuysPerDay,
                config.Policy.CooldownMinutes,
                new JsonStateStore(statePath)));
    }

    private static IEnumerable<DateOnly> EachDate(DateOnly startDate, DateOnly endDate)
    {
        for (var currentDate = startDate; currentDate <= endDate; currentDate = currentDate.AddDays(1))
        {
            yield return currentDate;
        }
    }

    private static string BuildSimulationOutputPath(string liveOutputPath, DateOnly asOfDate, string extension)
    {
        var directory = Path.GetDirectoryName(liveOutputPath);
        var simulationFileName = $"asof_{asOfDate:yyyy-MM-dd}.signals{extension}";

        return string.IsNullOrWhiteSpace(directory)
            ? simulationFileName
            : Path.Combine(directory, simulationFileName);
    }

    private static string BuildSimulationSiblingPath(string livePath, string prefix)
    {
        var directory = Path.GetDirectoryName(livePath);
        var fileName = Path.GetFileName(livePath);
        var simulationFileName = fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{prefix}{fileName}";

        return string.IsNullOrWhiteSpace(directory)
            ? simulationFileName
            : Path.Combine(directory, simulationFileName);
    }

    private sealed class ScenarioWorkspace : IDisposable
    {
        public ScenarioWorkspace(string scenarioName)
        {
            FixtureRoot = Path.Combine(AppContext.BaseDirectory, "fixtures", "cross-validation", scenarioName);
            RootPath = Path.Combine(Path.GetTempPath(), $"blowing-candles-cross-validation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);

            CopyFixtureFile("config.yaml");
            CopyFixtureFile("earnings_calendar.json");
        }

        public string FixtureRoot { get; }

        public string RootPath { get; }

        public string GetFixturePath(string relativePath)
        {
            return Path.Combine(FixtureRoot, relativePath);
        }

        public string GetGoldenPath(string relativePath)
        {
            return GetFixturePath(relativePath);
        }

        public string GetOutputPath(string relativePath)
        {
            return Path.Combine(RootPath, relativePath);
        }

        public string ResolvePath(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            return Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(RootPath, path));
        }

        public string? ResolveOptionalPath(string? path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? null
                : ResolvePath(path);
        }

        public AppConfig LoadConfig()
        {
            return new YamlConfigLoader().Load(GetOutputPath("config.yaml"));
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private void CopyFixtureFile(string fileName)
        {
            var sourcePath = GetFixturePath(fileName);
            if (!File.Exists(sourcePath))
            {
                return;
            }

            File.Copy(sourcePath, GetOutputPath(fileName), overwrite: true);
        }
    }

    private sealed record RunArtifacts(string TextPath, string JsonPath, string AuditPath);

    private sealed record RangeRunArtifacts(string TextRootPath, string JsonRootPath, string AuditPath)
    {
        public string GetTextPath(DateOnly date)
        {
            return Path.Combine(TextRootPath, $"asof_{date:yyyy-MM-dd}.signals.txt");
        }

        public string GetJsonPath(DateOnly date)
        {
            return Path.Combine(JsonRootPath, $"asof_{date:yyyy-MM-dd}.signals.json");
        }
    }
}
