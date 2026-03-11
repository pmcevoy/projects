namespace Wh40kSim.Models;

public sealed record SimulationConfig
{
    public int SimulationRuns { get; init; } = 100_000;
    public AttackerProfile Attacker { get; init; } = new();
    public DefenderProfile Defender { get; init; } = new();
}
