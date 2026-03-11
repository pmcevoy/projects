namespace Wh40kSim.Models;

public sealed record RerollOptions
{
    public bool HitRerollOnes { get; init; }
    public bool HitRerollAll { get; init; }
    public bool WoundRerollOnes { get; init; }
    public bool WoundRerollAll { get; init; }
}
