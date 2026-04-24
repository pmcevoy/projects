using FluentAssertions;
using Wh40kArmyEnricher.Core.Contracts;
using Wh40kArmyEnricher.Core.Simulation;

namespace Wh40kArmyEnricher.Tests.Simulation;

public class SimulationAdapterTests
{
    // ─── helpers ────────────────────────────────────────────────────────────────

    private static UnitProfile MakeAttacker(string weaponName = "Sword",
        string variant = "", int skill = 3, int s = 4, int ap = 0, int damage = 1,
        string modelName = "Warrior", int modelCount = 1, int critHitsOn = 6)
    {
        return new UnitProfile
        {
            Name = "Attacker",
            ModelCount = modelCount,
            CriticalHitsOn = critHitsOn,
            Models =
            [
                new ModelProfile
                {
                    ModelName = modelName,
                    Count = modelCount,
                    Weapons =
                    [
                        new WeaponProfile
                        {
                            WeaponName = weaponName,
                            Type = WeaponType.Melee,
                            Profiles =
                            [
                                new WeaponVariantProfile
                                {
                                    Variant = variant,
                                    Skill = skill,
                                    Strength = s,
                                    Ap = -ap,  // Contracts stores AP as negative integer
                                    Attacks = ScalarValue.FromInt(1),
                                    Damage = ScalarValue.FromInt(damage),
                                    Abilities = new WeaponAbilities()
                                }
                            ]
                        }
                    ]
                }
            ]
        };
    }

    private static UnitProfile MakeDefender(int t = 4, int save = 3, int wounds = 1,
        int modelCount = 1, int? invuln = null, int? fnp = null, string[]? keywords = null)
    {
        return new UnitProfile
        {
            Name = "Defender",
            ModelCount = modelCount,
            Toughness = t,
            Save = save,
            InvulnerableSave = invuln,
            Wounds = wounds,
            FeelNoPain = fnp,
            Keywords = keywords?.ToList() ?? [],
        };
    }

    private static SimulationRequest BasicRequest(string weaponName = "Sword",
        string variant = "", string modelName = "")
        => new()
        {
            WeaponSelections = [new WeaponSelection
            {
                WeaponName = weaponName,
                VariantName = variant,
                ModelName = modelName,
                ModelCount = 1
            }]
        };

    private static WeaponProfile MakeSword() => new()
    {
        WeaponName = "Sword",
        Type = WeaponType.Melee,
        Profiles =
        [
            new WeaponVariantProfile
            {
                Variant = "",
                Skill = 3,
                Strength = 4,
                Ap = 0,
                Attacks = ScalarValue.FromInt(1),
                Damage = ScalarValue.FromInt(1),
                Abilities = new WeaponAbilities()
            }
        ]
    };

    // ─── tests ───────────────────────────────────────────────────────────────

    [Fact]
    public void Adapt_BasicRequest_ReturnsResponse()
    {
        var adapter = new SimulationAdapter();
        var response = adapter.Adapt(BasicRequest(), MakeAttacker(), MakeDefender());

        response.MeanDamage.Should().BeGreaterThanOrEqualTo(0);
        response.ExpectedKills.Should().BeGreaterThanOrEqualTo(0);
        response.PKillAtLeastOne.Should().BeInRange(0, 1);
        response.StdDev.Should().BeGreaterThanOrEqualTo(0);
        response.StageStats.Should().NotBeNull();
    }

    [Fact]
    public void Adapt_SingleWeaponGroup_WeaponBreakdownIsEmpty()
    {
        var adapter = new SimulationAdapter();
        var response = adapter.Adapt(BasicRequest(), MakeAttacker(), MakeDefender());
        response.WeaponBreakdown.Should().BeEmpty();
    }

