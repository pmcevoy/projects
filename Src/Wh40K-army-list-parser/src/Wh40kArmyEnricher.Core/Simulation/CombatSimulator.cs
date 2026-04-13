namespace Wh40kArmyEnricher.Core.Simulation;

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

    private int SimulateOneRun(SimAttackerProfile attacker, SimDefenderProfile defender)
    {
        var weapon = attacker.Weapon;

        int weaponAttacks = _dice.Roll(weapon.Attacks);

        if (weapon.Abilities.Blast)
            weaponAttacks += defender.Models / 5;

        int totalAttacks = weaponAttacks * attacker.Models;

        if (weapon.WithinHalfRange && weapon.Abilities.RapidFire > 0)
            totalAttacks += weapon.Abilities.RapidFire * attacker.Models;

        int criticalWoundsOn = ComputeCriticalWoundsOn(weapon, defender);

        int totalDamage = 0;
        for (int i = 0; i < totalAttacks; i++)
            totalDamage += ResolveOneAttack(weapon, attacker.Rerolls, defender, attacker.CriticalHitsOn, criticalWoundsOn, isFromSustainedHits: false);

        return totalDamage;
    }

    private int ResolveOneAttack(
        SimWeaponProfile weapon,
        SimRerollOptions rerolls,
        SimDefenderProfile defender,
        int criticalHitsOn,
        int criticalWoundsOn,
        bool isFromSustainedHits)
    {
        int damage = 0;
        bool isCriticalHit = false;

        if (!weapon.Abilities.Torrent && !isFromSustainedHits)
        {
            bool hit = RollHit(weapon, rerolls, criticalHitsOn, out isCriticalHit);
            if (!hit) return 0;
        }

        if (isCriticalHit && weapon.Abilities.SustainedHits > 0 && !isFromSustainedHits)
        {
            for (int s = 0; s < weapon.Abilities.SustainedHits; s++)
                damage += ResolveOneAttack(weapon, rerolls, defender, criticalHitsOn, criticalWoundsOn, isFromSustainedHits: true);
        }

        bool isLethalHit = isCriticalHit && weapon.Abilities.LethalHits && !isFromSustainedHits;

        bool wounded = RollWound(weapon, defender, rerolls, isLethalHit, criticalWoundsOn, out bool devastatingWound);

        if (devastatingWound)
        {
            int rawDmg = _dice.Roll(weapon.Damage);
            if (weapon.WithinHalfRange && weapon.Abilities.Melta > 0)
                rawDmg += weapon.Abilities.Melta;
            damage += ApplyDamageWithFnp(rawDmg, defender);
            return damage;
        }

        if (!wounded) return damage;

        if (RollSave(weapon, defender)) return damage;

        int d = _dice.Roll(weapon.Damage);
        if (weapon.WithinHalfRange && weapon.Abilities.Melta > 0)
            d += weapon.Abilities.Melta;
        damage += ApplyDamageWithFnp(d, defender);
        return damage;
    }

    private bool RollHit(SimWeaponProfile weapon, SimRerollOptions rerolls, int criticalHitsOn, out bool isCriticalHit)
    {
        int raw = _dice.RollD6();

        bool shouldReroll =
            rerolls.HitRerollAll  ? (raw < criticalHitsOn && raw < weapon.Skill) :
            rerolls.HitRerollOnes ? (raw == 1) :
            false;

        if (shouldReroll)
            raw = _dice.RollD6();

        if (raw == 1) { isCriticalHit = false; return false; }

        if (raw >= criticalHitsOn) { isCriticalHit = true; return true; }

        isCriticalHit = false;
        return raw >= weapon.Skill;
    }

    private bool RollWound(
        SimWeaponProfile weapon,
        SimDefenderProfile defender,
        SimRerollOptions rerolls,
        bool isLethalHit,
        int criticalWoundsOn,
        out bool devastatingWound)
    {
        if (isLethalHit)
        {
            devastatingWound = false;
            return true;
        }

        int raw = _dice.RollD6();
        int threshold = AbilityProcessor.WoundThreshold(weapon.Strength, defender.Toughness);

        bool canRerollAll = rerolls.WoundRerollAll || weapon.Abilities.TwinLinked;
        bool shouldReroll =
            canRerollAll              ? (raw < criticalWoundsOn && raw < threshold) :
            rerolls.WoundRerollOnes   ? (raw == 1) :
            false;

        if (shouldReroll)
            raw = _dice.RollD6();

        if (raw == 1) { devastatingWound = false; return false; }

        if (raw >= criticalWoundsOn)
        {
            devastatingWound = weapon.Abilities.DevastatingWounds;
            return true;
        }

        devastatingWound = false;
        return raw >= threshold;
    }

    private static int ComputeCriticalWoundsOn(SimWeaponProfile weapon, SimDefenderProfile defender)
    {
        int threshold = 6;
        foreach (var (keyword, value) in weapon.Abilities.Anti)
        {
            if (defender.Keywords.Any(k => string.Equals(k, keyword, StringComparison.OrdinalIgnoreCase)))
                threshold = Math.Min(threshold, value);
        }
        return threshold;
    }

    private bool RollSave(SimWeaponProfile weapon, SimDefenderProfile defender)
    {
        int raw = _dice.RollD6();
        if (raw == 1) return false;

        int effectiveSave = AbilityProcessor.EffectiveSave(defender, weapon.Ap);
        return raw >= effectiveSave;
    }

    private int ApplyDamageWithFnp(int rawDamage, SimDefenderProfile defender)
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
