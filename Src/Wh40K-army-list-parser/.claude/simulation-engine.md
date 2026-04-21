# Simulation Engine

Ported from the retired `wh40k-sim` standalone project. Located in `Wh40kArmyEnricher.Core/Simulation/`.

The full 40K combat rules and attack sequence spec: @rules/combat-rules.md

---

## Key Types

| Type | Purpose |
|---|---|
| `DiceExpression` | Parses `"D6"`, `"2D3+1"`, fixed integers; `Count=0` means fixed value in `Modifier`; has `Scale(n)` and `Add(other)` for attack aggregation |
| `IDiceRoller` / `DiceRoller` | Abstracts randomness; injectable for deterministic testing; `RollWithReroll(expr)` rerolls each die independently if ≤ sides/2 |
| `SimAttackerProfile` | Name, Weapons, Rerolls, `CriticalHitsOn`, `CriticalWoundsOn` (default 6), `HitRollModifier`, `WoundRollModifier`, `FishForCriticalHits`, `FishForCriticalWounds` |
| `SimDefenderProfile` | Name, model count, T, Sv, invuln, W, FNP, keywords |
| `CombatSimulator` | Runs N iterations; returns `(IReadOnlyList<int> Damage, IReadOnlyList<int> Kills, CombatStageStats Aggregate, IReadOnlyList<WeaponGroupStats> PerWeapon)` |
| `CombatStageStats` | Per-run averages for each pipeline stage and ability contribution |
| `WeaponGroupStats` | `{ WeaponName, CombatStageStats Stats }` |
| `SimulationResult` | Computed statistics: mean, median, stddev, min, max, probability/cumulative distributions |
| `AbilityProcessor` | Pure static helpers: `WoundThreshold(S,T)`, `EffectiveSave(defender, ap)` |

---

## Simulation Flow (per run)

Loop over each weapon in `Attacker.Weapons`:

1. Roll attack dice (with optional per-die reroll) → apply Blast bonus → apply Rapid Fire bonus → apply `AttackModifier` offset
2. For each attack: hit roll (skip if Torrent; use 4+ if Indirect Fire) → Sustained Hits bonus attacks
3. Wound roll (skip if Lethal Hit) → save roll (skip if Devastating Wounds) → roll damage (with optional per-die reroll) → apply `DamageModifier` → FNP rolls → apply damage to wound pool

Rules: each die may only be rerolled once. Natural 1 always fails; natural 6 (or lower if `CriticalHitsOn`/`CriticalWoundsOn` is reduced) always succeeds.

**Wound pool (no damage spillover):** `WoundPool` struct initialised once per run, shared across all weapon groups. `pool.Apply(damage)` caps damage at the current model's remaining wounds — excess is lost, not carried to the next model. When a model reaches 0 wounds it is removed and `Kills` is incremented.

`SimulateOneRun` returns `(totalDamage, pool.Kills)`. `totalDamage` is raw post-FNP before capping (drives `Mean Damage`); `Kills` drives `ExpectedKills` and `P(kill ≥ 1)`. The two metrics are independent: a 3-damage weapon killing a 1-wound model shows 3 Mean Damage but 1 kill.

---

## Multi-Weapon Selection and Attack Aggregation

The simulation supports firing multiple weapons simultaneously (e.g. Marshal + Castellan + Sword Brethren all firing Master-crafted Power Weapon in the same fight phase).

**Weapon equality:** two weapon selections are the same profile if their `(Type, Skill, Strength, Ap, Damage, Abilities)` match. Attacks are **excluded** from the equality key — they are the quantity being aggregated.

**Attack aggregation** via `SimulationAdapter.AggregateAttacks()` using `DiceExpression.Scale(n)` and `DiceExpression.Add(other)`:
- Fixed attacks: Σ(attacks × modelCount) → `DiceExpression.Fixed(total)`
- Dice attacks: 3 models × D6 → `3D6` (correct distribution, not roll-once-multiply)
- `DiceExpression.Add` requires same `Sides` when both have dice (throws for mixed D3/D6)

**Phase constraint:** shooting and melee occur in different game phases; it is invalid to simulate both simultaneously. The UI enforces this — first weapon selected locks the type; opposite-type rows get `.weapon-type-locked` styling and clicks are silently rejected. Resets when all selections are cleared.

