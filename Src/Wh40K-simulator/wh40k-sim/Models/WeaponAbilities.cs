namespace Wh40kSim.Models;

public sealed record WeaponAbilities
{
    public bool Torrent { get; init; }
    public bool Blast { get; init; }
    public int Melta { get; init; }
    public int RapidFire { get; init; }
    public int SustainedHits { get; init; }
    public bool LethalHits { get; init; }
    public bool DevastatingWounds { get; init; }
}
