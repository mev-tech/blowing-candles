using BlowingCandles.Application;
using BlowingCandles.Infrastructure.Audit;
using BlowingCandles.Infrastructure.Clock;
using BlowingCandles.Infrastructure.Config;

namespace BlowingCandles.Cli.Handlers;

public sealed class RunAsOfHandler
{
    private readonly YamlConfigLoader _configLoader;
    private readonly OutputRenderer _outputRenderer;
    private readonly RunCommandSupport _support;

    public RunAsOfHandler(
        YamlConfigLoader configLoader,
        OutputRenderer outputRenderer,
        RunCommandSupport? support = null)
    {
        _configLoader = configLoader;
        _outputRenderer = outputRenderer;
        _support = support ?? new RunCommandSupport();
    }

    public int Handle(string configPath, DateOnly asOfDate)
    {
        var config = _configLoader.Load(configPath);
        var clock = new FixedClock(new DateTimeOffset(asOfDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        var liveAuditPath = RunCommandSupport.ResolveAuditPath(config, configPath);
        var simulationStatePath = RunCommandSupport.BuildSimulationStatePath(config.State.Path);
        var signals = _support.RunSignals(config, simulationStatePath, clock);
        var textPath = RunCommandSupport.BuildSimulationOutputPath(config.Output.TextFile, asOfDate, ".txt");
        var jsonPath = RunCommandSupport.BuildSimulationOutputPath(config.Output.JsonFile, asOfDate, ".json");
        var simulationAuditPath = RunCommandSupport.BuildSimulationAuditPath(liveAuditPath);

        _outputRenderer.WriteSignals(textPath, jsonPath, signals);
        new JsonlAuditWriter(simulationAuditPath).Append(signals);

        Console.WriteLine($"Generated {signals.Count} signals for {asOfDate:yyyy-MM-dd}.");
        Console.WriteLine($"Text output: {textPath}");
        Console.WriteLine($"JSON output: {jsonPath}");
        return 0;
    }
}
