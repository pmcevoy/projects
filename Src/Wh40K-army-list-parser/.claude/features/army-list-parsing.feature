Feature: Army List Parsing
  As a player preparing for a game
  I want to paste my army list export into the tool
  So that it can be enriched with stat data for simulation

  Background:
    Given the army list parser is initialised

  # ── Army header ───────────────────────────────────────────────────────────────

  Scenario: Parse army name and points from header
    Given the army list text begins with "Iron Canticle (1970 Points)"
    When the list is parsed
    Then the army name is "Iron Canticle"
    And the army points total is 1970

  Scenario: Parse army name with lowercase points keyword
    Given the army list text begins with "Death Guard (1000 points)"
    When the list is parsed
    Then the army name is "Death Guard"
    And the army points total is 1000

  # ── iOS format metadata ───────────────────────────────────────────────────────

  Scenario: Parse iOS format faction and detachment
    Given the following iOS format army list header:
      """
      Black Templars (1000 Points)
      Warhammer 40,000
      Black Templars
      Righteous Crusaders
      Incursion (1000 Points)
      """
    When the list is parsed
    Then the game system is "Warhammer 40,000"
    And the faction is "Black Templars"
    And the detachment is "Righteous Crusaders"

  # ── Android format metadata ───────────────────────────────────────────────────

  Scenario: Parse Android format detachment appearing after force-size line
    Given the following Android format army list header:
      """
      Death Guard (1000 Points)
      Death Guard
      Incursion (1000 Points)
      Plague Company
      """
    When the list is parsed
    Then the faction is "Death Guard"
    And the detachment is "Plague Company"

  # ── Section categorisation ────────────────────────────────────────────────────

  Scenario: Units are assigned to their correct category
    Given an army list containing units under these section headings:
      | Section              | Unit                      |
      | CHARACTERS           | Emperor's Champion        |
      | BATTLELINE           | Assault Intercessor Squad |
      | DEDICATED TRANSPORTS | Impulsor                  |
      | OTHER DATASHEETS     | Gladiator Lancer          |
    When the list is parsed
    Then "Emperor's Champion" has category "CHARACTERS"
    And "Assault Intercessor Squad" has category "BATTLELINE"
    And "Impulsor" has category "DEDICATED TRANSPORTS"
    And "Gladiator Lancer" has category "OTHER DATASHEETS"

  # ── Unit parsing ──────────────────────────────────────────────────────────────

  Scenario: Parse unit name and points
    Given an army list containing "Assault Intercessor Squad (75 Points)"
    When the list is parsed
    Then a unit named "Assault Intercessor Squad" exists with 75 points

  Scenario: Parse unit with count-prefixed models
    Given a unit entry containing "4x Assault Intercessor"
    When the list is parsed
    Then the unit contains a model named "Assault Intercessor" with count 4

  Scenario: Parse unit name containing a right single quotation mark
    Given an army list containing a unit named with a Unicode right single quote
    When the list is parsed
    Then the unit name is preserved with the correct quotation character

  Scenario: Parse unit enhancements from iOS format
    Given a unit with an enhancement line "  ◦ Enhancements: Tannhauser's Bones"
    When the list is parsed
    Then the unit has enhancement "Tannhauser's Bones"
    And no weapon named "Enhancements: Tannhauser's Bones" is created

  Scenario: Parse unit enhancements from Android format
    Given a unit with an enhancement line "  • Enhancement: Tannhauser's Bones"
    When the list is parsed
    Then the unit has enhancement "Tannhauser's Bones"

  # ── iOS format weapon parsing ─────────────────────────────────────────────────

  Scenario: Parse iOS current format squad model with weapons
    Given an iOS current format unit block:
      """
        • Assault Intercessor
             ◦ Astartes chainsword
             ◦ Heavy bolt pistol
      """
    When the list is parsed
    Then the model "Assault Intercessor" has weapon "Astartes chainsword"
    And the model "Assault Intercessor" has weapon "Heavy bolt pistol"

  Scenario: White-circle bullet is always treated as weapon regardless of indent depth
    Given an iOS format line containing a ◦ bullet at any indentation level
    When the line is classified by ClassifyBulletLine
    Then it is classified as Level 1 regardless of indent depth

  # ── Android format weapon parsing ─────────────────────────────────────────────

  Scenario: Parse Android format continuation weapons without bullet character
    Given an Android format unit block:
      """
        • Plague Marine
            • Plague knife
            Boltgun
      """
    When the list is parsed
    Then the model "Plague Marine" has weapon "Plague knife"
    And the model "Plague Marine" has weapon "Boltgun"

  Scenario: Android bare continuation lines do not trigger model mode
    Given an Android format unit where Level-1 items have no bullet character
    When the list is parsed
    Then the unit is not parsed in model mode

  # ── Non-weapon entries ────────────────────────────────────────────────────────

  Scenario: Ability upgrade entries alongside weapons are silently accepted
    Given a unit containing bullet entry "Shield Dome" which is an ability upgrade
    When the list is parsed
    Then no warning is emitted for "Shield Dome"
    And the unit parses successfully
