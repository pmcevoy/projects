using Wh40kSim.Models;
using Wh40kSim.Simulation;

namespace Wh40kSim.Tests;

public class FnpTests
{
    private static SimulationConfig MakeConfig(int? fnp = null, int damage = 3)
    {
        return new SimulationConfig
        {
            SimulationRuns = 1,
            Attacker = new AttackerProfile
            {
                Models = 1,
                Weapon = new WeaponProfile
                {
                    Attacks = DiceExpression.Fixed(1),
                    Skill = 2,
                    Strength = 10,
                    Ap = 6,
                    Damage = DiceExpression.Fixed(damage),
                },
            },
            Defender = new DefenderProfile
            {
                Models = 1,
                Toughness = 4,
                Save = 7,   // impossible save — always fails
                Wounds = 10,
                FeelNoPain = fnp,
            },
        };
    }

    [Fact]
    public void NoFnp_AllDamagePasses()
    {
        // Torrent-like setup: hit=6(nat6), wound=6(nat6, no dev wounds), save fails(1), damage=3
        var dice = new FakeDiceRoller(6, 6, 1);
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(fnp: null, damage: 3);
        var results = sim.Run(config);
        Assert.Equal(3, results[0]);
    }

    [Fact]
    public void Fnp5Plus_PerWoundPoint()
    {
        // Hit=6, Wound=6(nat6), Save=1(fail). 3 damage, FNP rolls: 4(fail fnp→wound), 5(pass fnp→no wound), 6(pass fnp→no wound)
        // FNP 5+: >= 5 ignores the wound
        // So 1 damage gets through
        var dice = new FakeDiceRoller(6, 6, 1, 4, 5, 6);
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(fnp: 5, damage: 3);
        var results = sim.Run(config);
        Assert.Equal(1, results[0]);
    }

    [Fact]
    public void Fnp2Plus_NearlyAllIgnored()
    {
        // FNP 2+: only a 1 fails. 3 damage, FNP rolls: 1, 2, 3 → only first wound gets through
        var dice = new FakeDiceRoller(6, 6, 1, 1, 2, 3);
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(fnp: 2, damage: 3);
        var results = sim.Run(config);
        Assert.Equal(1, results[0]);
    }

    [Fact]
    public void Fnp7Plus_AllFail_AllDamageThrough()
    {
        // Impossible FNP (7+): all wounds get through
        var dice = new FakeDiceRoller(6, 6, 1, 6, 6, 6);
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(fnp: 7, damage: 3);
        var results = sim.Run(config);
        Assert.Equal(3, results[0]);
    }
}
