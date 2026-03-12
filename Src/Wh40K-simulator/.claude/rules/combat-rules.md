# Combat Rules Specification — WH40K 10th Edition

This file is the authoritative reference for all simulation logic. Follow it exactly when
implementing or modifying any combat-related code.

---

## The Attack Sequence

Each attack resolves through up to four sequential steps. Some special rules skip or modify steps.
Simulate each attack individually, not in aggregate, so that per-attack special rule triggers
(e.g. Critical Hits) are correctly detected.

### Step 1 — Hit Roll

- Roll a D6 against the weapon's BS (ranged) or WS (melee). Meet or beat the value to hit.
- **Natural 1** always fails, regardless of modifiers.
- **Natural 6** always hits, regardless of modifiers. See Critical Hit below.
- Apply rerolls before applying modifiers.
- Sum all modifiers, then cap the total at +1/-1 before applying to the target number.

### Step 2 — Wound Roll

Compare the weapon's **Strength (S)** to the target's **Toughness (T)**:

| Condition       | Required roll |
|-----------------|---------------|
| S >= 2 × T      | 2+            |
| S > T           | 3+            |
| S == T          | 4+            |
| S < T           | 5+            |
| S <= T / 2      | 6+            |

- Same natural 1/6 rules and modifier cap (+1/-1) as hit rolls. See Critical Wound section below
- Apply rerolls before modifiers.

### Step 3 — Saving Throw

- Defender rolls D6 against their Save, modified by the weapon's AP (subtract AP from save value).
- If the defender has an **invulnerable save**, they choose whichever save is easier to make.
  AP does **not** apply to invulnerable saves.
- **Natural 1** always fails.
- A passed save negates the wound entirely.

### Step 4 — Damage

- On a failed save, apply the weapon's **Damage** characteristic to the target's wounds.
- Damage may be a fixed number or a dice expression (see Dice Expressions below).
- Track total wounds inflicted across the unit. Do not model individual model wound pools unless
  required by a future feature — for now, sum damage against total unit wounds.
- Apply **Feel No Pain** after the save fails and before recording damage (see Special Rules).

---

## Dice Expressions

Support the following formats wherever attacks or damage values are specified in YAML:

| Expression | Meaning                        |
|------------|--------------------------------|
| `1`        | Fixed value of 1               |
| `D3`       | Roll 1 three-sided die         |
| `D6`       | Roll 1 six-sided die           |
| `2D6`      | Roll 2 six-sided dice, sum     |
| `D6+2`     | Roll D6, add 2                 |
| `2D3+1`    | Roll 2D3, add 1                |

Parse these at load time into a structured type (`DiceExpression`) with `count`, `sides`,
and `modifier` fields. Roll them in `DiceRoller`.

---

## Weapon Abilities

All abilities are opt-in. Absence of an ability in the YAML means it does not apply.

### Torrent
- The weapon automatically hits. Skip the hit roll entirely for all attacks.

### Blast
- Each time you determine how many attacks are made with a Blast weapon, add 1 to the result for every five models that were in the target unit when you selected it as the target (rounding down).
- Model count comes from `defender.models` in the profile.

### Melta X
- If `withinHalfRange: true` on the attacker profile, add X to each damage roll.
- X is the numeric value of the Melta ability (e.g. `melta: 2` adds 2 to damage).

### Rapid Fire X
- If `withinHalfRange: true`, add X additional attacks to the weapon's attack count.

### Sustained Hits X
- Each **Critical Hit** on the hit roll generates X additional hits.
- These additional hits do not trigger further Sustained Hits.

### Lethal Hits
- A **Critical Hit** on the hit roll causes an automatic wound. Skip the wound roll for that hit.
- The automatic wound still proceeds to the saving throw.
- Compatible with Sustained Hits — additional hits from Sustained Hits do not benefit from
  Lethal Hits unless that hit roll is itself a Critical Hit.

### Devastating Wounds
- A **Critical Wound** on the wound roll causes **mortal wounds** equal to the weapon's Damage.
- Skip the saving throw for that wound. Feel No Pain still applies.
- Roll damage dice separately for each Devastating Wounds trigger.

### Anti
- If a weapon has `abilities.anti.keyword: x` and `defender.keywords` contains `keyword`, then an unmodified Wound roll of ‘x+’ scores a Critical Wound.

