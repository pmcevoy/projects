namespace Wh40kArmyEnricher.Core.Simulation;

public sealed class CombatSimulator
{
    private readonly IDiceRoller _dice;

    public CombatSimulator(IDiceRoller dice)
    {
        _dice = dice;
    }

    // Per-run counters (int — reset each run, never overflow within a single run).
    private struct RunTally
    {
        public int Attacks;
        public int Hits;
        public int CritHits;
        public int Wounds;
        public int CritWounds;
        public int FailedSaves;
        public int DamageBeforeFnp;
        public int FnpSaved;
        public int SustainedHitsBonus;
        public int LethalHitsAutoWounds;
        public int DevastatingWoundsTriggers;
        public int AntiCritWounds;
        public int ArmourSaveRolls;
        public int InvulnSaveRolls;
    }

    // Cross-run accumulator (long — avoids overflow at 10k+ runs).
    private struct RunTotals
    {
        public long Attacks, Hits, CritHits, Wounds, CritWounds;
        public long FailedSaves, DamageBeforeFnp, FnpSaved;
        public long SustainedHitsBonus, LethalHitsAutoWounds, DevastatingWoundsTriggers, AntiCritWounds;
        public long ArmourSaveRolls, InvulnSaveRolls;

        public void Add(in RunTally t)
        {
            Attacks += t.Attacks; Hits += t.Hits; CritHits += t.CritHits;
            Wounds += t.Wounds; CritWounds += t.CritWounds;
            FailedSaves += t.FailedSaves; DamageBeforeFnp += t.DamageBeforeFnp;
            FnpSaved += t.FnpSaved; SustainedHitsBonus += t.SustainedHitsBonus;
            LethalHitsAutoWounds += t.LethalHitsAutoWounds;
            DevastatingWoundsTriggers += t.DevastatingWoundsTriggers;
            AntiCritWounds += t.AntiCritWounds;
            ArmourSaveRolls += t.ArmourSaveRolls; InvulnSaveRolls += t.InvulnSaveRolls;
        }

        public void Add(in RunTotals other)
        {
            Attacks += other.Attacks; Hits += other.Hits; CritHits += other.CritHits;
            Wounds += other.Wounds; CritWounds += other.CritWounds;
            FailedSaves += other.FailedSaves; DamageBeforeFnp += other.DamageBeforeFnp;
            FnpSaved += other.FnpSaved; SustainedHitsBonus += other.SustainedHitsBonus;
            LethalHitsAutoWounds += other.LethalHitsAutoWounds;
            DevastatingWoundsTriggers += other.DevastatingWoundsTriggers;
            AntiCritWounds += other.AntiCritWounds;
            ArmourSaveRolls += other.ArmourSaveRolls; InvulnSaveRolls += other.InvulnSaveRolls;
        }
    }

    public (IReadOnlyList<int> Damage, CombatStageStats Aggregate, IReadOnlyList<WeaponGroupStats> PerWeapon) Run(SimulationConfig config)
    {
        int weaponCount = config.Attacker.Weapons.Count;
        var results = new List<int>(config.SimulationRuns);

        var weaponTotals = new RunTotals[weaponCount];
        var weaponTallies = new RunTally[weaponCount]; // reused buffer, cleared each run

        for (int i = 0; i < config.SimulationRuns; i++)
        {
            Array.Clear(weaponTallies, 0, weaponCount);
            results.Add(SimulateOneRun(config.Attacker, config.Defender, weaponTallies));
            for (int w = 0; w < weaponCount; w++)
                weaponTotals[w].Add(weaponTallies[w]);
        }

        double n = config.SimulationRuns;

        // Aggregate = element-wise sum of all per-weapon totals.
        var aggregateTotals = new RunTotals();
        for (int w = 0; w < weaponCount; w++)
            aggregateTotals.Add(weaponTotals[w]);

        var aggregate = ToStats(aggregateTotals, n);

        var perWeapon = config.Attacker.Weapons
            .Select((w, idx) => new WeaponGroupStats
            {
                WeaponName = w.Name,
                Stats = ToStats(weaponTotals[idx], n),
            })
            .ToList();

        return (results, aggregate, perWeapon);
    }

    private static CombatStageStats ToStats(in RunTotals t, double n) => new()
    {
        AvgAttacks                   = t.Attacks / n,
        AvgHits                      = t.Hits / n,
        AvgCritHits                  = t.CritHits / n,
        AvgWounds                    = t.Wounds / n,
        AvgCritWounds                = t.CritWounds / n,
        AvgFailedSaves               = t.FailedSaves / n,
        AvgDamageBeforeFnp           = t.DamageBeforeFnp / n,
        AvgFnpSaved                  = t.FnpSaved / n,
        AvgSustainedHitsBonus        = t.SustainedHitsBonus / n,
        AvgLethalHitsAutoWounds      = t.LethalHitsAutoWounds / n,
        AvgDevastatingWoundsTriggers = t.DevastatingWoundsTriggers / n,
        AvgAntiCritWounds            = t.AntiCritWounds / n,
        AvgArmourSaveRolls           = t.ArmourSaveRolls / n,
        AvgInvulnSaveRolls           = t.InvulnSaveRolls / n,
    };

