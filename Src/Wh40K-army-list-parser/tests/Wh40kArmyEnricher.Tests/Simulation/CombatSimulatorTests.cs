using FluentAssertions;
using Wh40kArmyEnricher.Core.Contracts;
using Wh40kArmyEnricher.Core.Simulation;

namespace Wh40kArmyEnricher.Tests.Simulation;

public class CombatSimulatorTests
{
    // ─── helpers ────────────────────────────────────────────────────────────────

    private static SimAttackerProfile BasicAttacker(SimWeaponProfile weapon,
        int critHitsOn = 6, bool hitRr1 = false, bool hitRrAll = false,
        bool fishHit = false, bool woundRr1 = false, bool woundRrAll = false, bool fishWound = false)
        => new()
        {
            Name = "Attacker",
            Weapons = [weapon],
            CriticalHitsOn = critHitsOn,
            CriticalWoundsOn = 6,
            HitRerollOnes = hitRr1,
            HitRerollAll = hitRrAll,
            FishForCriticalHits = fishHit,
            WoundRerollOnes = woundRr1,
            WoundRerollAll = woundRrAll,
            FishForCriticalWounds = fishWound,
        };

    private static SimDefenderProfile BasicDefender(int t = 4, int save = 7, int wounds = 1,
        int models = 1, int? invuln = null, int? fnp = null, string[]? keywords = null)
        => new()
        {
            Name = "Defender",
            Models = models,
            Toughness = t,
            Save = save,  // 7 = impossible save so attacks always get through by default
            InvulnerableSave = invuln,
            Wounds = wounds,
            FeelNoPain = fnp,
            Keywords = keywords ?? [],
        };

    private static SimWeaponProfile MeleeWeapon(int skill = 3, int s = 4, int ap = 0,
        int damage = 1, int attacks = 1, SimWeaponAbilities? abilities = null)
        => new()
        {
            WeaponName = "Test Weapon",
            Type = WeaponType.Melee,
            Attacks = DiceExpression.Fixed(attacks),
            Skill = skill,
            Strength = s,
            Ap = ap,
            Damage = DiceExpression.Fixed(damage),
            Abilities = abilities ?? new SimWeaponAbilities(),
        };

    private static (IReadOnlyList<int> damage, IReadOnlyList<int> kills, CombatStageStats stats)
        Run1(IDiceRoller roller, SimAttackerProfile attacker, SimDefenderProfile defender)
    {
        var sim = new CombatSimulator(roller);
        var (dmg, kills, agg, _) = sim.Run(attacker, defender, 1);
        return (dmg, kills, agg);
    }

    // ─── basic attack sequence ────────────────────────────────────────────────

