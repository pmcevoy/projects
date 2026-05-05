# Combat Rules Specification — WH40K 10th Edition

This file is the authoritative reference for all simulation logic. Follow it exactly when
implementing or modifying any combat-related code.

---

## The Attack Sequence

Each attack resolves through up to four sequential steps. Some special rules skip or modify
steps. Simulate each attack individually so that per-attack special rule triggers (e.g.
Critical Hits) are correctly detected.

### Step 1 — Hit Roll

- Roll a D6 against the weapon's BS (ranged) or WS (melee). Meet or beat the value to hit.
- **Natural 1** always fails, regardless of modifiers.
- **Natural 6** always hits, regardless of modifiers — this is a Critical Hit.
- Apply rerolls before applying modifiers.
- Sum all modifiers, then cap the total at +1/-1 before applying to the target number.
- If `IndirectFire` is true, use a fixed skill target of 4 regardless of BS/WS.
- If `Torrent` is true, skip the hit roll entirely — all attacks auto-hit.
- Torrent takes priority over Indirect Fire.

### Step 2 — Wound Roll

Compare the weapon's **Strength (S)** to the target's **Toughness (T)**:

| Condition     | Required roll |
|---------------|---------------|
| S >= 2 × T    | 2+            |
| S > T         | 3+            |
| S == T        | 4+            |
| S < T         | 5+            |
| S <= T / 2    | 6+            |

- Same natural 1/6 rules and +1/-1 modifier cap as hit rolls.
- Apply rerolls before modifiers.
- Critical wound threshold checks always use the **raw unmodified die result**.
- If `LethalHits` is true and the triggering hit was a Critical Hit, skip the wound roll —
  the wound is automatic.

### Step 3 — Saving Throw

- Defender rolls D6 against their Save value. The weapon's AP modifies the save:
  `effectiveSave = save - ap`. AP is a negative integer (e.g. AP-2 → `-2`), so subtracting it
  raises the effective save threshold (e.g. save 3, AP-2: `3 - (-2) = 5`).
- If the defender has an **invulnerable save**, use whichever of armour save (AP-modified)
  or invulnerable save is numerically lower (easier to make).
- AP does **not** apply to invulnerable saves.
- **Natural 1** always fails.
- A passed save negates the wound entirely.
- If `DevastatingWounds` triggered on this wound, skip the saving throw entirely.

### Step 4 — Damage

- On a failed save (or Devastating Wounds bypass), roll the weapon's Damage characteristic.
- Damage may be a fixed integer or a dice expression (see Dice Expressions below).
- Apply `DamageModifier` (±1 per wound, minimum 1) after rolling.
- Apply Melta bonus if `withinHalfRange` is true: add `MeltaBonus` to each damage roll.
- Apply **Feel No Pain** after damage is determined and before recording (see Special Rules).
- Apply damage to the **wound pool** (see Wound Pool below).

---

## Wound Pool

A `WoundPool` struct is initialised once per simulation run, shared across all weapon groups.

- Models are tracked individually with their full wound count.
- `pool.Apply(damage)` caps damage at the current model's remaining wounds — excess is
  **lost**, not carried over to the next model.
- When a model reaches 0 wounds it is removed and `Kills` is incremented.
- `totalDamage` (raw post-FNP, before capping) and `pool.Kills` are tracked independently:
  a 3-damage weapon killing a 1-wound model shows 3 Mean Damage but 1 kill.

---

## Dice Expressions

`DiceExpression` supports:

| Expression | Meaning               |
|------------|-----------------------|
| `1`        | Fixed value of 1      |
| `D3`       | Roll 1 three-sided die |
| `D6`       | Roll 1 six-sided die  |
| `2D6`      | Roll 2D6, sum         |
| `D6+2`     | Roll D6, add 2        |
| `2D3+1`    | Roll 2D3, add 1       |

`Count=0` means a fixed value stored in `Modifier`. Has `Scale(n)` and `Add(other)` for
attack aggregation (see Multi-Weapon Aggregation below).

`RollWithReroll(DiceExpression)` on `IDiceRoller` — rolls each die individually and rerolls
once if ≤ `sides/2` (D6 threshold: 3; D3 threshold: 1). Fixed expressions pass through
unchanged.

---

## Multi-Weapon Aggregation

The simulation supports firing multiple weapons simultaneously (e.g. multiple models with
the same weapon profile firing in the same phase).

**Weapon equality key:** `(Type, Skill, Strength, Ap, Damage, Abilities)`. Attacks are
**excluded** — they are the quantity being aggregated, not part of the weapon's identity.

**Attack aggregation:**
- Fixed attacks: Σ(attacks × modelCount) → `DiceExpression.Fixed(total)`
- Dice attacks: 3 models × D6 → `3D6` (correct distribution, not roll-once-multiply)
- `DiceExpression.Add` requires same `Sides` when both have dice

**Phase constraint:** shooting and melee are mutually exclusive per simulation run. The UI
enforces this — first weapon selected locks the phase type.

---

## Weapon Abilities

All abilities are opt-in. `0` or `false` means the ability is not present.

### Torrent
Auto-hits. Skip the hit roll entirely.

### Blast
Add 1 attack per 5 defender models (rounded down) each time attacks are determined.
`defender.Models` is the value after any `DefenderModelCount` override.

### Melta X
Add X to each damage roll when `withinHalfRange` is true. Stored as a positive integer;
`0` = not present.

### Rapid Fire X
Add X additional attacks when `withinHalfRange` is true. Stored as a positive integer;
`0` = not present. Applied after attack dice roll, before `AttackModifier`.