    private int SimulateOneRun(SimAttackerProfile attacker, SimDefenderProfile defender, RunTally[] weaponTallies)
    {
        int totalDamage = 0;
        for (int wi = 0; wi < attacker.Weapons.Count; wi++)
        {
            var weapon = attacker.Weapons[wi];
            int attacks = _dice.Roll(weapon.Attacks);
            weaponTallies[wi].Attacks = attacks;
            int criticalWoundsOn = ComputeCriticalWoundsOn(weapon, defender);
            for (int i = 0; i < attacks; i++)
                totalDamage += ResolveOneAttack(weapon, attacker.Rerolls, defender,
                    attacker.CriticalHitsOn, criticalWoundsOn, isFromSustainedHits: false,
                    ref weaponTallies[wi]);
        }
        return totalDamage;
    }

    private int ResolveOneAttack(
        SimWeaponProfile weapon,
        SimRerollOptions rerolls,
        SimDefenderProfile defender,
        int criticalHitsOn,
        int criticalWoundsOn,
        bool isFromSustainedHits,
        ref RunTally tally)
    {
        int damage = 0;
        bool isCriticalHit = false;

        if (isFromSustainedHits)
        {
            tally.Hits++;
            tally.SustainedHitsBonus++;
        }
        else if (weapon.Abilities.Torrent)
        {
            tally.Hits++;
        }
        else
        {
            bool hit = RollHit(weapon, rerolls, criticalHitsOn, out isCriticalHit);
            if (!hit) return 0;
            tally.Hits++;
            if (isCriticalHit) tally.CritHits++;
        }

        if (isCriticalHit && weapon.Abilities.SustainedHits > 0 && !isFromSustainedHits)
        {
            for (int s = 0; s < weapon.Abilities.SustainedHits; s++)
                damage += ResolveOneAttack(weapon, rerolls, defender, criticalHitsOn, criticalWoundsOn,
                    isFromSustainedHits: true, ref tally);
        }

        bool isLethalHit = isCriticalHit && weapon.Abilities.LethalHits && !isFromSustainedHits;

        bool wounded = RollWound(weapon, defender, rerolls, isLethalHit, criticalWoundsOn,
            out bool devastatingWound, ref tally);

        if (devastatingWound)
        {
            tally.DevastatingWoundsTriggers++;
            int rawDmg = _dice.Roll(weapon.Damage);
            if (weapon.WithinHalfRange && weapon.Abilities.Melta > 0)
                rawDmg += weapon.Abilities.Melta;
            damage += ApplyDamageWithFnp(rawDmg, defender, ref tally);
            return damage;
        }

        if (!wounded) return damage;

        bool savedRoll = RollSave(weapon, defender, ref tally);
        if (savedRoll) return damage;

        tally.FailedSaves++;
        int d = _dice.Roll(weapon.Damage);
        if (weapon.WithinHalfRange && weapon.Abilities.Melta > 0)
            d += weapon.Abilities.Melta;
        damage += ApplyDamageWithFnp(d, defender, ref tally);
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
        out bool devastatingWound,
        ref RunTally tally)
    {
        if (isLethalHit)
        {
            tally.Wounds++;
            tally.LethalHitsAutoWounds++;
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
            tally.Wounds++;
            tally.CritWounds++;
            if (criticalWoundsOn < 6 && raw < 6)
                tally.AntiCritWounds++;
            devastatingWound = weapon.Abilities.DevastatingWounds;
            return true;
        }

        devastatingWound = false;
        if (raw >= threshold)
        {
            tally.Wounds++;
            return true;
        }
        return false;
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

    private bool RollSave(SimWeaponProfile weapon, SimDefenderProfile defender, ref RunTally tally)
    {
        int armourSave = defender.Save + weapon.Ap;
        bool usingInvuln = defender.InvulnerableSave.HasValue
            && defender.InvulnerableSave.Value < armourSave;

        if (usingInvuln)
            tally.InvulnSaveRolls++;
        else
            tally.ArmourSaveRolls++;

        int raw = _dice.RollD6();
        if (raw == 1) return false;

        int effectiveSave = usingInvuln ? defender.InvulnerableSave!.Value : armourSave;
        return raw >= effectiveSave;
    }

    private int ApplyDamageWithFnp(int rawDamage, SimDefenderProfile defender, ref RunTally tally)
    {
        tally.DamageBeforeFnp += rawDamage;

        if (!defender.FeelNoPain.HasValue)
            return rawDamage;

        int fnpValue = defender.FeelNoPain.Value;
        int damageThroughFnp = 0;
        for (int i = 0; i < rawDamage; i++)
        {
            if (_dice.RollD6() < fnpValue)
                damageThroughFnp++;
            else
                tally.FnpSaved++;
        }
        return damageThroughFnp;
    }
}
