// WH40K Army Tool — Army View interactive logic

(function () {
    'use strict';

    // State
    const selectedAttackers = new Map(); // index -> unitProfile
    let selectedDefenderIndex = null;
    let selectedDefenderUnit = null;
    let selectedWeapon = null; // { weaponName, variantName, modelName, modelCount, rowElement }

    const combatPanel = document.getElementById('combat-panel');
    const selectionSummary = document.getElementById('selection-summary');
    const weaponDisplay = document.getElementById('weapon-display');
    const attackingModelsInput = document.getElementById('attacking-models');
    const runSimBtn = document.getElementById('run-sim-btn');
    const resultsPanel = document.getElementById('results-panel');
    const loadingOverlay = document.getElementById('loading-overlay');

    // ---------------------------------------------------------------------------
    // Unit card click handling (header area — selection toggle)
    // ---------------------------------------------------------------------------

    document.querySelectorAll('.unit-card').forEach(card => {
        card.addEventListener('click', function (e) {
            // Clicks inside the expanded detail body are handled separately
            if (e.target.closest('.collapse')) return;

            const role = card.dataset.role;
            const index = parseInt(card.dataset.index, 10);
            const unit = JSON.parse(card.dataset.unit);

            if (role === 'attacker') {
                toggleAttacker(card, index, unit);
            } else if (role === 'defender') {
                selectDefender(card, index, unit);
            }

            updateUI();
        });
    });

    function toggleAttacker(card, index, unit) {
        if (selectedAttackers.has(index)) {
            selectedAttackers.delete(index);
            card.classList.remove('selected-attacker');
            // Clear weapon if it belonged to this unit
            if (selectedWeapon && selectedWeapon.unitIndex === index) {
                clearWeaponSelection();
            }
        } else {
            selectedAttackers.set(index, unit);
            card.classList.add('selected-attacker');
        }
    }

    function selectDefender(card, index, unit) {
        document.querySelectorAll('.unit-card[data-role="defender"]').forEach(c => {
            c.classList.remove('selected-defender');
        });

        if (selectedDefenderIndex === index) {
            selectedDefenderIndex = null;
            selectedDefenderUnit = null;
        } else {
            selectedDefenderIndex = index;
            selectedDefenderUnit = unit;
            card.classList.add('selected-defender');
        }
    }

    // ---------------------------------------------------------------------------
    // Weapon row click handling
    // ---------------------------------------------------------------------------

    document.querySelectorAll('.unit-card[data-role="attacker"] .weapon-variant-row').forEach(row => {
        row.addEventListener('click', function (e) {
            e.stopPropagation(); // prevent card selection toggle

            const card = row.closest('.unit-card');
            const unitIndex = parseInt(card.dataset.index, 10);
            const unit = JSON.parse(card.dataset.unit);

            const weaponName  = row.dataset.weaponName;
            const variantName = row.dataset.variant;
            const modelName   = row.dataset.modelName;
            const modelCount  = parseInt(row.dataset.modelCount, 10);

            const isSameRow = selectedWeapon
                && selectedWeapon.weaponName === weaponName
                && selectedWeapon.variantName === variantName
                && selectedWeapon.unitIndex === unitIndex;

            if (isSameRow) {
                // Clicking the same row again deselects it
                clearWeaponSelection();
            } else {
                // Auto-select the parent unit as attacker if not already
                if (!selectedAttackers.has(unitIndex)) {
                    selectedAttackers.set(unitIndex, unit);
                    card.classList.add('selected-attacker');
                }

                // Clear any previously highlighted weapon row
                clearWeaponSelection(/* keepAttackers */ true);

                selectedWeapon = { weaponName, variantName, modelName, modelCount, unitIndex, rowElement: row };
                row.classList.add('selected-weapon');
                attackingModelsInput.value = modelCount;
            }

            updateUI();
        });
    });

    function clearWeaponSelection(keepAttackers = false) {
        if (selectedWeapon && selectedWeapon.rowElement) {
            selectedWeapon.rowElement.classList.remove('selected-weapon');
        }
        selectedWeapon = null;
        updateWeaponDisplay();
        if (!keepAttackers) updateRunButton();
    }

    // ---------------------------------------------------------------------------
    // UI updates
    // ---------------------------------------------------------------------------

    function updateUI() {
        const hasAttackers = selectedAttackers.size > 0;
        const hasDefender = selectedDefenderIndex !== null;

        combatPanel.style.display = (hasAttackers || hasDefender) ? 'block' : 'none';

        const attackerNames = [...selectedAttackers.values()].map(u => u.name).join(' + ');
        const defenderNameStr = selectedDefenderUnit ? selectedDefenderUnit.name : '—';
        selectionSummary.textContent = hasAttackers
            ? `Attacker: ${attackerNames} vs Defender: ${defenderNameStr}`
            : 'Select one or more attacker units and a defender unit.';

        updateWeaponDisplay();
        updateRunButton();
    }

    function updateWeaponDisplay() {
        if (!selectedWeapon) {
            weaponDisplay.className = 'weapon-display-hint';
            weaponDisplay.textContent = 'Expand an attacker unit and click a weapon row to select it.';
            return;
        }
        weaponDisplay.className = 'weapon-display-selected';
        const variantStr = selectedWeapon.variantName === 'default' ? '' : ` [${selectedWeapon.variantName}]`;
        weaponDisplay.textContent =
            `${selectedWeapon.weaponName}${variantStr}  —  ${selectedWeapon.modelName} ×${selectedWeapon.modelCount}`;
    }

    function updateRunButton() {
        const hasAttackers = selectedAttackers.size > 0;
        const hasDefender = selectedDefenderIndex !== null;
        runSimBtn.disabled = !(hasAttackers && hasDefender && selectedWeapon !== null);
    }

    // ---------------------------------------------------------------------------
    // Run simulation
    // ---------------------------------------------------------------------------

    runSimBtn.addEventListener('click', async function () {
        loadingOverlay.classList.add('active');
        resultsPanel.style.display = 'none';

        const request = {
            attackerUnitIndices: [...selectedAttackers.keys()],
            defenderUnitIndex: selectedDefenderIndex,
            weaponName: selectedWeapon.weaponName,
            variantName: selectedWeapon.variantName,
            modelName: selectedWeapon.modelName,
            attackingModels: parseInt(attackingModelsInput.value, 10) || 0,
            withinHalfRange: document.getElementById('within-half-range').checked,
            inCover: document.getElementById('in-cover').checked,
            hitRerolls: document.querySelector('input[name="hit-rerolls"]:checked')?.value ?? 'none',
            woundRerolls: document.querySelector('input[name="wound-rerolls"]:checked')?.value ?? 'none',
            criticalHitsOn5: document.getElementById('crit-5').checked,
            runs: 10000
        };

        try {
            const resp = await fetch('/api/simulate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(request)
            });

            const data = await resp.json();

            if (!data.success) {
                alert(data.error || 'Simulation failed.');
                return;
            }

            displayResults(data);
        } catch (err) {
            alert('Network error: ' + err.message);
        } finally {
            loadingOverlay.classList.remove('active');
        }
    });

    function displayResults(data) {
        document.getElementById('results-description').textContent =
            `${data.attackerName} firing ${data.weaponDescription} at ${data.defenderName} (${data.runs.toLocaleString()} runs)`;

        document.getElementById('res-mean').textContent = data.meanDamage.toFixed(2);
        document.getElementById('res-kills').textContent = data.expectedKills.toFixed(2);
        document.getElementById('res-prob-kill').textContent = (data.probKillAtLeastOne * 100).toFixed(1) + '%';
        document.getElementById('res-std').textContent = '±' + data.stdDeviation.toFixed(2);
        document.getElementById('res-range').textContent =
            `Damage range: ${data.minDamage} – ${data.maxDamage}`;

        resultsPanel.style.display = 'block';
        resultsPanel.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

})();
