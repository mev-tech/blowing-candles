using BlowingCandles.Application;
using BlowingCandles.Infrastructure.Audit;
using BlowingCandles.Infrastructure.Clock;
using BlowingCandles.Infrastructure.Config;
using BlowingCandles.Domain.Interfaces;

namespace BlowingCandles.Cli.Handlers;

public sealed class RunRealtimeHandler
{
    private readonly YamlConfigLoader _configLoader;
    private readonly OutputRenderer _outputRenderer;
    private readonly RunCommandSupport _support;
    private readonly Func<IClock> _clockFactory;

    public RunRealtimeHandler(
        YamlConfigLoader configLoader,
        OutputRenderer outputRenderer,
        RunCommandSupport? support = null,
        Func<IClock>? clockFactory = null)
    {
        _configLoader = configLoader;
        _outputRenderer = outputRenderer;
        _support = support ?? new RunCommandSupport();
        _clockFactory = clockFactory ?? (() => new SystemClock());
    }

    public int Handle(string configPath)
    {
        var config = _configLoader.Load(configPath);
        var clock = _clockFactory();
        var signals = _support.RunSignals(config, config.State.Path, clock);
        var auditPath = RunCommandSupport.ResolveAuditPath(config, configPath);

        _outputRenderer.WriteSignals(config.Output.TextFile, config.Output.JsonFile, signals);
        new JsonlAuditWriter(auditPath).Append(signals);

        Console.WriteLine($"Generated {signals.Count} signals.");
        Console.WriteLine($"Text output: {config.Output.TextFile}");
        Console.WriteLine($"JSON output: {config.Output.JsonFile}");
        return 0;
    }
}
