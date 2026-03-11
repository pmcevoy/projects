using Wh40kSim.Models;

namespace Wh40kSim.Simulation;

public sealed class CombatSimulator
{
    private readonly IDiceRoller _dice;

    public CombatSimulator(IDiceRoller dice)
    {
        _dice = dice;
    }

    public IReadOnlyList<int> Run(SimulationConfig config)
    {
        var results = new List<int>(config.SimulationRuns);
        for (int i = 0; i < config.SimulationRuns; i++)
            results.Add(SimulateOneRun(config.Attacker, config.Defender));
        return results;
    }

    private int SimulateOneRun(AttackerProfile attacker, DefenderProfile defender)
    {
        var weapon = attacker.Weapon;

        // Determine per-model attack count
        int weaponAttacks = _dice.Roll(weapon.Attacks);

        // Apply Blast: minimum 3 if defender has 6+ models
        if (weapon.Abilities.Blast && defender.Models >= 6)
            weaponAttacks = Math.Max(weaponAttacks, 3);

        // Total attacks across all models
        int totalAttacks = weaponAttacks * attacker.Models;

        // Apply Rapid Fire: +X attacks per model if within half range
        if (weapon.WithinHalfRange && weapon.Abilities.RapidFire > 0)
            totalAttacks += weapon.Abilities.RapidFire * attacker.Models;

        int totalDamage = 0;
        for (int i = 0; i < totalAttacks; i++)
            totalDamage += ResolveOneAttack(weapon, attacker.Rerolls, defender, isFromSustainedHits: false);

        return totalDamage;
    }

    private int ResolveOneAttack(
        WeaponProfile weapon,
        RerollOptions rerolls,
        DefenderProfile defender,
        bool isFromSustainedHits)
    {
        int damage = 0;
        bool hitNaturalSix = false;

        // Step 1: Hit roll
        // Torrent = auto-hit; sustained-hit bonus attacks ARE hits already (skip roll)
        if (!weapon.Abilities.Torrent && !isFromSustainedHits)
        {
            bool hit = RollHit(weapon, rerolls, out hitNaturalSix);
            if (!hit) return 0;
        }

        // Sustained Hits: natural 6 on hit generates X bonus attacks (no chaining)
        if (hitNaturalSix && weapon.Abilities.SustainedHits > 0 && !isFromSustainedHits)
        {
            for (int s = 0; s < weapon.Abilities.SustainedHits; s++)
                damage += ResolveOneAttack(weapon, rerolls, defender, isFromSustainedHits: true);
        }

        // Lethal Hits: natural 6 on hit = auto-wound (not for sustained-hit bonus attacks)
        bool isLethalHit = hitNaturalSix && weapon.Abilities.LethalHits && !isFromSustainedHits;

        // Step 2: Wound roll
        bool wounded = RollWound(weapon, defender, rerolls, isLethalHit,
            out bool woundNaturalSix, out bool devastatingWound);

        if (devastatingWound)
        {
            // Mortal wounds equal to Damage; skip save; FNP still applies
            int rawDmg = _dice.Roll(weapon.Damage);
            if (weapon.WithinHalfRange && weapon.Abilities.Melta > 0)
                rawDmg += weapon.Abilities.Melta;
            damage += ApplyDamageWithFnp(rawDmg, defender);
            return damage;
        }

        if (!wounded) return damage;

        // Step 3: Saving throw
        if (RollSave(weapon, defender)) return damage;

        // Step 4: Damage
        int d = _dice.Roll(weapon.Damage);
        if (weapon.WithinHalfRange && weapon.Abilities.Melta > 0)
            d += weapon.Abilities.Melta;
        damage += ApplyDamageWithFnp(d, defender);
        return damage;
    }

    /// <returns>true if the hit succeeds; sets <paramref name="naturalSix"/> if raw roll was 6.</returns>
    private bool RollHit(WeaponProfile weapon, RerollOptions rerolls, out bool naturalSix)
    {
        int raw = _dice.RollD6();

        // Rerolls are applied before modifiers; check against unmodified skill
        bool shouldReroll =
            rerolls.HitRerollAll  ? (raw != 6 && raw < weapon.Skill) :
            rerolls.HitRerollOnes ? (raw == 1) :
            false;

        if (shouldReroll)
            raw = _dice.RollD6();

        // Natural 1/6 on the kept die
        if (raw == 1) { naturalSix = false; return false; }
        if (raw == 6) { naturalSix = true;  return true;  }

        naturalSix = false;
        return raw >= weapon.Skill;
    }

    /// <returns>true if the wound succeeds.</returns>
    private bool RollWound(
        WeaponProfile weapon,
        DefenderProfile defender,
        RerollOptions rerolls,
        bool isLethalHit,
        out bool naturalSix,
        out bool devastatingWound)
    {
        if (isLethalHit)
        {
            naturalSix = false;
            devastatingWound = false;
            return true;
        }

        int raw = _dice.RollD6();
        int threshold = AbilityProcessor.WoundThreshold(weapon.Strength, defender.Toughness);

        bool shouldReroll =
            rerolls.WoundRerollAll  ? (raw != 6 && raw < threshold) :
            rerolls.WoundRerollOnes ? (raw == 1) :
            false;

        if (shouldReroll)
            raw = _dice.RollD6();

        if (raw == 1) { naturalSix = false; devastatingWound = false; return false; }

        if (raw == 6)
        {
            naturalSix = true;
            devastatingWound = weapon.Abilities.DevastatingWounds;
            return true;
        }

        naturalSix = false;
        devastatingWound = false;
        return raw >= threshold;
    }

    /// <returns>true if the save passes (wound is negated).</returns>
    private bool RollSave(WeaponProfile weapon, DefenderProfile defender)
    {
        int raw = _dice.RollD6();
        if (raw == 1) return false;

        int effectiveSave = AbilityProcessor.EffectiveSave(defender, weapon.Ap);
        return raw >= effectiveSave;
    }

    private int ApplyDamageWithFnp(int rawDamage, DefenderProfile defender)
    {
        if (!defender.FeelNoPain.HasValue)
            return rawDamage;

        int fnpValue = defender.FeelNoPain.Value;
        int survived = 0;
        for (int i = 0; i < rawDamage; i++)
        {
            if (_dice.RollD6() < fnpValue)
                survived++;
        }
        return survived;
    }
}
