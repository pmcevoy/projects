namespace Wh40kArmyEnricher.Core.Simulation;

public sealed record SimRerollOptions
{
    public bool HitRerollOnes { get; init; }
    public bool HitRerollAll { get; init; }
    public bool WoundRerollOnes { get; init; }
    public bool WoundRerollAll { get; init; }
}
