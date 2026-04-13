// WH40K Army Tool — Army View interactive logic

(function () {
    'use strict';

    // State
    const selectedAttackers = new Map(); // index -> unitProfile
    let selectedDefenderIndex = null;
    let selectedDefenderUnit = null;

    const combatPanel = document.getElementById('combat-panel');
    const selectionSummary = document.getElementById('selection-summary');
    const weaponSelect = document.getElementById('weapon-select');
    const variantSelect = document.getElementById('variant-select');
    const attackingModelsInput = document.getElementById('attacking-models');
    const runSimBtn = document.getElementById('run-sim-btn');
    const resultsPanel = document.getElementById('results-panel');
    const loadingOverlay = document.getElementById('loading-overlay');

    // ---------------------------------------------------------------------------
    // Unit card click handling
    // ---------------------------------------------------------------------------

    document.querySelectorAll('.unit-card').forEach(card => {
        // Expand/collapse is handled by Bootstrap on the inner header.
        // We handle selection on the card element itself, but must not fire when
        // the user is clicking the header to expand (Bootstrap handles that toggle).
        card.addEventListener('click', function (e) {
            // Ignore clicks that originate inside the collapsed detail body
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
        } else {
            selectedAttackers.set(index, unit);
            card.classList.add('selected-attacker');
        }
    }

    function selectDefender(card, index, unit) {
        // Deselect previous defender
        document.querySelectorAll('.unit-card[data-role="defender"]').forEach(c => {
            c.classList.remove('selected-defender');
        });

        if (selectedDefenderIndex === index) {
            // Clicking again deselects
            selectedDefenderIndex = null;
            selectedDefenderUnit = null;
        } else {
            selectedDefenderIndex = index;
            selectedDefenderUnit = unit;
            card.classList.add('selected-defender');
        }
    }

    // ---------------------------------------------------------------------------
    // UI updates
    // ---------------------------------------------------------------------------

    function updateUI() {
        const hasAttackers = selectedAttackers.size > 0;
        const hasDefender = selectedDefenderIndex !== null;

        if (hasAttackers || hasDefender) {
            combatPanel.style.display = 'block';
        } else {
            combatPanel.style.display = 'none';
        }

        // Selection summary
        const attackerNames = [...selectedAttackers.values()].map(u => u.name).join(' + ');
        const defenderNameStr = selectedDefenderUnit ? selectedDefenderUnit.name : '—';
        selectionSummary.textContent = hasAttackers
            ? `Attacker: ${attackerNames} vs Defender: ${defenderNameStr}`
            : 'Select one or more attacker units and a defender unit.';

        // Populate weapon dropdown
        populateWeapons();

        // Enable run button only when everything is selected
        const weaponChosen = weaponSelect.value !== '';
        runSimBtn.disabled = !(hasAttackers && hasDefender && weaponChosen);
    }

    function populateWeapons() {
        const previousWeapon = weaponSelect.value;
        weaponSelect.innerHTML = '<option value="">— choose weapon —</option>';

        if (selectedAttackers.size === 0) return;

        // Collect all weapons from all selected attacker units
        // weapon key: "modelName|weaponName"
        const weaponMap = new Map(); // weaponName -> { modelName, weapon }

        for (const unit of selectedAttackers.values()) {
            for (const model of unit.models) {
                for (const weapon of model.weapons) {
                    const key = weapon.weaponName;
                    if (!weaponMap.has(key)) {
                        weaponMap.set(key, { modelName: model.modelName, count: model.count, weapon });
                    } else {
                        // Same weapon on multiple models — accumulate count
                        weaponMap.get(key).count += model.count;
                    }
                }
            }
        }

        for (const [weaponName, info] of weaponMap) {
            const opt = document.createElement('option');
            opt.value = weaponName;
            opt.dataset.modelName = info.modelName;
            opt.dataset.count = info.count;
            opt.dataset.variants = JSON.stringify(info.weapon.profiles.map(p => p.variant));
            opt.textContent = `${weaponName} (${info.modelName} ×${info.count})`;
            weaponSelect.appendChild(opt);
        }

        // Restore previous selection if still available
        if (previousWeapon && [...weaponMap.keys()].includes(previousWeapon)) {
            weaponSelect.value = previousWeapon;
        }

        populateVariants();
    }

    weaponSelect.addEventListener('change', function () {
        populateVariants();
        const selectedOpt = weaponSelect.options[weaponSelect.selectedIndex];
        if (selectedOpt && selectedOpt.dataset.count) {
            attackingModelsInput.value = selectedOpt.dataset.count;
        }
        updateRunButton();
    });

    function populateVariants() {
        variantSelect.innerHTML = '';
        const selectedOpt = weaponSelect.options[weaponSelect.selectedIndex];
        if (!selectedOpt || !selectedOpt.dataset.variants) {
            const opt = document.createElement('option');
            opt.value = 'default';
            opt.textContent = 'default';
            variantSelect.appendChild(opt);
            return;
        }

        const variants = JSON.parse(selectedOpt.dataset.variants);
        for (const v of variants) {
            const opt = document.createElement('option');
            opt.value = v;
            opt.textContent = v;
            variantSelect.appendChild(opt);
        }
    }

    function updateRunButton() {
        const hasAttackers = selectedAttackers.size > 0;
        const hasDefender = selectedDefenderIndex !== null;
        const weaponChosen = weaponSelect.value !== '';
        runSimBtn.disabled = !(hasAttackers && hasDefender && weaponChosen);
    }

    weaponSelect.addEventListener('change', updateRunButton);
    variantSelect.addEventListener('change', updateRunButton);

    // ---------------------------------------------------------------------------
    // Run simulation
    // ---------------------------------------------------------------------------

    runSimBtn.addEventListener('click', async function () {
        loadingOverlay.classList.add('active');
        resultsPanel.style.display = 'none';

        const selectedOpt = weaponSelect.options[weaponSelect.selectedIndex];

        const request = {
            attackerUnitIndices: [...selectedAttackers.keys()],
            defenderUnitIndex: selectedDefenderIndex,
            weaponName: weaponSelect.value,
            variantName: variantSelect.value || 'default',
            modelName: selectedOpt ? selectedOpt.dataset.modelName : '',
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