### Sustained Hits X
Each Critical Hit on the hit roll generates X additional hits. Additional hits do not
trigger further Sustained Hits.

### Lethal Hits
A Critical Hit on the hit roll causes an automatic wound — skip the wound roll.
The auto-wound still proceeds to the saving throw.
Sustained Hits additional hits do not benefit from Lethal Hits (only original Critical Hits do).

### Devastating Wounds
A Critical Wound bypasses the saving throw. The wound deals mortal wounds equal to the
weapon's Damage characteristic. FNP still applies. Roll damage dice separately per trigger.

### Anti (keyword: threshold)
If the weapon has `anti[keyword]: x` and the defender has that keyword, an unmodified wound
roll of x or higher scores a Critical Wound. The lower of Anti threshold and `CriticalWoundsOn`
wins. Stored as `Dictionary<string, int>`.

### Twin-Linked
Re-roll the wound roll for each attack made with this weapon. Applied as `woundRerollAll`
scoped to this weapon.

### Indirect Fire
Use a fixed skill target of 4 regardless of BS/WS. Torrent takes priority.

---

## Special Rules

### Critical Hit
Natural 6 on the hit roll (or lower if `CriticalHitsOn` is reduced, e.g. `criticalHitsOn: 5`
means natural 5 or 6). Detection uses the **raw unmodified die result**.

### Critical Wound
Natural 6 on the wound roll (or lower if `CriticalWoundsOn` is reduced by Anti or the
Crit Wound on 5+ modifier). Detection uses the **raw unmodified die result**.

### Rerolls

Configured on `SimAttackerProfile`:

| Field | Effect |
|---|---|
| `HitRerollOnes` | Reroll hit rolls of 1 before modifiers |
| `HitRerollAll` | Reroll all failed hit rolls before modifiers |
| `WoundRerollOnes` | Reroll wound rolls of 1 |
| `WoundRerollAll` | Reroll all failed wound rolls |
| `FishForCriticalHits` | Sub-option of HitRerollAll — reroll any result below `criticalHitsOn` |
| `FishForCriticalWounds` | Sub-option of WoundRerollAll — reroll any result below `criticalWoundsOn` |

Each die may only be rerolled once. `RerollAll` takes precedence over `RerollOnes`.

### Feel No Pain (FNP)
After a failed save or Devastating Wounds trigger, roll a D6 per wound point. On a result
≥ the FNP value, that wound is ignored. Stored as a raw integer (e.g. `5` means 5+++).

### Invulnerable Save
Unaffected by AP. Defender uses whichever of armour save (AP-modified) or invulnerable save
is numerically lower. Stored as a raw integer (e.g. `4` means 4++). `null` if absent.

---

## AP Sign Convention

AP is stored as a **negative integer** everywhere — in `UnitProfile`, `SimWeaponProfile`, and
all intermediate types — matching the game value (e.g. AP-2 → `-2`).

`SimulationAdapter` passes AP through unchanged; no negation.

`AbilityProcessor.EffectiveSave` uses: `effectiveSave = save - ap`. Since `ap` is negative,
subtracting it raises the effective save threshold (e.g. AP-2: `save - (-2) = save + 2`).
A positive AP value would correspondingly lower the threshold (easier save for the defender).

The `+1/-1 AP` UI modifier adds directly to the stored AP value (`ap += modifier`). A modifier
of -1 makes AP more negative (more penetrating, harder save, more damage); +1 makes it less
negative (less penetrating, easier save, less damage).

---

## Simulation Output

`CombatSimulator.Run()` returns:
- `IReadOnlyList<int> Damage` — raw total damage per run (post-FNP, pre-wound-pool-cap)
- `IReadOnlyList<int> Kills` — model kills per run
- `CombatStageStats Aggregate` — per-stage averages across all weapon groups
- `IReadOnlyList<WeaponGroupStats> PerWeapon` — per-group breakdown (one entry per distinct weapon group)

`SimulationResponse` exposes: mean damage, expected kills, P(kill ≥ 1), stddev, `StageStats`,
and `WeaponBreakdown` (empty for single-group runs).

---

## Combat Stage Statistics Fields

Tracked per weapon group, summed for aggregate:

| Field | What it counts |
|---|---|
| `AvgAttacks` | Attack dice rolled (including Rapid Fire, excluding SH bonus hits) |
| `AvgHits` | Attacks that hit (including SH bonus hits; Torrent auto-hits counted) |
| `AvgCritHits` | Hits that were Critical Hits |
| `AvgSustainedHitsBonus` | Extra hits from Sustained Hits |
| `AvgWounds` | Successful wound rolls (including Lethal Hits auto-wounds) |
| `AvgCritWounds` | Wounds scoring a Critical Wound |
| `AvgLethalHitsAutoWounds` | Wounds that bypassed the wound roll via Lethal Hits |
| `AvgAntiCritWounds` | Wounds that became crit wounds only because Anti lowered threshold below 6 |
| `AvgFailedSaves` | Save rolls that failed (excludes DevW bypasses) |
| `AvgDevastatingWoundsTriggers` | Wounds that bypassed the save via Devastating Wounds |
| `AvgArmourSaveRolls` | Rolls made against the armour save |
| `AvgInvulnSaveRolls` | Rolls made against the invulnerable save |
| `AvgDamageBeforeFnp` | Raw damage reaching the FNP step |
| `AvgFnpSaved` | Damage points negated by FNP |