**`SimulationRequest.WeaponSelections`** — `List<WeaponSelection>`, each carrying `{ WeaponName, VariantName, ModelName, ModelCount }`. For single-weapon selections, `ModelCount` may be overridden by the user; for multi-weapon, each group has its own inline count input.

**`SimulationRequest.DefenderModelCount`** — `0` means use `defender.ModelCount` from session; positive value overrides it in `SimulationAdapter`.

---

## Combat Stage Statistics

`CombatSimulator.Run()` returns raw per-run damage list, aggregate `CombatStageStats`, and per-weapon `IReadOnlyList<WeaponGroupStats>`. Displayed in the UI as an "Attack Pipeline" funnel.

**Implementation:** two private structs:
- `RunTally` (int fields) — per-run counters, passed via `ref`; cleared between runs using a reused `RunTally[]` buffer (one slot per weapon, `Array.Clear` each run — no per-run heap allocations)
- `RunTotals` (long fields) — cross-run accumulators, one per weapon

**Pipeline fields tracked per weapon:**

| Field | What it counts |
|---|---|
| `AvgAttacks` | Total attack dice rolled (including Rapid Fire bonus, excluding SH bonus hits) |
| `AvgHits` | Attacks that hit (including SH bonus hits; Torrent auto-hits) |
| `AvgCritHits` | Hits that were natural-6 (or lower if `CriticalHitsOn` reduced) |
| `AvgSustainedHitsBonus` | Extra hits from Sustained Hits |
| `AvgWounds` | Successful wound rolls (including Lethal Hits auto-wounds) |
| `AvgCritWounds` | Wounds scoring a critical wound |
| `AvgLethalHitsAutoWounds` | Wounds that bypassed the wound roll via Lethal Hits |
| `AvgAntiCritWounds` | Wounds that became crit wounds only because Anti lowered threshold below 6 |
| `AvgFailedSaves` | Save rolls that failed (excludes DevW bypasses) |
| `AvgDevastatingWoundsTriggers` | Wounds that bypassed the save roll via Devastating Wounds |
| `AvgArmourSaveRolls` | Save rolls made against the armour save |
| `AvgInvulnSaveRolls` | Save rolls made against the invulnerable save |
| `AvgDamageBeforeFnp` | Raw damage that reached the FNP step |
| `AvgFnpSaved` | Damage points negated by Feel No Pain rolls |

**Save type logic:** `RollSave` replicates `AbilityProcessor.EffectiveSave` inline: if `defender.InvulnerableSave.HasValue && invuln < armourSave`, invuln save is used.

---

## SimulationAdapter

Bridges `UnitProfile` (Contracts) → `SimulationConfig` (Core.Simulation):

- Groups `WeaponSelections` by `WeaponGroupKey` (`type + Skill + S + AP + D + Abilities`; Anti normalised to sorted `"kw:val,..."` string for dictionary equality)
- Aggregates attacks per group via `DiceExpression.Scale` + `DiceExpression.Add`
- **Negates AP:** `simAp = -contractsAp` (AP is negative in Contracts, positive in the sim engine)
- Applies cover by adding 1 to `SimDefenderProfile.Save` before the run
- Returns `SimulationResponse` with mean damage, expected kills, P(kill ≥ 1), stddev, `StageStats` (aggregate), and `WeaponBreakdown` (per-group, empty when single group)

---

## Combat Options (User-Controlled Modifiers)

All simulation modifiers are set explicitly by the user — ability text is not auto-parsed.

The combat panel is organised into five collapsible sections plus a top-level "Models firing" control. Each section header shows a live one-line summary of active modifiers (e.g. `½ Range · RR All · Crit 5+`).

### Attack Modifiers
| Control | Effect |
|---|---|
| **+1/-1 Attack** | Adds/subtracts 1 from total attack count per weapon group, after Blast and Rapid Fire |
| **Blast** | Override toggle — enables Blast on weapons that don't already have it; adds 1 attack per 5 defender models |
| **Reroll attack dice** | Reroll variable-attack dice once if below expected average (≤3 on D6, ≤1 on D3); applied per model contribution before aggregation |

