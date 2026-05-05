namespace ProbHammer.Core.Simulation;

public sealed class CombatSimulator
{
    private readonly IDiceRoller _dice;

    public CombatSimulator() : this(new DiceRoller()) { }
    public CombatSimulator(IDiceRoller dice) => _dice = dice;

    public (IReadOnlyList<int> Damage, IReadOnlyList<int> Kills, CombatStageStats Aggregate, IReadOnlyList<WeaponGroupStats> PerWeapon)
        Run(SimAttackerProfile attacker, SimDefenderProfile defender, int iterations = 10_000)
    {
        var damages = new int[iterations];
        var kills = new int[iterations];
        int wc = attacker.Weapons.Count;
        var totals = new RunTotals[wc];

        for (int iter = 0; iter < iterations; iter++)
        {
            var pool = new WoundPool(defender.Models, defender.Wounds);
            var tallies = new RunTally[wc];
            int runDamage = 0;

            for (int w = 0; w < wc; w++)
            {
                SimulateWeapon(attacker.Weapons[w], attacker, defender, ref pool, ref tallies[w], ref runDamage);
                AccumulateTotals(ref totals[w], tallies[w]);
            }

            damages[iter] = runDamage;
            kills[iter] = pool.Kills;
        }

        var aggregate = BuildStats(SumTotals(totals), iterations);
        var perWeapon = attacker.Weapons.Select((w, i) => new WeaponGroupStats
        {
            WeaponName = w.WeaponName,
            Stats = BuildStats(totals[i], iterations)
        }).ToArray();

        return (damages, kills, aggregate, perWeapon);
    }

    private void SimulateWeapon(
        SimWeaponProfile weapon,
        SimAttackerProfile attacker,
        SimDefenderProfile defender,
        ref WoundPool pool,
        ref RunTally tally,
        ref int totalDamage)
    {
        // Step 1: Determine attack count
        int attacks = weapon.RerollAttackDice
            ? _dice.RollWithReroll(weapon.Attacks)
            : RollAll(weapon.Attacks);

        if (weapon.Abilities.Blast)
            attacks += defender.Models / 5;
        if (weapon.WithinHalfRange && weapon.Abilities.RapidFire > 0)
            attacks += weapon.Abilities.RapidFire;
        attacks = Math.Max(0, attacks + weapon.AttackModifier);
        tally.Attacks = attacks;

        int bonusSHHits = 0;

        // Step 2a: Process original attacks through hit roll
        for (int i = 0; i < attacks; i++)
        {
            bool isCritHit;

            if (weapon.Abilities.Torrent)
            {
                // Auto-hit; Torrent does not produce critical hits
                isCritHit = false;
                tally.Hits++;
            }
            else
            {
                int baseTarget = weapon.Abilities.IndirectFire ? 4 : weapon.Skill;
                int roll = RollHit(attacker, baseTarget);
                bool isHit = roll != 1 && (roll == 6 || roll + weapon.HitRollModifier >= baseTarget);
                isCritHit = roll >= attacker.CriticalHitsOn;

                if (!isHit) continue;

                tally.Hits++;
                if (isCritHit) tally.CritHits++;

                if (isCritHit && weapon.Abilities.SustainedHits > 0)
                {
                    bonusSHHits += weapon.Abilities.SustainedHits;
                    tally.SustainedHitsBonus += weapon.Abilities.SustainedHits;
                }
            }

            // Lethal Hits: critical hit auto-wounds (skips wound roll)
            bool lethalAutoWound = isCritHit && weapon.Abilities.LethalHits;
            ProcessWoundSaveDamage(weapon, attacker, defender, ref pool, ref tally, ref totalDamage, lethalAutoWound);
        }

        // Step 2b: Sustained Hits bonus attacks — already hits, no hit roll, not crits (no Lethal Hits)
        tally.Hits += bonusSHHits;
        for (int i = 0; i < bonusSHHits; i++)
            ProcessWoundSaveDamage(weapon, attacker, defender, ref pool, ref tally, ref totalDamage, false);
    }

