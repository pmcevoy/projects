namespace Wh40kArmyEnricher.Core.Simulation;

public record SimWeaponAbilities
{
    public bool Torrent { get; init; }
    public bool Blast { get; init; }
    public int Melta { get; init; }          // 0 = absent
    public int RapidFire { get; init; }      // 0 = absent
    public int SustainedHits { get; init; }  // 0 = absent
    public bool LethalHits { get; init; }
    public bool DevastatingWounds { get; init; }
    public bool TwinLinked { get; init; }
    public bool IndirectFire { get; init; }
    public IReadOnlyDictionary<string, int> Anti { get; init; } = new Dictionary<string, int>();
}
