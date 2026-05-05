Feature: Web Application
  As a player at the table with a phone or tablet
  I want to paste two army lists and run simulations instantly
  So that I can make tactical decisions during the game

  # ── Army submission and enrichment ───────────────────────────────────────────

  Scenario: Submitting two army lists enriches and stores them in session
    Given a player pastes a valid attacker army list
    And a player pastes a valid defender army list
    When the form is submitted
    Then both armies are enriched against BSData
    And the enriched profiles are stored in session
    And the player is redirected to the ArmyView page

  Scenario: ArmyView page displays catalogue names and revision numbers
    Given two army lists have been enriched using specific BSData catalogues
    When the ArmyView page is loaded
    Then the catalogue names used by these armies are shown
    And their revision numbers are shown

  # ── Unit card interaction ─────────────────────────────────────────────────────

  Scenario: Unit cards are collapsed by default
    Given the ArmyView page is loaded
    When the page renders
    Then all unit cards are in a collapsed state

  Scenario: Clicking a unit card header expands it
    Given a collapsed unit card for "Assault Intercessor Squad"
    When the card header is clicked
    Then the card expands showing the unit's weapon profiles

  Scenario: Clicking an attacker weapon row selects it and highlights red
    Given an expanded attacker unit card
    When a weapon variant row is clicked
    Then the row is highlighted red
    And the attacker unit is automatically selected

  Scenario: Clicking a defender unit selects it and highlights blue
    Given a rendered ArmyView page
    When a defender unit card is clicked
    Then the unit card is highlighted blue

  Scenario: Selecting both attacker weapon and defender unit reveals the combat panel
    Given an attacker weapon is selected
    When a defender unit is selected
    Then the combat panel appears at the bottom of the page

  # ── Phase locking ────────────────────────────────────────────────────────────

  Scenario: Selecting a ranged weapon locks out melee weapon rows
    Given an attacker with both ranged and melee weapons
    When a ranged weapon is selected
    Then melee weapon rows are rendered with locked styling
    And clicks on melee weapon rows are silently rejected

  Scenario: Clearing all weapon selections resets the phase lock
    Given a ranged weapon is currently selected
    When all weapon selections are cleared
    Then melee weapon rows become selectable again

  # ── Multi-weapon selection ────────────────────────────────────────────────────

  Scenario: Multiple weapons of the same profile aggregate into one simulation group
    Given a Marshal with Master-crafted Power Weapon A7 and a Castellan with A6
    When both weapons are selected
    Then the simulation runs with a single weapon group of 13 attacks

  Scenario: Each weapon group has its own inline model count input
    Given two distinct weapon groups are selected
    When the combat panel renders
    Then each weapon group shows its own model count input

  Scenario: Updating a weapon group's model count updates that group independently
    Given two weapon groups are selected with different model counts
    When the model count for one group is changed
    Then only that group's attack count is affected

  # ── Simulation execution ─────────────────────────────────────────────────────

  Scenario: Clicking Run Simulation returns results without page reload
    Given an attacker weapon and defender unit are selected
    When Run Simulation is clicked
    Then results appear inline on the page
    And the page does not reload

  Scenario: Simulation results display mean damage, expected kills, P(kill >= 1), and stddev
    Given a simulation has been run
    When results are displayed
    Then mean damage is shown
    And expected kills is shown
    And probability of at least one kill is shown
    And standard deviation is shown

  Scenario: Attack pipeline funnel is displayed after simulation
    Given a simulation has been run
    When results are displayed
    Then an attack pipeline funnel is shown with per-stage averages

  Scenario: Multi-weapon simulation shows per-group breakdown followed by combined summary
    Given a simulation was run with two distinct weapon groups
    When results are displayed
    Then each weapon group has its own labelled pipeline section
    And a combined summary table appears after the per-group sections

  # ── Combat modifiers ──────────────────────────────────────────────────────────

  Scenario: Combat panel modifier sections are collapsible
    Given the combat panel is visible
    When a modifier section header is clicked
    Then the section expands or collapses

  Scenario: Active modifiers are summarised in the section header
    Given the Hit Modifiers section has Reroll All and Crit 5+ active
    When the section is collapsed
    Then the header shows a summary like "RR All · Crit 5+"

  Scenario: Cover modifier adds 1 to defender armour save in simulation
    Given a defender with Sv3+
    And the Cover modifier is enabled
    When the simulation is run
    Then the effective armour save used is 2+

  Scenario: Ignores Cover cancels the Cover modifier
    Given both Cover and Ignores Cover are enabled
    When the simulation is run
    Then the cover bonus is not applied to the defender's save

  # ── Catalogue refresh ─────────────────────────────────────────────────────────

  Scenario: Re-download catalogues button refreshes BSData for current session
    Given two armies are loaded in session using specific catalogue IDs
    When the Re-download catalogues button is clicked
    Then a POST to /api/refresh-catalogues is made
    And the catalogues used by the current session are re-fetched from BSData
    And the server does not restart

  # ── Session persistence ───────────────────────────────────────────────────────

  Scenario: Navigating back to ArmyView retains enriched army data
    Given two armies have been enriched and stored in session
    When the player navigates to the ArmyView page again
    Then both armies are still available without re-enrichment
