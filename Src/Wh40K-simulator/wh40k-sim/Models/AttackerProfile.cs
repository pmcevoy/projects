namespace Wh40kSim.Models;

public sealed record AttackerProfile
{
    public string Name { get; init; } = string.Empty;
    public int Models { get; init; }
    public WeaponProfile Weapon { get; init; } = new();
    public RerollOptions Rerolls { get; init; } = new();
    /// <summary>Minimum raw die roll that counts as a Critical Hit. Default 6; can be lowered e.g. to 5.</summary>
    public int CriticalHitsOn { get; init; } = 6;
}
