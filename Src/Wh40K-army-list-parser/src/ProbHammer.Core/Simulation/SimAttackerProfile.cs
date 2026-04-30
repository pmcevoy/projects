namespace ProbHammer.Core.Simulation;

public record SimAttackerProfile
{
    public string Name { get; init; } = "";
    public IReadOnlyList<SimWeaponProfile> Weapons { get; init; } = [];
    public bool HitRerollOnes { get; init; }
    public bool HitRerollAll { get; init; }
    public bool FishForCriticalHits { get; init; }
    public bool WoundRerollOnes { get; init; }
    public bool WoundRerollAll { get; init; }
    public bool FishForCriticalWounds { get; init; }
    public int CriticalHitsOn { get; init; } = 6;
    public int CriticalWoundsOn { get; init; } = 6;
}
