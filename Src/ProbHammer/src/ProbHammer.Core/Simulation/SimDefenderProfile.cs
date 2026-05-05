namespace ProbHammer.Core.Simulation;

public record SimDefenderProfile
{
    public string Name { get; init; } = "";
    public int Models { get; init; }
    public int Toughness { get; init; }
    public int Save { get; init; }
    public int? InvulnerableSave { get; init; }
    public int Wounds { get; init; }
    public int? FeelNoPain { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];
}
