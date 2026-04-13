namespace Wh40kArmyEnricher.Core.Simulation;

public sealed record SimWeaponAbilities
{
    public bool Torrent { get; init; }
    public bool Blast { get; init; }
    public int Melta { get; init; }
    public int RapidFire { get; init; }
    public int SustainedHits { get; init; }
    public bool LethalHits { get; init; }
    public bool DevastatingWounds { get; init; }
    /// <summary>Anti ability: maps defender keyword → minimum unmodified wound roll for a Critical Wound.</summary>
    public IReadOnlyDictionary<string, int> Anti { get; init; } = new Dictionary<string, int>();
    public bool TwinLinked { get; init; }
}
