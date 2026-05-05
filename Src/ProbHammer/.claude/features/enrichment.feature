Feature: Army Enrichment
  As a player who has pasted their army list
  I want the tool to look up each unit's stats from BSData
  So that I have accurate profiles for simulation

  Background:
    Given the BSData catalogues are loaded

  # ── Name resolution ───────────────────────────────────────────────────────────

  Scenario: Resolve unit by exact name match
    Given a BSData catalogue containing a unit named "Assault Intercessor Squad"
    When the army list contains a unit named "Assault Intercessor Squad"
    Then the unit is resolved to the BSData entry

  Scenario: Resolve unit after stripping count prefix
    Given a BSData catalogue containing a unit named "Assault Intercessor Squad"
    When the army list contains "3x Assault Intercessor Squad"
    Then the unit is resolved to the BSData entry

  Scenario: Resolve unit by fuzzy match above threshold
    Given a BSData catalogue containing a unit named "Deathshroud Terminator Champion"
    When the army list contains "Deathshroud Champion"
    Then the unit is resolved to "Deathshroud Terminator Champion"
    And a warning is logged containing the input name, matched name, and similarity score

  Scenario: Manual override takes precedence over all automatic matching
    Given a name_overrides.json mapping "Deathshroud Champion" to "Deathshroud Terminator Champion"
    When the army list contains "Deathshroud Champion"
    Then the unit is resolved to "Deathshroud Terminator Champion" via the override
    And no fuzzy match warning is emitted

  Scenario: Unresolvable unit is skipped with a structured warning
    Given a BSData catalogue that does not contain "Unknown Unit"
    When the army list contains "Unknown Unit"
    Then "Unknown Unit" is skipped
    And a structured warning is emitted containing the unit name
    And no default statline is substituted

  Scenario: Model name resolved by prefix match for loadout variants
    Given a BSData catalogue containing "Initiate w/Bolt Rifle" and "Initiate w/Chainsword"
    When the army list contains a model named "Initiate"
    Then the model is resolved by prefix match to a matching loadout variant

  # ── Statline extraction ───────────────────────────────────────────────────────

  Scenario: Extract statline from squad model entry
    Given a BSData catalogue with Assault Intercessor Squad containing model "Assault Intercessor"
    When the army list is enriched
    Then the resolved model has toughness 4, save 3, wounds 2

  Scenario: Extract statline from single-model unit entry
    Given a BSData catalogue with a single-model unit "Foetid Bloat-drone"
    When the army list is enriched
    Then the statline is taken from the unit entry itself, not a child model

  Scenario: Extract weapon profiles from model entry
    Given a BSData catalogue entry for "Assault Intercessor" containing "Astartes chainsword"
    When the army list is enriched
    Then the weapon "Astartes chainsword" has AP -1 and damage 1

  Scenario: Extract both variants from a multi-profile weapon
    Given a BSData catalogue entry for "Hellforged weapons" with strike and sweep profiles
    When the army list is enriched
    Then the weapon has variant "strike" and variant "sweep"
    And the variant label is derived by stripping the entry name prefix

  # ── Invulnerable saves and FNP ────────────────────────────────────────────────

  Scenario: Extract invulnerable save from ability text
    Given a BSData entry with ability text "The bearer has a 5+ invulnerable save."
    When the army list is enriched
    Then the model has invulnerableSave 5

  Scenario: Extract invulnerable save from infoLink pointing to shared profile
    Given a BSData entry with an infoLink named "Invulnerable Save" referencing a shared profile
    And the shared profile description is "4+"
    When the army list is enriched
    Then the model has invulnerableSave 4

  Scenario: Extract Feel No Pain from ability text
    Given a BSData entry with ability text "Feel No Pain 5+"
    When the army list is enriched
    Then the model has feelNoPain 5

  Scenario: Cross-catalogue infoLink resolves correctly
    Given a Black Templars unit with an infoLink targeting a shared profile in Space Marines catalogue
    When the army list is enriched
    Then the shared profile is resolved across catalogue boundaries

  Scenario: Ability upgrade grants invulnerable save to single-model unit
    Given a single-model unit with a "Shield Dome" upgrade in the army list
    And the Shield Dome catalogue entry grants a 5+ invulnerable save
    When the army list is enriched
    Then the unit has invulnerableSave 5

  # ── Keyword extraction ────────────────────────────────────────────────────────

  Scenario: Extract unit keywords from BSData category links
    Given a BSData entry for "Assault Intercessor Squad" with categories INFANTRY, CORE, ADEPTUS ASTARTES
    When the army list is enriched
    Then the unit profile has keywords INFANTRY, CORE, ADEPTUS ASTARTES

  Scenario: Weapon keywords drive simulation ability flags
    Given a BSData weapon profile with Keywords containing "Lethal Hits"
    When the army list is enriched
    Then the weapon profile has lethalHits true

  Scenario: Weapon keywords containing "Sustained Hits 1" set sustainedHits to 1
    Given a BSData weapon profile with Keywords "Sustained Hits 1"
    When the army list is enriched
    Then the weapon profile has sustainedHits 1

  # ── Catalogue version tracking ────────────────────────────────────────────────

  Scenario: Enrichment records the catalogue IDs used
    Given an army list containing units from the Black Templars and Space Marines catalogues
    When the army list is enriched
    Then the used catalogue ID set includes the Black Templars catalogue ID
    And the used catalogue ID set includes the Space Marines catalogue ID

  Scenario: Catalogue refresh re-downloads and re-parses without full server restart
    Given a session with used_catalogue_ids from a previous enrichment
    When a POST to /api/refresh-catalogues is made
    Then the relevant catalogues are re-fetched from BSData
    And the in-memory catalogue store is updated
    And the session catalogue revision numbers are updated