    private void ProcessWoundSaveDamage(
        SimWeaponProfile weapon,
        SimAttackerProfile attacker,
        SimDefenderProfile defender,
        ref WoundPool pool,
        ref RunTally tally,
        ref int totalDamage,
        bool lethalAutoWound)
    {
        // Step 2: Wound roll
        bool isCritWound;

        if (lethalAutoWound)
        {
            // Skip wound roll; result is not a critical wound so DevW does not trigger
            isCritWound = false;
            tally.Wounds++;
            tally.LethalHitsAutoWounds++;
        }
        else
        {
            int woundTarget = AbilityProcessor.WoundThreshold(weapon.Strength, defender.Toughness);
            int roll = RollWound(weapon, attacker, woundTarget);

            // Determine critical wound threshold; Anti can lower it below CriticalWoundsOn
            int critThreshold = attacker.CriticalWoundsOn;
            bool antiLowered = false;
            foreach (var (keyword, threshold) in weapon.Abilities.Anti)
            {
                if (defender.Keywords.Any(k => string.Equals(k, keyword, StringComparison.OrdinalIgnoreCase))
                    && threshold < critThreshold)
                {
                    critThreshold = threshold;
                    antiLowered = true;
                }
            }

            bool normalWound = roll != 1 && (roll == 6 || roll + weapon.WoundRollModifier >= woundTarget);
            isCritWound = roll != 1 && roll >= critThreshold;
            bool isWound = normalWound || isCritWound;

            if (!isWound) return;

            tally.Wounds++;
            if (isCritWound)
            {
                tally.CritWounds++;
                // AntiCritWound: only a crit because Anti lowered the threshold, and a normal wound would have failed
                if (antiLowered && roll < attacker.CriticalWoundsOn && !normalWound)
                    tally.AntiCritWounds++;
            }
        }

        // Step 3: Save roll (skipped for Devastating Wounds triggers)
        if (isCritWound && weapon.Abilities.DevastatingWounds)
        {
            tally.DevastatingWoundsTriggers++;
            ApplyDamage(weapon, defender, ref pool, ref tally, ref totalDamage);
            return;
        }

        int armourSave = defender.Save - weapon.Ap;
        bool useInvuln = defender.InvulnerableSave.HasValue && defender.InvulnerableSave.Value < armourSave;
        int effectiveSave = useInvuln ? defender.InvulnerableSave!.Value : armourSave;

        int saveRoll = _dice.Roll(6);
        bool savePassed = saveRoll != 1 && saveRoll >= effectiveSave;

        if (useInvuln) tally.InvulnSaveRolls++;
        else tally.ArmourSaveRolls++;

        if (savePassed) return;

        tally.FailedSaves++;
        ApplyDamage(weapon, defender, ref pool, ref tally, ref totalDamage);
    }

    private void ApplyDamage(
        SimWeaponProfile weapon,
        SimDefenderProfile defender,
        ref WoundPool pool,
        ref RunTally tally,
        ref int totalDamage)
    {
        int dmg = weapon.RerollDamageDice
            ? _dice.RollWithReroll(weapon.Damage)
            : RollAll(weapon.Damage);

        dmg = Math.Max(1, dmg + weapon.DamageModifier);

        if (weapon.WithinHalfRange && weapon.Abilities.Melta > 0)
            dmg += weapon.Abilities.Melta;

        tally.DamageBeforeFnp += dmg;

        int fnpSaved = 0;
        if (defender.FeelNoPain.HasValue)
        {
            int fnpValue = defender.FeelNoPain.Value;
            for (int i = 0; i < dmg; i++)
                if (_dice.Roll(6) >= fnpValue) fnpSaved++;
        }
        tally.FnpSaved += fnpSaved;

        // totalDamage is post-FNP before wound pool capping
        int netDmg = dmg - fnpSaved;
        totalDamage += netDmg;
        pool.Apply(netDmg);
    }

    private int RollAll(DiceExpression expr)
    {
        if (expr.Count == 0) return expr.Modifier;
        int total = expr.Modifier;
        for (int i = 0; i < expr.Count; i++)
            total += _dice.Roll(expr.Sides);
        return total;
    }

    // Rerolls applied before modifiers; check uses unmodified roll vs unmodified target.
    private int RollHit(SimAttackerProfile attacker, int baseTarget)
    {
        int roll = _dice.Roll(6);
        bool shouldReroll;
        if (attacker.HitRerollAll && attacker.FishForCriticalHits)
            shouldReroll = roll < attacker.CriticalHitsOn;
        else if (attacker.HitRerollAll)
            shouldReroll = roll < baseTarget;
        else if (attacker.HitRerollOnes)
            shouldReroll = roll == 1;
        else
            shouldReroll = false;

        if (shouldReroll) roll = _dice.Roll(6);
        return roll;
    }

