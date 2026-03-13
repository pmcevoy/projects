namespace Wh40kArmyEnricher.Core.Models;

public record ArmyList(
    string Name,
    int Points,
    string GameSystem,
    string Faction,
    string Detachment,
    IReadOnlyList<UnitEntry> Units
);

public record UnitEntry(
    string Name,
    int Points,
    string Category,
    IReadOnlyList<string> Enhancements,
    IReadOnlyList<ModelEntry> Models
);

public record ModelEntry(
    string Name,
    int Count,
    IReadOnlyList<WeaponEntry> Weapons
);

public record WeaponEntry(string Name, int Count);
