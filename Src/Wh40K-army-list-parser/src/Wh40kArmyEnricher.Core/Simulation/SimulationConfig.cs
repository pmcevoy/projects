namespace Wh40kArmyEnricher.Core.Simulation;

public sealed record SimulationConfig
{
    public int SimulationRuns { get; init; } = 10_000;
    public SimAttackerProfile Attacker { get; init; } = new();
    public SimDefenderProfile Defender { get; init; } = new();
}
