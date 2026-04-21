Feature: Combat Simulation
  As a player at the table
  I want to run Monte Carlo simulations of weapon attacks against a target unit
  So that I can make informed tactical decisions

  Background:
    Given the combat simulator is initialised with 100000 runs
    And results are compared within a tolerance of 0.02

  # ── Basic attack sequence ─────────────────────────────────────────────────────

  Scenario: Bolt rifle versus Space Marine produces statistically correct mean damage
    Given an attacker with a Bolt Rifle: A2 BS3+ S4 AP0 D1
    And a defender with T4 Sv3+ W2 and no invulnerable save
    When the simulation is run
    Then mean damage is approximately 0.74 per attack

  Scenario: Natural 1 always fails the hit roll regardless of modifiers
    Given an attacker with BS2+ and a +1 hit roll modifier
    When many hit rolls of natural 1 are made
    Then all natural 1s fail

  Scenario: Natural 6 always succeeds the hit roll regardless of modifiers
    Given an attacker with BS5+ and a -1 hit roll modifier
    When many hit rolls of natural 6 are made
    Then all natural 6s succeed

  Scenario: Hit roll modifiers are capped at net plus or minus one
    Given an attacker with two sources of +1 hit roll modifier
    When the simulation is run
    Then the effective modifier applied is +1, not +2

  Scenario: BS characteristic modifier and roll modifier stack independently
    Given an attacker with BS4+, a +1 BS characteristic modifier, and a +1 hit roll modifier
    When the simulation is run
    Then the effective hit target is 2+ with a +1 roll modifier applied

  # ── Wound roll ────────────────────────────────────────────────────────────────

  Scenario Outline: Wound threshold is calculated correctly from S vs T
    Given an attacker with Strength <strength>
    And a defender with Toughness <toughness>
    When the wound roll threshold is calculated
    Then the required roll is <required>

    Examples:
      | strength | toughness | required |
      | 8        | 4         | 2        |
      | 5        | 4         | 3        |
      | 4        | 4         | 4        |
      | 3        | 4         | 5        |
      | 2        | 5         | 6        |

  # ── Saving throw ─────────────────────────────────────────────────────────────

  Scenario: AP modifies the armour save
    Given a defender with Sv3+ facing a weapon with AP-2
    When the save roll is made
    Then the effective save target is 5+

  Scenario: Invulnerable save is used when better than AP-modified armour save
    Given a defender with Sv3+ and invulnerable save 4+
    And a weapon with AP-3
    When the save roll is made
    Then the invulnerable save of 4+ is used

  Scenario: AP does not modify the invulnerable save
    Given a defender with invulnerable save 4+
    And a weapon with AP-3
    When the save roll is made against the invulnerable save
    Then the target is still 4+

  Scenario: Natural 1 always fails the save roll
    Given a defender with Sv2+
    When a save roll of natural 1 is made
    Then the save fails

  # ── Damage and wound pool ─────────────────────────────────────────────────────

  Scenario: Excess damage on a model is lost not carried over
    Given a defender with W1 models
    And a weapon with damage 3
    When one attack gets through
    Then 1 model is killed
    And total damage recorded is 3
    And kills recorded is 1

  Scenario: Variable damage rolls produce correct distribution
    Given a weapon with D6 damage
    When the simulation is run
    Then mean damage per wound is approximately 3.5

  Scenario: Feel No Pain reduces damage after failed save
    Given a defender with feelNoPain 5+
    And a weapon that would deal 6 damage
    When the simulation is run
    Then approximately one third of damage points are negated by FNP

  # ── Weapon abilities ──────────────────────────────────────────────────────────

  Scenario: Torrent weapon skips the hit roll
    Given a weapon with Torrent
    When the simulation is run
    Then all attacks proceed directly to the wound roll

  Scenario: Sustained Hits 1 generates one additional hit on a Critical Hit
    Given a weapon with Sustained Hits 1 and BS3+
    When the simulation is run
    Then approximately one sixth of hits generate one bonus hit

  Scenario: Lethal Hits auto-wounds on a Critical Hit
    Given a weapon with Lethal Hits and BS3+
    When the simulation is run
    Then approximately one sixth of attacks auto-wound skipping the wound roll

  Scenario: Devastating Wounds bypasses save and deals damage as mortal wounds
    Given a weapon with Devastating Wounds
    When a Critical Wound is scored
    Then the saving throw is skipped
    And damage is applied directly subject to FNP only

  Scenario: Blast adds attacks based on defender model count
    Given a weapon with Blast
    And a defender unit of 10 models
    When attacks are determined
    Then 2 bonus attacks are added (10 / 5 = 2)

  Scenario: Rapid Fire adds attacks within half range
    Given a weapon with Rapid Fire 1
    And withinHalfRange is true
    When attacks are determined
    Then 1 additional attack is added

  Scenario: Rapid Fire does not add attacks beyond half range
    Given a weapon with Rapid Fire 1
    And withinHalfRange is false
    When attacks are determined
    Then no additional attacks are added

  Scenario: Melta bonus applies to damage within half range
    Given a weapon with Melta 2 and D6 damage
    And withinHalfRange is true
    When damage is rolled
    Then 2 is added to each damage roll

  Scenario: Anti keyword triggers Critical Wound on qualifying target
    Given a weapon with Anti-Infantry 4+
    And a defender with the INFANTRY keyword
    When a wound roll of 4 is made
    Then it counts as a Critical Wound

  Scenario: Anti keyword does not trigger on non-matching target
    Given a weapon with Anti-Infantry 4+
    And a defender without the INFANTRY keyword
    When a wound roll of 4 is made
    Then it is resolved as a normal wound roll

  Scenario: Indirect Fire uses flat 4+ to hit regardless of BS
    Given a weapon with Indirect Fire and BS2+
    When hit rolls are made
    Then the hit target is 4+

  Scenario: Torrent takes priority over Indirect Fire
    Given a weapon with both Torrent and Indirect Fire
    When hit rolls are made
    Then the hit roll is skipped entirely

  # ── Rerolls ───────────────────────────────────────────────────────────────────

  Scenario: Reroll ones rerolls only hit rolls of 1
    Given an attacker with hitRerollOnes and BS4+
    When the simulation is run
    Then only dice results of 1 are rerolled on hit rolls

  Scenario: Reroll all rerolls all failed hit rolls
    Given an attacker with hitRerollAll and BS4+
    When the simulation is run
    Then all failed hit rolls are rerolled once

  Scenario: Fish for Criticals rerolls non-critical hits when criticalHitsOn is 6
    Given an attacker with hitRerollAll, FishForCriticalHits, and criticalHitsOn 6
    When the simulation is run
    Then any hit result below 6 is rerolled, accepting only natural 6s

  Scenario: Each die is rerolled at most once
    Given any reroll rule active on hit or wound rolls
    When the simulation is run
    Then no die result is rerolled more than once

  Scenario: Twin-Linked allows wound roll reroll
    Given a weapon with Twin-Linked
    When a wound roll fails
    Then it may be rerolled once

  # ── Critical Hit threshold ────────────────────────────────────────────────────

  Scenario: CriticalHitsOn 5 means natural 5 or 6 triggers critical hit effects
    Given an attacker with criticalHitsOn 5 and a weapon with Sustained Hits 1
    When hit rolls are made
    Then natural 5 and natural 6 both trigger Sustained Hits

  Scenario: Critical hit detection uses raw die result before modifiers
    Given an attacker with criticalHitsOn 6 and a -1 hit roll modifier
    When a hit roll of natural 6 is made
    Then it is still a Critical Hit

  # ── Multi-weapon simulation ───────────────────────────────────────────────────

  Scenario: Multiple models with identical weapon profile aggregate attacks correctly
    Given 3 models each with a weapon of A2 BS3+ S4 AP0 D1
    When the simulation is run
    Then total attacks per run equal 6

  Scenario: Multiple models with D6 attacks each roll independently
    Given 3 models each with D6 attacks
    When attacks are determined
    Then each model rolls its own D6 independently (result is 3D6, not 1D6 multiplied by 3)

  Scenario: Shooting and melee weapons cannot be simulated simultaneously
    Given a selection containing both a ranged weapon and a melee weapon
    When the simulation is attempted
    Then an error is returned indicating phase conflict

  Scenario: AttackModifier is applied after Blast and Rapid Fire bonuses
    Given a weapon with Blast, Rapid Fire 1, and AttackModifier +1
    And withinHalfRange is true and defender has 10 models
    When attacks are determined
    Then the sequence is: base attacks + Blast bonus + Rapid Fire bonus + AttackModifier

  # ── Surviving models override ─────────────────────────────────────────────────

  Scenario: DefenderModelCount override changes wound pool size
    Given a defender unit with 5 models in the session profile
    And the simulation request specifies DefenderModelCount 3
    When the simulation is run
    Then the wound pool is initialised with 3 models not 5

  Scenario: DefenderModelCount of zero uses the session profile model count
    Given a defender unit with 5 models in the session profile
    And the simulation request specifies DefenderModelCount 0
    When the simulation is run
    Then the wound pool is initialised with 5 models
