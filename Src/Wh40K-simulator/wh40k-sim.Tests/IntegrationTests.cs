using Wh40kSim;
using Wh40kSim.Simulation;

namespace Wh40kSim.Tests;

public class IntegrationTests
{
    [Fact]
    public void Run_ExampleProfile_ProducesReasonableResults()
    {
        string profilePath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "profiles", "example.yaml");
        profilePath = Path.GetFullPath(profilePath);

        var config = YamlLoader.Load(profilePath);
        config = config with { SimulationRuns = 1_000 };

        var sim = new CombatSimulator(new DiceRoller(seed: 42));
        var results = sim.Run(config);

        Assert.Equal(1_000, results.Count);
        Assert.All(results, r => Assert.True(r >= 0, $"Damage must be non-negative, got {r}"));
        Assert.True(results.Average() > 0, "Expected positive mean damage for Librarian vs Chaos Psyker");
    }

    [Fact]
    public void Run_ExampleProfile_LoadsCorrectly()
    {
        string profilePath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "profiles", "example.yaml");
        profilePath = Path.GetFullPath(profilePath);

        var config = YamlLoader.Load(profilePath);

        Assert.Equal(100_000, config.SimulationRuns);
        Assert.Equal("Librarian with Force Sword", config.Attacker.Name);
        Assert.Equal(1, config.Attacker.Models);
        Assert.Equal("Force Sword", config.Attacker.Weapon.Name);
        Assert.Equal(3, config.Attacker.Weapon.Skill);
        Assert.Equal(6, config.Attacker.Weapon.Strength);
        Assert.Equal(2, config.Attacker.Weapon.Ap);
        Assert.True(config.Attacker.Weapon.Abilities.DevastatingWounds);
        Assert.Equal(4, config.Attacker.Weapon.Abilities.Anti["Psyker"]);
        Assert.Equal(5, config.Attacker.Weapon.Abilities.Anti["Daemon"]);
        Assert.Equal("Chaos Psyker", config.Defender.Name);
        Assert.Equal(1, config.Defender.Models);
        Assert.Equal(4, config.Defender.Toughness);
        Assert.Equal(5, config.Defender.Save);
        Assert.Equal(4, config.Defender.InvulnerableSave);
        Assert.Null(config.Defender.FeelNoPain);
        Assert.Contains("Psyker", config.Defender.Keywords);
        Assert.Contains("Character", config.Defender.Keywords);
        Assert.Contains("Chaos", config.Defender.Keywords);
    }
}