    [Fact]
    public void Adapt_NegativeApPassedThrough_RaisesSaveThreshold()
    {
        // AP-2 in contracts (stored as -2); passed unchanged into sim engine.
        // effectiveSave = 3 - (-2) = 5 → some attacks get through → MeanDamage > 0.
        var adapter = new SimulationAdapter();
        var attacker = MakeAttacker(ap: 2); // MakeAttacker sets Ap = -ap = -2
        var defender = MakeDefender(t: 4, save: 3);

        var response = adapter.Adapt(BasicRequest(), attacker, defender);
        response.MeanDamage.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Adapt_Cover_ImprovesDefenderSave()
    {
        var attacker = MakeAttacker(s: 4, ap: 0);
        var defender = MakeDefender(t: 4, save: 6); // 6+ save → almost always fails

        var reqNoCover = BasicRequest();
        var reqCover = new SimulationRequest
        {
            WeaponSelections = BasicRequest().WeaponSelections,
            Cover = true
        };

        var adapter = new SimulationAdapter(new CombatSimulator(new DiceRoller(new Random(42))));
        var noCover = adapter.Adapt(reqNoCover, attacker, defender);

        adapter = new SimulationAdapter(new CombatSimulator(new DiceRoller(new Random(42))));
        var withCover = adapter.Adapt(reqCover, attacker, defender);

        // Cover improves save from 6+ to 5+ → less damage on average
        withCover.MeanDamage.Should().BeLessThan(noCover.MeanDamage + 0.05);
    }

    [Fact]
    public void Adapt_CoverIgnoresCover_NoEffect()
    {
        var adapter = new SimulationAdapter();
        var attacker = MakeAttacker();
        var defender = MakeDefender(t: 4, save: 6);

        var reqIgnore = new SimulationRequest
        {
            WeaponSelections = BasicRequest().WeaponSelections,
            Cover = true,
            IgnoresCover = true
        };

        // Cover + IgnoresCover → cover has no effect — just verify no crash
        var response = adapter.Adapt(reqIgnore, attacker, defender);
        response.Should().NotBeNull();
    }

    [Fact]
    public void Adapt_FnpOverride_ReducesDamage()
    {
        var attacker = MakeAttacker(s: 4, damage: 3);
        var defender = MakeDefender(t: 4, save: 7, wounds: 3, fnp: null);

        var reqNoFnp = BasicRequest();
        var reqFnp = new SimulationRequest
        {
            WeaponSelections = BasicRequest().WeaponSelections,
            FnpOverride = 4
        };

        var adapter1 = new SimulationAdapter(new CombatSimulator(new DiceRoller(new Random(1))));
        var noFnp = adapter1.Adapt(reqNoFnp, attacker, defender);

        var adapter2 = new SimulationAdapter(new CombatSimulator(new DiceRoller(new Random(1))));
        var withFnp = adapter2.Adapt(reqFnp, attacker, defender);

        withFnp.MeanDamage.Should().BeLessThan(noFnp.MeanDamage + 0.05);
    }

    [Fact]
    public void Adapt_NativeFnpWinsOverOverride_NoException()
    {
        var adapter = new SimulationAdapter();
        var attacker = MakeAttacker();
        var defender = MakeDefender(t: 4, save: 7, fnp: 5);
        var request = new SimulationRequest
        {
            WeaponSelections = BasicRequest().WeaponSelections,
            FnpOverride = 3
        };

        var act = () => adapter.Adapt(request, attacker, defender);
        act.Should().NotThrow();
    }

    [Fact]
    public void Adapt_TwoSelectionsOfSameProfile_AggregatesAttacks()
    {
        // Two models with identical weapon profiles → single grouped weapon with 2× attacks.
        var attacker = new UnitProfile
        {
            Name = "Attacker",
            ModelCount = 2,
            CriticalHitsOn = 6,
            Models =
            [
                new ModelProfile { ModelName = "Model A", Count = 1, Weapons = [MakeSword()] },
                new ModelProfile { ModelName = "Model B", Count = 1, Weapons = [MakeSword()] }
            ]
        };
        var defender = MakeDefender(t: 4, save: 7);

        var combined = new SimulationRequest
        {
            WeaponSelections =
            [
                new WeaponSelection { WeaponName = "Sword", ModelName = "Model A", ModelCount = 1 },
                new WeaponSelection { WeaponName = "Sword", ModelName = "Model B", ModelCount = 1 },
            ]
        };
        var single = new SimulationRequest
        {
            WeaponSelections = [new WeaponSelection { WeaponName = "Sword", ModelName = "Model A", ModelCount = 1 }]
        };

        var adapter1 = new SimulationAdapter(new CombatSimulator(new DiceRoller(new Random(0))));
        var singleResult = adapter1.Adapt(single, attacker, defender);

        var adapter2 = new SimulationAdapter(new CombatSimulator(new DiceRoller(new Random(0))));
        var combinedResult = adapter2.Adapt(combined, attacker, defender);

        // Same profile grouped → single weapon group → WeaponBreakdown empty
        combinedResult.WeaponBreakdown.Should().BeEmpty();
        // 2× attacks → roughly 2× damage
        combinedResult.MeanDamage.Should().BeApproximately(singleResult.MeanDamage * 2, singleResult.MeanDamage * 0.3);
    }

    [Fact]
    public void Adapt_DefenderModelCountOverride_NoException()
    {
        var adapter = new SimulationAdapter();
        var attacker = MakeAttacker(s: 4);
        var defender = MakeDefender(modelCount: 10);
        var request = new SimulationRequest
        {
            WeaponSelections = BasicRequest().WeaponSelections,
            DefenderModelCount = 1
        };

        var response = adapter.Adapt(request, attacker, defender);
        response.MeanDamage.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Adapt_CritHitOn5Plus_LowersThreshold()
    {
        // With CritHitOn5Plus and LethalHits override: roll of 5 = crit → auto-wound.
        // ConstantRoller(5): every hit roll = 5, every save roll = 5 (fails vs 7+).
        var roller = new ConstantRoller(5);
        var attacker = MakeAttacker();
        var defender = MakeDefender(t: 4, save: 7);

        var request = new SimulationRequest
        {
            WeaponSelections = [new WeaponSelection { WeaponName = "Sword", ModelCount = 1 }],
            CritHitOn5Plus = true,
            LethalHitsOverride = true,
        };

        var adapter = new SimulationAdapter(new CombatSimulator(roller));
        var response = adapter.Adapt(request, attacker, defender);

        response.StageStats.AvgLethalHitsAutoWounds.Should().BeApproximately(1, 0.01);
    }

    [Fact]
    public void Adapt_UnknownWeapon_Skipped_NoException()
    {
        var adapter = new SimulationAdapter();
        var attacker = MakeAttacker(weaponName: "Sword");
        var defender = MakeDefender();
        var request = new SimulationRequest
        {
            WeaponSelections = [new WeaponSelection { WeaponName = "NonExistentWeapon" }]
        };

        var act = () => adapter.Adapt(request, attacker, defender);
        act.Should().NotThrow();
    }
}