    // TwinLinked forces wound reroll all (scoped to this weapon).
    private int RollWound(SimWeaponProfile weapon, SimAttackerProfile attacker, int woundTarget)
    {
        int roll = _dice.Roll(6);
        bool rerollAll = attacker.WoundRerollAll || weapon.Abilities.TwinLinked;
        bool shouldReroll;
        if (rerollAll && attacker.FishForCriticalWounds)
            shouldReroll = roll < attacker.CriticalWoundsOn;
        else if (rerollAll)
            shouldReroll = roll < woundTarget;
        else if (attacker.WoundRerollOnes)
            shouldReroll = roll == 1;
        else
            shouldReroll = false;

        if (shouldReroll) roll = _dice.Roll(6);
        return roll;
    }

    private struct RunTally
    {
        public int Attacks, Hits, CritHits, SustainedHitsBonus;
        public int Wounds, CritWounds, LethalHitsAutoWounds, AntiCritWounds;
        public int FailedSaves, DevastatingWoundsTriggers;
        public int ArmourSaveRolls, InvulnSaveRolls;
        public int DamageBeforeFnp, FnpSaved;
    }

    private struct RunTotals
    {
        public long Attacks, Hits, CritHits, SustainedHitsBonus;
        public long Wounds, CritWounds, LethalHitsAutoWounds, AntiCritWounds;
        public long FailedSaves, DevastatingWoundsTriggers;
        public long ArmourSaveRolls, InvulnSaveRolls;
        public long DamageBeforeFnp, FnpSaved;
    }

    private static void AccumulateTotals(ref RunTotals t, RunTally r)
    {
        t.Attacks += r.Attacks;
        t.Hits += r.Hits;
        t.CritHits += r.CritHits;
        t.SustainedHitsBonus += r.SustainedHitsBonus;
        t.Wounds += r.Wounds;
        t.CritWounds += r.CritWounds;
        t.LethalHitsAutoWounds += r.LethalHitsAutoWounds;
        t.AntiCritWounds += r.AntiCritWounds;
        t.FailedSaves += r.FailedSaves;
        t.DevastatingWoundsTriggers += r.DevastatingWoundsTriggers;
        t.ArmourSaveRolls += r.ArmourSaveRolls;
        t.InvulnSaveRolls += r.InvulnSaveRolls;
        t.DamageBeforeFnp += r.DamageBeforeFnp;
        t.FnpSaved += r.FnpSaved;
    }

    private static RunTotals SumTotals(RunTotals[] totals)
    {
        var s = new RunTotals();
        foreach (var t in totals)
        {
            s.Attacks += t.Attacks;
            s.Hits += t.Hits;
            s.CritHits += t.CritHits;
            s.SustainedHitsBonus += t.SustainedHitsBonus;
            s.Wounds += t.Wounds;
            s.CritWounds += t.CritWounds;
            s.LethalHitsAutoWounds += t.LethalHitsAutoWounds;
            s.AntiCritWounds += t.AntiCritWounds;
            s.FailedSaves += t.FailedSaves;
            s.DevastatingWoundsTriggers += t.DevastatingWoundsTriggers;
            s.ArmourSaveRolls += t.ArmourSaveRolls;
            s.InvulnSaveRolls += t.InvulnSaveRolls;
            s.DamageBeforeFnp += t.DamageBeforeFnp;
            s.FnpSaved += t.FnpSaved;
        }
        return s;
    }

    private static CombatStageStats BuildStats(RunTotals t, int iterations) => new()
    {
        AvgAttacks = (double)t.Attacks / iterations,
        AvgHits = (double)t.Hits / iterations,
        AvgCritHits = (double)t.CritHits / iterations,
        AvgSustainedHitsBonus = (double)t.SustainedHitsBonus / iterations,
        AvgWounds = (double)t.Wounds / iterations,
        AvgCritWounds = (double)t.CritWounds / iterations,
        AvgLethalHitsAutoWounds = (double)t.LethalHitsAutoWounds / iterations,
        AvgAntiCritWounds = (double)t.AntiCritWounds / iterations,
        AvgFailedSaves = (double)t.FailedSaves / iterations,
        AvgDevastatingWoundsTriggers = (double)t.DevastatingWoundsTriggers / iterations,
        AvgArmourSaveRolls = (double)t.ArmourSaveRolls / iterations,
        AvgInvulnSaveRolls = (double)t.InvulnSaveRolls / iterations,
        AvgDamageBeforeFnp = (double)t.DamageBeforeFnp / iterations,
        AvgFnpSaved = (double)t.FnpSaved / iterations,
    };
}