    [Fact]
    public void HitWoundSave_AllSucceed_DealsDamage()
    {
        // rolls: hit=5 (BS3+), wound=4 (S4 vs T4), save=2 (vs impossible 7+)
        var roller = new SequenceRoller(5, 4, 2);
        var weapon = MeleeWeapon(skill: 3, s: 4, damage: 1);
        var (dmg, kills, _) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 7));
        dmg[0].Should().Be(1);
        kills[0].Should().Be(1);
    }

    [Fact]
    public void HitRollMiss_NoDamage()
    {
        // roll hit=2 (BS3+, miss)
        var roller = new SequenceRoller(2);
        var weapon = MeleeWeapon(skill: 3);
        var (dmg, kills, _) = Run1(roller, BasicAttacker(weapon), BasicDefender());
        dmg[0].Should().Be(0);
        kills[0].Should().Be(0);
    }

    [Fact]
    public void Natural1_AlwaysFails_EvenWithPositiveModifier()
    {
        // roll=1 must always fail even if modifier would bring it to 2+
        var roller = new SequenceRoller(1);
        var weapon = MeleeWeapon(skill: 3) with { HitRollModifier = 1 };
        var (dmg, _, _) = Run1(roller, BasicAttacker(weapon), BasicDefender());
        dmg[0].Should().Be(0);
    }

    [Fact]
    public void Natural6_AlwaysHits_EvenAgainstHighSkill()
    {
        // Skill 7 (normally impossible); roll=6 always hits
        var roller = new SequenceRoller(6, 4, 2); // hit=6, wound=4, save=2
        var weapon = MeleeWeapon(skill: 7);
        var (dmg, _, _) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 7));
        dmg[0].Should().Be(1);
    }

    [Fact]
    public void HitModifier_MakesHitEasier()
    {
        // BS4+, roll=3, modifier=+1 → 3+1=4 >= 4 → hit
        var roller = new SequenceRoller(3, 4, 2); // hit=3, wound=4, save=2
        var weapon = MeleeWeapon(skill: 4) with { HitRollModifier = 1 };
        var (dmg, _, _) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 7));
        dmg[0].Should().Be(1);
    }

    [Fact]
    public void WoundRollFail_NoDamage()
    {
        // hit=5 (success), wound=2 (S3 vs T4 needs 5+, fail)
        var roller = new SequenceRoller(5, 2);
        var weapon = MeleeWeapon(skill: 3, s: 3);
        var (dmg, _, _) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 7));
        dmg[0].Should().Be(0);
    }

    [Fact]
    public void SaveSucceeds_NoDamage()
    {
        // hit=5, wound=5 (S4 vs T4 needs 4+), save=3 (vs save=3, ap=0 → needs 3+)
        var roller = new SequenceRoller(5, 5, 3);
        var weapon = MeleeWeapon(skill: 3, s: 4, ap: 0);
        var (dmg, _, _) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 3));
        dmg[0].Should().Be(0);
    }

    // ─── critical hits ────────────────────────────────────────────────────────

    [Fact]
    public void CriticalHit_NaturalSix()
    {
        // roll=6 → crit; with Lethal Hits active, auto-wound (no wound roll)
        var roller = new SequenceRoller(6, 2); // hit=6 (crit+auto-wound), save=2 (fail)
        var weapon = MeleeWeapon(skill: 3) with
        {
            Abilities = new SimWeaponAbilities { LethalHits = true }
        };
        var (dmg, kills, stats) = Run1(roller, BasicAttacker(weapon), BasicDefender(save: 7));
        dmg[0].Should().Be(1);
        stats.AvgLethalHitsAutoWounds.Should().Be(1);
    }

    [Fact]
    public void CriticalHitOn5Plus_Roll5IsCrit()
    {
        // critHitsOn=5; roll=5 → crit → with Lethal Hits, auto-wound
        var roller = new SequenceRoller(5, 2); // hit=5, save=2
        var weapon = MeleeWeapon(skill: 3) with
        {
            Abilities = new SimWeaponAbilities { LethalHits = true }
        };
        var (dmg, _, _) = Run1(roller, BasicAttacker(weapon, critHitsOn: 5), BasicDefender(save: 7));
        dmg[0].Should().Be(1);
    }

    // ─── Torrent ──────────────────────────────────────────────────────────────

    [Fact]
    public void Torrent_AutoHit_NoHitRoll()
    {
        // With Torrent, the first roll consumed is the wound roll (no hit roll)
        var roller = new SequenceRoller(4, 2); // wound=4 (S4 vs T4), save=2
        var weapon = MeleeWeapon() with
        {
            Abilities = new SimWeaponAbilities { Torrent = true }
        };
        var (dmg, _, stats) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 7));
        dmg[0].Should().Be(1);
        stats.AvgHits.Should().Be(1);
    }

    [Fact]
    public void Torrent_NoCriticalHits()
    {
        // Torrent never produces crits — Lethal Hits should not trigger
        var roller = new SequenceRoller(4, 2); // wound=4, save=2
        var weapon = MeleeWeapon() with
        {
            Abilities = new SimWeaponAbilities { Torrent = true, LethalHits = true }
        };
        var (_, _, stats) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 7));
        stats.AvgLethalHitsAutoWounds.Should().Be(0);
    }

    // ─── Lethal Hits ─────────────────────────────────────────────────────────

    [Fact]
    public void LethalHits_NonCritHit_StillRollsWound()
    {
        // roll=5 (hit, not crit at 6), LH active → wound roll still happens
        var roller = new SequenceRoller(5, 3, 2); // hit=5, wound=3 (fail vs T4), save unused
        var weapon = MeleeWeapon(skill: 3, s: 4) with
        {
            Abilities = new SimWeaponAbilities { LethalHits = true }
        };
        var (dmg, _, _) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 7));
        dmg[0].Should().Be(0);
    }

    [Fact]
    public void LethalHits_CritHit_SkipsWoundRoll()
    {
        // roll=6 (crit), LH → auto-wound, no wound roll; next roll is save
        var roller = new SequenceRoller(6, 2); // crit hit, save=2 (fail)
        var weapon = MeleeWeapon(skill: 3, damage: 2) with
        {
            Abilities = new SimWeaponAbilities { LethalHits = true }
        };
        var (dmg, _, _) = Run1(roller, BasicAttacker(weapon), BasicDefender(save: 7, wounds: 2));
        dmg[0].Should().Be(2);
    }

    // ─── Devastating Wounds ──────────────────────────────────────────────────

    [Fact]
    public void DevastatingWounds_CritWound_BypassesSave()
    {
        // hit=5, wound=6 (crit wound), DevW → no save roll, goes straight to damage
        var roller = new SequenceRoller(5, 6); // hit=5, wound=6 (crit)
        var weapon = MeleeWeapon(skill: 3, s: 4, damage: 3) with
        {
            Abilities = new SimWeaponAbilities { DevastatingWounds = true }
        };
        // save=2 (would normally succeed vs no AP), but DevW skips save
        var (dmg, _, stats) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 2));
        dmg[0].Should().Be(3);
        stats.AvgDevastatingWoundsTriggers.Should().Be(1);
    }

    [Fact]
    public void DevastatingWounds_NonCritWound_RollsSave()
    {
        // hit=5, wound=4 (normal wound, not crit), save=3 (success)
        var roller = new SequenceRoller(5, 4, 3);
        var weapon = MeleeWeapon(skill: 3, s: 4) with
        {
            Abilities = new SimWeaponAbilities { DevastatingWounds = true }
        };
        var (dmg, _, _) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 3));
        dmg[0].Should().Be(0);
    }

    // ─── Sustained Hits ───────────────────────────────────────────────────────

    [Fact]
    public void SustainedHits_CritHit_GeneratesBonusHits()
    {
        // 1 attack, crit hit → 1 bonus SH hit
        // Rolls needed: hit=6 (crit), wound1=4 (for original hit), save1=2, wound2=4, save2=2
        var roller = new SequenceRoller(6, 4, 2, 4, 2);
        var weapon = MeleeWeapon(skill: 3, s: 4, damage: 1) with
        {
            Abilities = new SimWeaponAbilities { SustainedHits = 1 }
        };
        var (dmg, _, stats) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 7));
        dmg[0].Should().Be(2);          // original hit + bonus hit
        stats.AvgHits.Should().Be(2);
        stats.AvgSustainedHitsBonus.Should().Be(1);
    }

    [Fact]
    public void SustainedHits_NonCritHit_NoBonusHit()
    {
        var roller = new SequenceRoller(5, 4, 2); // hit=5 (not crit), wound, save
        var weapon = MeleeWeapon(skill: 3, s: 4) with
        {
            Abilities = new SimWeaponAbilities { SustainedHits = 1 }
        };
        var (dmg, _, stats) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 7));
        dmg[0].Should().Be(1);
        stats.AvgSustainedHitsBonus.Should().Be(0);
    }

    // ─── Blast ───────────────────────────────────────────────────────────────

    [Fact]
    public void Blast_AddsAttacksPerFiveModels()
    {
        // 10 models → +2 attacks. 1 base attack → 3 total.
        // All 3 attacks miss (roll=1) so we can count attacks in stats.
        var roller = new ConstantRoller(1); // always roll 1 → miss
        var weapon = new SimWeaponProfile
        {
            WeaponName = "Blast",
            Type = WeaponType.Ranged,
            Attacks = DiceExpression.Fixed(1),
            Skill = 3, Strength = 4, Ap = 0,
            Damage = DiceExpression.Fixed(1),
            Abilities = new SimWeaponAbilities { Blast = true },
        };
        var defender = BasicDefender(models: 10);
        var (_, _, stats) = Run1(roller, BasicAttacker(weapon), defender);
        stats.AvgAttacks.Should().Be(3); // 1 base + 2 blast
    }

    // ─── Rapid Fire ───────────────────────────────────────────────────────────

    [Fact]
    public void RapidFire_WithinHalfRange_AddsAttacks()
    {
        var roller = new ConstantRoller(1); // all miss → count via AvgAttacks
        var weapon = new SimWeaponProfile
        {
            WeaponName = "RF",
            Type = WeaponType.Ranged,
            Attacks = DiceExpression.Fixed(2),
            Skill = 3, Strength = 4, Ap = 0,
            Damage = DiceExpression.Fixed(1),
            Abilities = new SimWeaponAbilities { RapidFire = 1 },
            WithinHalfRange = true,
        };
        var (_, _, stats) = Run1(roller, BasicAttacker(weapon), BasicDefender());
        stats.AvgAttacks.Should().Be(3); // 2 base + 1 RF
    }

    [Fact]
    public void RapidFire_OutsideHalfRange_NoBonus()
    {
        var roller = new ConstantRoller(1);
        var weapon = new SimWeaponProfile
        {
            WeaponName = "RF",
            Type = WeaponType.Ranged,
            Attacks = DiceExpression.Fixed(2),
            Skill = 3, Strength = 4, Ap = 0,
            Damage = DiceExpression.Fixed(1),
            Abilities = new SimWeaponAbilities { RapidFire = 1 },
            WithinHalfRange = false,
        };
        var (_, _, stats) = Run1(roller, BasicAttacker(weapon), BasicDefender());
        stats.AvgAttacks.Should().Be(2);
    }

    // ─── Invulnerable save ───────────────────────────────────────────────────

    [Fact]
    public void InvulnerableSave_UsedWhenBetterThanArmour()
    {
        // AP-3 (stored as -3): armour = 3 - (-3) = 6; invuln=4 is lower → invuln used
        // roll=5 → passes invuln 4+ → no damage
        var roller = new SequenceRoller(5, 4, 5); // hit=5, wound=4, save=5
        var weapon = MeleeWeapon(skill: 3, s: 4, ap: -3);
        var defender = BasicDefender(t: 4, save: 3, invuln: 4);
        var (dmg, _, stats) = Run1(roller, BasicAttacker(weapon), defender);
        dmg[0].Should().Be(0);
        stats.AvgInvulnSaveRolls.Should().Be(1);
    }

    [Fact]
    public void ArmourSave_UsedWhenBetterThanInvuln()
    {
        // Save=2, AP=0 → armour=2; invuln=5 → armour is better
        // roll=2 → passes armour 2+ → no damage
        var roller = new SequenceRoller(5, 4, 2); // hit=5, wound=4, save=2
        var weapon = MeleeWeapon(skill: 3, s: 4, ap: 0);
        var defender = BasicDefender(t: 4, save: 2, invuln: 5);
        var (dmg, _, stats) = Run1(roller, BasicAttacker(weapon), defender);
        dmg[0].Should().Be(0);
        stats.AvgArmourSaveRolls.Should().Be(1);
    }

    // ─── Feel No Pain ────────────────────────────────────────────────────────

    [Fact]
    public void FnP_ReducesDamage()
    {
        // hit=5, wound=4, save=2 (fail), damage=3, FNP5+: rolls 5,4,3 → 1 saved
        var roller = new SequenceRoller(5, 4, 2, 5, 4, 3); // +FNP rolls
        var weapon = MeleeWeapon(skill: 3, s: 4, damage: 3);
        var defender = BasicDefender(t: 4, save: 7, fnp: 5, wounds: 3);
        var (dmg, _, stats) = Run1(roller, BasicAttacker(weapon), defender);
        dmg[0].Should().Be(2);               // 3 damage − 1 FNP save
        stats.AvgFnpSaved.Should().Be(1);
    }

    // ─── Rerolls ─────────────────────────────────────────────────────────────

    [Fact]
    public void HitRerollOnes_Rerolls1s()
    {
        // Initial hit=1 (fail) → reroll → 5 (hit), then wound=4, save=2
        var roller = new SequenceRoller(1, 5, 4, 2);
        var weapon = MeleeWeapon(skill: 3, s: 4);
        var (dmg, _, _) = Run1(roller, BasicAttacker(weapon, hitRr1: true), BasicDefender(t: 4, save: 7));
        dmg[0].Should().Be(1);
    }

    [Fact]
    public void HitRerollAll_RerollsFailures()
    {
        // Initial hit=2 (fail vs BS3+) → reroll → 4 (hit), wound=4, save=2
        var roller = new SequenceRoller(2, 4, 4, 2);
        var weapon = MeleeWeapon(skill: 3, s: 4);
        var (dmg, _, _) = Run1(roller, BasicAttacker(weapon, hitRrAll: true), BasicDefender(t: 4, save: 7));
        dmg[0].Should().Be(1);
    }

    [Fact]
    public void WoundRerollOnes_Rerolls1s()
    {
        // hit=5, wound=1 (fail) → reroll → 4 (wound), save=2
        var roller = new SequenceRoller(5, 1, 4, 2);
        var weapon = MeleeWeapon(skill: 3, s: 4);
        var (dmg, _, _) = Run1(roller, BasicAttacker(weapon, woundRr1: true), BasicDefender(t: 4, save: 7));
        dmg[0].Should().Be(1);
    }

    [Fact]
    public void TwinLinked_ForcesWoundReroll()
    {
        // hit=5; wound=2 (fail vs 4+ for S4 T4) → reroll → 5 (wound); save=2
        var roller = new SequenceRoller(5, 2, 5, 2);
        var weapon = MeleeWeapon(skill: 3, s: 4) with
        {
            Abilities = new SimWeaponAbilities { TwinLinked = true }
        };
        var (dmg, _, _) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 7));
        dmg[0].Should().Be(1);
    }

    // ─── Wound pool ───────────────────────────────────────────────────────────

    [Fact]
    public void WoundPool_NoSpillover_ExcessLost()
    {
        // 3-damage weapon, 2-wound model. 3 damage dealt but only 2 absorbed; 1 excess lost.
        // rolls: hit=5, wound=4, save=2
        var roller = new SequenceRoller(5, 4, 2);
        var weapon = MeleeWeapon(skill: 3, s: 4, damage: 3);
        var defender = BasicDefender(t: 4, save: 7, wounds: 2, models: 2);
        var (dmg, kills, _) = Run1(roller, BasicAttacker(weapon), defender);
        dmg[0].Should().Be(3);   // Mean Damage = raw post-FNP before capping
        kills[0].Should().Be(1); // one model killed
    }

    // ─── Multi-attack ────────────────────────────────────────────────────────

    [Fact]
    public void MultipleAttacks_All_Hit()
    {
        // 3 attacks, all hit (roll=5), wound (roll=4), save fails (roll=2)
        var roller = new SequenceRoller(5, 4, 2, 5, 4, 2, 5, 4, 2);
        var weapon = MeleeWeapon(skill: 3, s: 4, damage: 1, attacks: 3);
        var (dmg, kills, stats) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 7, wounds: 3));
        dmg[0].Should().Be(3);
        kills[0].Should().Be(1); // single model with 3 wounds killed
        stats.AvgAttacks.Should().Be(3);
        stats.AvgHits.Should().Be(3);
    }

    // ─── Pipeline stats ──────────────────────────────────────────────────────

    [Fact]
    public void PipelineStats_TrackAllStages()
    {
        var roller = new SequenceRoller(5, 4, 2); // hit, wound, save
        var weapon = MeleeWeapon(skill: 3, s: 4, damage: 1);
        var (_, _, stats) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 7));
        stats.AvgAttacks.Should().Be(1);
        stats.AvgHits.Should().Be(1);
        stats.AvgWounds.Should().Be(1);
        stats.AvgFailedSaves.Should().Be(1);
        stats.AvgArmourSaveRolls.Should().Be(1);
        stats.AvgInvulnSaveRolls.Should().Be(0);
        stats.AvgDamageBeforeFnp.Should().Be(1);
        stats.AvgFnpSaved.Should().Be(0);
    }

    // ─── Large-run statistical sanity ────────────────────────────────────────

    [Fact]
    public void LargeRun_MeanDamage_InExpectedRange()
    {
        // S4 vs T4 → 4+ wound. BS3+. AP0, save3+ → effective save = 3.
        // P(hit BS3+) = 4/6, P(wound S4T4) = 3/6, P(fail save 3+ AP0) = 2/6
        // Expected damage per attack = (4/6)×(3/6)×(2/6)×1 = 24/216 ≈ 0.111
        var sim = new CombatSimulator();
        var weapon = MeleeWeapon(skill: 3, s: 4, ap: 0, damage: 1);
        var (dmg, _, _, _) = sim.Run(BasicAttacker(weapon), BasicDefender(t: 4, save: 3), 50_000);
        double mean = dmg.Average();
        mean.Should().BeApproximately(0.111, 0.015);
    }

    // ─── AP sign convention ──────────────────────────────────────────────────

    [Fact]
    public void ApNegative2_Raises3PlusSaveTo5Plus_SaveFailsOn4()
    {
        // Given: weapon AP-2 (stored as -2), defender save 3+
        // When:  hit=5, wound=4, save=4
        // Then:  effectiveSave = 3 - (-2) = 5; roll of 4 fails → 1 damage dealt
        var roller = new SequenceRoller(5, 4, 4);
        var weapon = MeleeWeapon(skill: 3, s: 4, ap: -2, damage: 1);
        var (dmg, _, stats) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 3));
        dmg[0].Should().Be(1);
        stats.AvgFailedSaves.Should().Be(1);
    }

    [Fact]
    public void ApNegative2_Raises3PlusSaveTo5Plus_SavePassesOn5()
    {
        // Given: weapon AP-2 (stored as -2), defender save 3+
        // When:  hit=5, wound=4, save=5
        // Then:  effectiveSave = 3 - (-2) = 5; roll of 5 passes → no damage
        var roller = new SequenceRoller(5, 4, 5);
        var weapon = MeleeWeapon(skill: 3, s: 4, ap: -2, damage: 1);
        var (dmg, _, _) = Run1(roller, BasicAttacker(weapon), BasicDefender(t: 4, save: 3));
        dmg[0].Should().Be(0);
    }
}