### Hit Modifiers
| Control | Effect |
|---|---|
| **Within half range** | Enables Rapid Fire and Melta bonuses |
| **+1/-1 Hit** | Roll modifier, capped at net ±1; natural 1 still fails, natural 6 always hits |
| **+1/-1 BS/WS** | Characteristic modifier, changes effective BS/WS by ±1 step; tracks separately from roll modifier |
| **Reroll 1s** | Reroll hit rolls of 1. Mutually exclusive with Reroll All |
| **Reroll All** | Reroll all failed hit rolls. Mutually exclusive with Reroll 1s |
| **Fish for Criticals** | Sub-option of Reroll All only — reroll any result below `criticalHitsOn` |
| **Indirect Fire** | Weapon hits on flat 4+ regardless of BS |
| **Crit Hit on 5+** | Lowers `CriticalHitsOn` to 5 |
| **Sustained Hits 1** | Override toggle — grants Sustained Hits 1 to weapons that don't already have it |
| **Lethal Hits** | Override toggle — grants Lethal Hits to weapons that don't already have it |

### Wound Modifiers
| Control | Effect |
|---|---|
| **+1/-1 Wound** | Roll modifier, capped at net ±1; critical wound threshold checks use raw unmodified die |
| **+1/-1 Strength** | Adjusts attacker's effective Strength by ±1 for wound table lookup |
| **+1/-1 Toughness** | Adjusts defender's effective Toughness by ±1 |
| **Reroll 1s** | Reroll wound rolls of 1 |
| **Reroll All** | Reroll all failed wound rolls (also applies to Twin-Linked) |
| **Fish for Criticals** | Sub-option of Wound Reroll All — reroll any wound result below `criticalWoundsOn` |
| **Crit Wound on 5+** | Lowers critical wound threshold to 5; independent of Anti thresholds |
| **Devastating Wounds** | Override toggle |
| **Anti-X** | Override — user configures keyword type and threshold; lower threshold wins |

### Save Modifiers
| Control | Effect |
|---|---|
| **Cover** | Adds +1 to defender's armour save; does not affect invuln |
| **Ignores Cover** | Negates Cover bonus if both active |
| **+1/-1 AP** | Adjusts weapon AP by ±1 |

### Damage Modifiers
| Control | Effect |
|---|---|
| **+1/-1 Damage** | Adds/subtracts 1 from each damage roll after rolling, per wound, before FNP; clamped to minimum 1 |
| **Reroll damage dice** | Reroll variable-damage dice once if below expected average |
| **Feel No Pain** | Override — grants defender a FNP save (4+++, 5+++, or 6+++) if they don't already have one |

### Modifier stacking rules
- Roll modifier cap: +1/-1 Hit and +1/-1 Wound are each capped at net ±1. BS/WS characteristic modifier is separate and uncapped.
- Ability overrides (Blast, Sustained, Lethals, Dev Wounds, Anti): OR the flag / merge value into `SimWeaponAbilities`. Weapons that already have the ability are unaffected.
- Fish for Criticals replaces normal reroll condition: `raw < criticalHitsOn` (hits) or `raw < criticalWoundsOn` (wounds).
- Cover + Ignores Cover: if both set, cover bonus not applied.
- FNP override: `defender.FeelNoPain ?? (request.FnpOverride > 0 ? request.FnpOverride : null)` — native value always wins.
- Blast and Rapid Fire fully simulated in `SimulateOneRun`: Blast adds `defender.Models / 5` attacks; Rapid Fire adds `RapidFire` attacks when `WithinHalfRange` is true. Both applied after attack dice roll, before `AttackModifier` offset.
- `AttackModifier` is a field on `SimWeaponProfile` (not in `DiceExpression`): `attacks = Math.Max(0, attacks + weapon.AttackModifier)`.
- `RollWithReroll(DiceExpression)`: rolls each die individually, rerolls once if ≤ `sides/2`. Fixed expressions pass through unchanged.
- Indirect Fire: `RollHit` uses fixed skill target of 4. Torrent still takes priority (hit roll skipped entirely).

---

## UI Display of Pipeline Results

`displayPipeline()` in `army-view.js` generates all pipeline HTML into `#pipeline-content`:

- **Single weapon group:** full funnel table with ability sub-rows; Final Damage at bottom. Sub-rows hidden when value < 0.001.
- **Multiple weapon groups:** one labelled full-funnel section per group (`.pipeline-weapon-header`), followed by a compact "Combined" summary table (main stages only, no rates) ending with Final Damage.

`SimulationResponse.WeaponBreakdown` is empty for single-group runs (use `StageStats` directly); populated only when multiple groups exist.