### Twin-linked
- Each time an attack is made with a Twin-Linked weapon, you can re-roll that attack’s Wound roll.
- Identfied by `twinLinked: true` on the weapon's abilities

---

## Core Special Rules

### Critical Hit
- The hit is always successful
- Usually a natural 6 on the hit roll
- Some attacker profiles can adjust this lower, specified by `criticalHitsOn: 5`, meaning that a natural 5 or 6 on the dice roll is a Critical Hit

### Critical Wound
- The wound is always successful
- Usually a natural 6 on the wound roll
- Some weapon abilities can adjust this lower

### Rerolls

Support the following independently for hit and wound steps:

- `hitRerollOnes: true` — reroll hit roll results of 1 (before modifiers)
- `hitRerollAll: true` — reroll all failed hit rolls (before modifiers)
- `woundRerollOnes: true` — reroll wound roll results of 1
- `woundRerollAll: true` — reroll all failed wound rolls

Each die may only be rerolled once. `rerollAll` takes precedence over `rerollOnes` if both
are somehow set.

### Feel No Pain (FNP)

- After a failed save (or after a Devastating Wounds trigger), roll a D6 for each wound.
- On a result equal to or greater than the FNP value, the wound is ignored.
- Specified as `feelNoPain: 5` (meaning 5+) in the defender profile.

### Invulnerable Save

- A separate save value unaffected by AP.
- The defender always uses whichever of their armour save (modified by AP) or invulnerable
  save is numerically lower (easier to make).
- Specified as `invulnerableSave: 4` in the defender profile.

---

## YAML Profile Schema

### Top-level config keys

```yaml
simulationRuns: 100000       # optional, default 100000
attacker:
  ...
defender:
  ...
```

### Attacker profile

```yaml
attacker:
  name: "Intercessor Squad"
  models: 5
  weapon:
    name: "Bolt Rifle"
    attacks: 2               # fixed int or dice expression string e.g. "D6"
    skill: 3                 # hit on 3+
    strength: 4
    ap: 1                    # stored as positive int; subtract from save
    damage: 1                # fixed int or dice expression string
    abilities:
      torrent: false
      blast: false
      melta: 0               # 0 means not active; set to X for Melta X
      rapidFire: 1           # 0 means not active; set to X for Rapid Fire X
      sustainedHits: 0       # 0 means not active; set to X for Sustained Hits X
      lethalHits: false
      devastatingWounds: false
      anti:
        Psyker: 4
        Character: 2
      twinLinked: false
    withinHalfRange: false
  rerolls:
    hitRerollOnes: false
    hitRerollAll: false
    woundRerollOnes: false
    woundRerollAll: false
  criticalHitsOn: 5          # the minimum value on the hit roll to count as a Critical Hit
```

### Defender profile

```yaml
defender:
  name: "Space Marine Squad"
  models: 10
  toughness: 4
  save: 3                    # armour save, e.g. 3 means 3+
  invulnerableSave: null     # null or an int e.g. 4
  wounds: 2                  # wounds per model (used for total pool = models × wounds)
  feelNoPain: null           # null or an int e.g. 5
  keywords:
    - Psyker
    - Vehicle
    - Character
```

---

## Output Requirements

### Console summary

After simulation, print:

```
--- Results: Intercessors vs Space Marines (100,000 runs) ---
Mean damage  : 4.21
Median       : 4
Std deviation: 1.87
Min          : 0
Max          : 12

Damage  Probability  Cumulative
0       3.21%        3.21%
1       7.45%        10.66%
...
```

### ASCII bar chart

Render using **Spectre.Console**. Damage values on the X axis, probability % on the Y axis.
Keep it readable in an 80-column terminal.

### CSV export (`--csv` flag)

Write a file named `<config-filename>-results.csv` in the same directory as the config file.
Columns: `Damage,Probability,CumulativeProbability`

---

## Implementation Notes

- Simulate each **individual attack** through the full sequence — do not batch rolls.
- For each simulation run, loop over `attacker.models × weapon.attacks` attacks.
- Resolve Blast minimum attack count once per run, before the attack loop.
- Roll Melta and Rapid Fire additional attacks/damage only when `withinHalfRange: true`.
- Natural 6 detection must happen on the **raw die result**, before any modifier is applied.
- Rerolls must be applied **before** modifier caps are calculated.
- Total damage per run = sum of all damage that passes the full sequence (including FNP).
- Record total damage per run in a results list; derive the distribution from that list.
