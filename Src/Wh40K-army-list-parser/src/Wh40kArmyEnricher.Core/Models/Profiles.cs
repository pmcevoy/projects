// Internal enrichment context — maps army list entries to resolved BSData data.
// The final output is produced in Contracts.SimulationProfiles.

using Wh40kArmyEnricher.Contracts;

namespace Wh40kArmyEnricher.Core.Models;

/// <summary>Fully enriched unit ready for serialisation.</summary>
public record EnrichedUnit
{
    public UnitEntry ArmyListEntry { get; init; } = null!;
    public AttackerProfile Attacker { get; init; } = new();
    public DefenderProfile Defender { get; init; } = new();
}
