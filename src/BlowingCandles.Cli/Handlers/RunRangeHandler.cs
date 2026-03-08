using BlowingCandles.Application;
using BlowingCandles.Infrastructure.Audit;
using BlowingCandles.Infrastructure.Clock;
using BlowingCandles.Infrastructure.Config;

namespace BlowingCandles.Cli.Handlers;

public sealed class RunRangeHandler
{
    private readonly YamlConfigLoader _configLoader;
    private readonly OutputRenderer _outputRenderer;
    private readonly RunCommandSupport _support;

    public RunRangeHandler(
        YamlConfigLoader configLoader,
        OutputRenderer outputRenderer,
        RunCommandSupport? support = null)
    {
        _configLoader = configLoader;
        _outputRenderer = outputRenderer;
        _support = support ?? new RunCommandSupport();
    }

    public int Handle(string configPath, DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
        {
            throw new ArgumentOutOfRangeException(nameof(endDate), "End date must be on or after the start date.");
        }

        var config = _configLoader.Load(configPath);
        var simulationStatePath = RunCommandSupport.BuildSimulationStatePath(config.State.Path);
        var simulationAuditPath = RunCommandSupport.BuildSimulationAuditPath(
            RunCommandSupport.ResolveAuditPath(config, configPath));
        var daysProcessed = 0;

        for (var currentDate = startDate; currentDate <= endDate; currentDate = currentDate.AddDays(1))
        {
            var clock = new FixedClock(new DateTimeOffset(currentDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
            var signals = _support.RunSignals(config, simulationStatePath, clock);
            var textPath = RunCommandSupport.BuildSimulationOutputPath(config.Output.TextFile, currentDate, ".txt");
            var jsonPath = RunCommandSupport.BuildSimulationOutputPath(config.Output.JsonFile, currentDate, ".json");

            _outputRenderer.WriteSignals(textPath, jsonPath, signals);
            new JsonlAuditWriter(simulationAuditPath).Append(signals);
            daysProcessed++;
        }

        Console.WriteLine($"Generated signals for {daysProcessed} day(s).");
        Console.WriteLine($"Simulation audit: {simulationAuditPath}");
        return 0;
    }
}
