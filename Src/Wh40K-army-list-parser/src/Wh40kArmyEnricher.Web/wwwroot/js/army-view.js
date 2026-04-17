// WH40K Army Tool — Army View interactive logic

(function () {
    'use strict';

    // ---------------------------------------------------------------------------
    // State
    // ---------------------------------------------------------------------------

    const selectedAttackers = new Map();  // unitIndex -> unitProfile
    let selectedDefenderIndex = null;
    let selectedDefenderUnit  = null;

    // Map of weaponKey -> { weaponName, variantName, modelName, modelCount, weaponType, unitIndex, rowElement }
    // weaponKey = `${unitIndex}::${modelName}::${weaponName}::${variantName}`
    const selectedWeapons = new Map();
    let activeWeaponType = null; // 'Melee' | 'Ranged' | null

    const combatPanel         = document.getElementById('combat-panel');
    const selectionSummary    = document.getElementById('selection-summary');
    const weaponDisplay       = document.getElementById('weapon-display');
    const attackingModelsInput = document.getElementById('attacking-models');
    const attackingModelsCol  = document.getElementById('attacking-models-col');
    const runSimBtn           = document.getElementById('run-sim-btn');
    const resultsPanel        = document.getElementById('results-panel');
    const loadingOverlay      = document.getElementById('loading-overlay');

    // ---------------------------------------------------------------------------
    // Unit card click handling (header — selection toggle)
    // ---------------------------------------------------------------------------

    document.querySelectorAll('.unit-card').forEach(card => {
        card.addEventListener('click', function (e) {
            if (e.target.closest('.collapse')) return;

            const role  = card.dataset.role;
            const index = parseInt(card.dataset.index, 10);
            const unit  = JSON.parse(card.dataset.unit);

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

            // Remove any weapons that came from this unit.
            for (const [key, w] of selectedWeapons.entries()) {
                if (w.unitIndex === index) {
                    w.rowElement.classList.remove('selected-weapon');
                    selectedWeapons.delete(key);
                }
            }
            if (selectedWeapons.size === 0) activeWeaponType = null;
        } else {
            selectedAttackers.set(index, unit);
            card.classList.add('selected-attacker');
        }
    }

    function selectDefender(card, index, unit) {
        document.querySelectorAll('.unit-card[data-role="defender"]').forEach(c =>
            c.classList.remove('selected-defender'));

        if (selectedDefenderIndex === index) {
            selectedDefenderIndex = null;
            selectedDefenderUnit  = null;
        } else {
            selectedDefenderIndex = index;
            selectedDefenderUnit  = unit;
            card.classList.add('selected-defender');
        }
    }

    // ---------------------------------------------------------------------------
    // Weapon row click handling — toggle-based multi-select
    // ---------------------------------------------------------------------------

    document.querySelectorAll('.unit-card[data-role="attacker"] .weapon-variant-row').forEach(row => {
        row.addEventListener('click', function (e) {
            e.stopPropagation();

            const card       = row.closest('.unit-card');
            const unitIndex  = parseInt(card.dataset.index, 10);
            const unit       = JSON.parse(card.dataset.unit);
            const weaponName  = row.dataset.weaponName;
            const variantName = row.dataset.variant;
            const modelName   = row.dataset.modelName;
            const modelCount  = parseInt(row.dataset.modelCount, 10);
            const weaponType  = row.dataset.weaponType;

            const weaponKey = `${unitIndex}::${modelName}::${weaponName}::${variantName}`;

            if (selectedWeapons.has(weaponKey)) {
                // Deselect this weapon.
                row.classList.remove('selected-weapon');
                selectedWeapons.delete(weaponKey);
                if (selectedWeapons.size === 0) activeWeaponType = null;
            } else {
                // Enforce phase constraint.
                if (activeWeaponType && weaponType !== activeWeaponType) return;

                // Auto-select the parent unit as attacker.
                if (!selectedAttackers.has(unitIndex)) {
                    selectedAttackers.set(unitIndex, unit);
                    card.classList.add('selected-attacker');
                }

                activeWeaponType = weaponType;
                selectedWeapons.set(weaponKey, {
                    weaponName, variantName, modelName, modelCount,
                    weaponType, unitIndex, rowElement: row
                });
                row.classList.add('selected-weapon');

                // Seed the models-firing input on first selection.
                if (selectedWeapons.size === 1)
                    attackingModelsInput.value = modelCount;
            }

            updateUI();
        });
    });

    // ---------------------------------------------------------------------------
    // Fish for Crits — show/hide conditional checkboxes
    // ---------------------------------------------------------------------------

    document.querySelectorAll('input[name="hit-rerolls"]').forEach(r => {
        r.addEventListener('change', function () {
            const fishCol = document.getElementById('fish-hit-col');
            const fishCheck = document.getElementById('fish-crit-hits');
            if (this.value === 'all') {
                fishCol.style.display = '';
            } else {
                fishCol.style.display = 'none';
                fishCheck.checked = false;
            }
            updateModifierSummaries();
        });
    });

    document.querySelectorAll('input[name="wound-rerolls"]').forEach(r => {
        r.addEventListener('change', function () {
            const fishCol = document.getElementById('fish-wound-col');
            const fishCheck = document.getElementById('fish-crit-wounds');
            if (this.value === 'all') {
                fishCol.style.display = '';
            } else {
                fishCol.style.display = 'none';
                fishCheck.checked = false;
            }
            updateModifierSummaries();
        });
    });

    // Update summaries whenever any modifier changes.
    document.getElementById('modifierAccordion').addEventListener('change', updateModifierSummaries);

    // ---------------------------------------------------------------------------
    // Modifier summaries (brief text shown on collapsed accordion headers)
    // ---------------------------------------------------------------------------

    function updateModifierSummaries() {
        document.getElementById('summary-attack').textContent  = buildAttackSummary();
        document.getElementById('summary-hit').textContent     = buildHitSummary();
        document.getElementById('summary-wound').textContent   = buildWoundSummary();
        document.getElementById('summary-save').textContent    = buildSaveSummary();
        document.getElementById('summary-damage').textContent  = buildDamageSummary();
    }

    function radioVal(name)    { return document.querySelector(`input[name="${name}"]:checked`)?.value ?? '0'; }
    function checkVal(id)      { return document.getElementById(id)?.checked ?? false; }
    function selectVal(id)     { return document.getElementById(id)?.value ?? ''; }

    function buildAttackSummary() {
        const parts = [];
        const mod = parseInt(radioVal('attack-mod'), 10);
        if (mod !== 0) parts.push(mod > 0 ? `+${mod} Atk` : `${mod} Atk`);
        if (checkVal('blast-override')) parts.push('Blast');
        if (checkVal('reroll-attacks')) parts.push('RR Atk');
        return parts.join(' · ');
    }

    function buildHitSummary() {
        const parts = [];
        if (checkVal('within-half-range')) parts.push('½ Range');
        const hitMod = parseInt(radioVal('hit-roll-mod'), 10);
        const bswsMod = parseInt(radioVal('bsws-mod'), 10);
        if (hitMod !== 0) parts.push(hitMod > 0 ? `+${hitMod} Hit` : `${hitMod} Hit`);
        if (bswsMod !== 0) parts.push(bswsMod > 0 ? `+${bswsMod} BS` : `${bswsMod} BS`);
        const rr = radioVal('hit-rerolls');
        if (rr === 'ones') parts.push('RR1s');
        if (rr === 'all') parts.push(checkVal('fish-crit-hits') ? 'Fish Crits' : 'RR All');
        if (checkVal('indirect-fire')) parts.push('Indirect');
        if (checkVal('crit-5')) parts.push('Crit 5+');
        if (checkVal('sustained-hits-override')) parts.push('SH1');
        if (checkVal('lethal-hits-override')) parts.push('LH');
        return parts.join(' · ');
    }

    function buildWoundSummary() {
        const parts = [];
        const wMod = parseInt(radioVal('wound-roll-mod'), 10);
        const sMod = parseInt(radioVal('str-mod'), 10);
        const tMod = parseInt(radioVal('tough-mod'), 10);
        if (wMod !== 0) parts.push(wMod > 0 ? `+${wMod} Wnd` : `${wMod} Wnd`);
        if (sMod !== 0) parts.push(sMod > 0 ? `+${sMod} S` : `${sMod} S`);
        if (tMod !== 0) parts.push(tMod > 0 ? `+${tMod} T` : `${tMod} T`);
        const rr = radioVal('wound-rerolls');
        if (rr === 'ones') parts.push('RR1s');
        if (rr === 'all') parts.push(checkVal('fish-crit-wounds') ? 'Fish Crits' : 'RR All');
        if (checkVal('crit-wound-5')) parts.push('CritWnd 5+');
        if (checkVal('dev-wounds-override')) parts.push('DW');
        const anti = selectVal('anti-keyword');
        if (anti) parts.push(`Anti-${anti} ${selectVal('anti-threshold')}+`);
        return parts.join(' · ');
    }

    function buildSaveSummary() {
        const parts = [];
        const apMod = parseInt(radioVal('ap-mod'), 10);
        if (apMod !== 0) parts.push(apMod > 0 ? `+${apMod} AP` : `${apMod} AP`);
        if (checkVal('in-cover')) parts.push(checkVal('ignores-cover') ? 'Cover (ignored)' : 'Cover');
        return parts.join(' · ');
    }

    function buildDamageSummary() {
        const parts = [];
        const dMod = parseInt(radioVal('dmg-mod'), 10);
        if (dMod !== 0) parts.push(dMod > 0 ? `+${dMod} Dmg` : `${dMod} Dmg`);
        if (checkVal('reroll-damage')) parts.push('RR Dmg');
        const fnp = parseInt(selectVal('fnp-override'), 10);
        if (fnp > 0) parts.push(`FNP ${fnp}+++`);
        return parts.join(' · ');
    }

    // ---------------------------------------------------------------------------
    // UI updates
    // ---------------------------------------------------------------------------

    function updateUI() {
        const hasAttackers = selectedAttackers.size > 0;
        const hasDefender  = selectedDefenderIndex !== null;

        combatPanel.style.display = (hasAttackers || hasDefender) ? 'block' : 'none';

        const attackerNames  = [...selectedAttackers.values()].map(u => u.name).join(' + ');
        const defenderNameStr = selectedDefenderUnit ? selectedDefenderUnit.name : '—';
        selectionSummary.textContent = hasAttackers
            ? `Attacker: ${attackerNames} vs Defender: ${defenderNameStr}`
            : 'Select one or more attacker units and a defender unit.';

        updateWeaponDisplay();
        updateWeaponTypeConstraints();
        updateRunButton();
    }

    function updateWeaponDisplay() {
        const count = selectedWeapons.size;

        if (count === 0) {
            weaponDisplay.className = 'weapon-display-hint';
            weaponDisplay.textContent = 'Expand an attacker unit and click a weapon row to select it.';
            attackingModelsCol.style.display = '';
            return;
        }

        weaponDisplay.className = 'weapon-display-selected';

        if (count === 1) {
            const w = [...selectedWeapons.values()][0];
            const variantStr = w.variantName === 'default' ? '' : ` [${w.variantName}]`;
            weaponDisplay.textContent = `${w.weaponName}${variantStr}  —  ${w.modelName} ×${w.modelCount}`;
            attackingModelsCol.style.display = '';
        } else {
            const lines = [...selectedWeapons.values()].map(w => {
                const variantStr = w.variantName === 'default' ? '' : ` [${w.variantName}]`;
                return `${w.weaponName}${variantStr} — ${w.modelName} ×${w.modelCount}`;
            });
            weaponDisplay.innerHTML = lines.map(l => escHtml(l)).join('<br>');
            attackingModelsCol.style.display = 'none';
        }
    }

    function updateWeaponTypeConstraints() {
        document.querySelectorAll('.unit-card[data-role="attacker"] .weapon-variant-row').forEach(row => {
            if (activeWeaponType && row.dataset.weaponType !== activeWeaponType)
                row.classList.add('weapon-type-locked');
            else
                row.classList.remove('weapon-type-locked');
        });
    }

    function updateRunButton() {
        const hasAttackers = selectedAttackers.size > 0;
        const hasDefender  = selectedDefenderIndex !== null;
        runSimBtn.disabled = !(hasAttackers && hasDefender && selectedWeapons.size > 0);
    }

    // ---------------------------------------------------------------------------
    // Run simulation
    // ---------------------------------------------------------------------------

    runSimBtn.addEventListener('click', async function () {
        loadingOverlay.classList.add('active');
        resultsPanel.style.display = 'none';
        document.getElementById('pipeline-section').style.display = 'none';

        const isSingleWeapon = selectedWeapons.size === 1;
        const modelsOverride = isSingleWeapon ? (parseInt(attackingModelsInput.value) || 0) : 0;

        const weaponSelections = [...selectedWeapons.values()].map(w => ({
            weaponName:  w.weaponName,
            variantName: w.variantName,
            modelName:   w.modelName,
            modelCount:  (isSingleWeapon && modelsOverride > 0) ? modelsOverride : w.modelCount,
        }));

        const antiKeyword = selectVal('anti-keyword');

        const request = {
            attackerUnitIndices: [...selectedAttackers.keys()],
            defenderUnitIndex:   selectedDefenderIndex,
            weaponSelections,
            withinHalfRange:  checkVal('within-half-range'),
            runs: 10000,

            // Attack modifiers
            attackModifier:    parseInt(radioVal('attack-mod'), 10),
            blastOverride:     checkVal('blast-override'),
            rerollAttackDice:  checkVal('reroll-attacks'),

            // Hit modifiers
            hitRollModifier:      parseInt(radioVal('hit-roll-mod'), 10),
            bsWsModifier:         parseInt(radioVal('bsws-mod'), 10),
            hitRerolls:           radioVal('hit-rerolls'),
            fishForCriticalHits:  checkVal('fish-crit-hits'),
            indirectFireOverride: checkVal('indirect-fire'),
            criticalHitsOn5:      checkVal('crit-5'),
            sustainedHitsOverride: checkVal('sustained-hits-override'),
            lethalHitsOverride:   checkVal('lethal-hits-override'),

            // Wound modifiers
            woundRollModifier:    parseInt(radioVal('wound-roll-mod'), 10),
            strengthModifier:     parseInt(radioVal('str-mod'), 10),
            toughnessModifier:    parseInt(radioVal('tough-mod'), 10),
            woundRerolls:         radioVal('wound-rerolls'),
            fishForCriticalWounds: checkVal('fish-crit-wounds'),
            critWoundOn5:         checkVal('crit-wound-5'),
            devastatingWoundsOverride: checkVal('dev-wounds-override'),
            antiOverrideKeyword:  antiKeyword,
            antiOverrideThreshold: antiKeyword ? parseInt(selectVal('anti-threshold'), 10) : 4,

            // Save modifiers
            inCover:       checkVal('in-cover'),
            ignoresCover:  checkVal('ignores-cover'),
            apModifier:    parseInt(radioVal('ap-mod'), 10),
            fnpOverride:   parseInt(selectVal('fnp-override'), 10),

            // Damage modifiers
            damageModifier:   parseInt(radioVal('dmg-mod'), 10),
            rerollDamageDice: checkVal('reroll-damage'),
        };

        try {
            const resp = await fetch('/api/simulate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(request),
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

    // ---------------------------------------------------------------------------
    // Results display
    // ---------------------------------------------------------------------------

    function displayResults(data) {
        document.getElementById('results-description').textContent =
            `${data.attackerName} firing ${data.weaponDescription} at ${data.defenderName} (${data.runs.toLocaleString()} runs)`;

        document.getElementById('res-mean').textContent    = data.meanDamage.toFixed(2);
        document.getElementById('res-kills').textContent   = data.expectedKills.toFixed(2);
        document.getElementById('res-prob-kill').textContent = (data.probKillAtLeastOne * 100).toFixed(1) + '%';
        document.getElementById('res-std').textContent     = '±' + data.stdDeviation.toFixed(2);
        document.getElementById('res-range').textContent   = `Damage range: ${data.minDamage} – ${data.maxDamage}`;

        resultsPanel.style.display = 'block';

        if (data.stageStats) {
            displayPipeline(data.stageStats, data.weaponBreakdown || [], data.meanDamage);
        }

        resultsPanel.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    // ---------------------------------------------------------------------------
    // Pipeline display — dynamically generated
    // ---------------------------------------------------------------------------

    function displayPipeline(aggregate, weaponBreakdown, finalDamage) {
        const content = document.getElementById('pipeline-content');
        let html = '';

        if (weaponBreakdown.length > 1) {
            // Per-weapon sections followed by a combined summary.
            for (const group of weaponBreakdown) {
                html += `<div class="pipeline-weapon-header">${escHtml(group.weaponName)}</div>`;
                html += buildFunnelTable(group.stats, null);
            }
            html += '<div class="pipeline-weapon-header pipeline-combined">Combined</div>';
            html += buildSummaryTable(aggregate, finalDamage);
        } else {
            // Single weapon: full funnel with Final Damage at the bottom.
            html += buildFunnelTable(aggregate, finalDamage);
        }

        content.innerHTML = html;
        document.getElementById('pipeline-section').style.display = 'block';
    }

    /** Full funnel table: all stages and ability sub-rows. finalDamage appended when non-null. */
    function buildFunnelTable(s, finalDamage) {
        const rows = [];

        rows.push(pRow('main', 'Attacks', s.avgAttacks, ''));
        rows.push(pRow('main', 'Hits', s.avgHits, pct(s.avgHits, s.avgAttacks)));
        rows.push(pRow('sub',  '↳ Critical Hits', s.avgCritHits, ''));
        if (s.avgSustainedHitsBonus > 0.001)
            rows.push(pRow('sub', '↳ Sustained Hits bonus', s.avgSustainedHitsBonus, ''));

        rows.push(pRow('main', 'Wounds', s.avgWounds, pct(s.avgWounds, s.avgHits)));
        rows.push(pRow('sub',  '↳ Critical Wounds', s.avgCritWounds, ''));
        if (s.avgLethalHitsAutoWounds > 0.001)
            rows.push(pRow('sub', '↳ Lethal Hits auto-wounds', s.avgLethalHitsAutoWounds, ''));
        if (s.avgAntiCritWounds > 0.001)
            rows.push(pRow('sub', '↳ Anti-X crit wounds', s.avgAntiCritWounds, ''));

        const totalFailed = s.avgFailedSaves + s.avgDevastatingWoundsTriggers;
        rows.push(pRow('main', 'Failed Saves', totalFailed, pct(totalFailed, s.avgWounds)));
        if (s.avgDevastatingWoundsTriggers > 0.001)
            rows.push(pRow('sub', '↳ Devastating Wounds (bypassed)', s.avgDevastatingWoundsTriggers, ''));

        const totalSaveRolls = s.avgArmourSaveRolls + s.avgInvulnSaveRolls;
        if (totalSaveRolls > 0.001) {
            rows.push(pRow('sub', '↳ vs Armour save', s.avgArmourSaveRolls, ''));
            if (s.avgInvulnSaveRolls > 0.001)
                rows.push(pRow('sub', '↳ vs Invulnerable save', s.avgInvulnSaveRolls, ''));
        }

        rows.push(pRow('main', 'Damage (pre-FNP)', s.avgDamageBeforeFnp, ''));
        if (s.avgFnpSaved > 0.001)
            rows.push(pRow('sub', '↳ Feel No Pain saved', s.avgFnpSaved, pct(s.avgFnpSaved, s.avgDamageBeforeFnp)));

        if (finalDamage !== null)
            rows.push(pRow('final', 'Final Damage', finalDamage, ''));

        return `<table class="pipeline-table"><tbody>${rows.join('')}</tbody></table>`;
    }

    /** Compact summary table for the combined row in multi-weapon mode. */
    function buildSummaryTable(s, finalDamage) {
        const totalFailed = s.avgFailedSaves + s.avgDevastatingWoundsTriggers;
        const rows = [
            pRow('main',  'Attacks',           s.avgAttacks,        ''),
            pRow('main',  'Hits',               s.avgHits,           ''),
            pRow('main',  'Wounds',             s.avgWounds,         ''),
            pRow('main',  'Failed Saves',       totalFailed,         ''),
            pRow('main',  'Damage (pre-FNP)',   s.avgDamageBeforeFnp,''),
            pRow('final', 'Final Damage',       finalDamage,         ''),
        ];
        return `<table class="pipeline-table"><tbody>${rows.join('')}</tbody></table>`;
    }

    function pRow(cls, label, value, rate) {
        return `<tr class="pipeline-row pipeline-${escHtml(cls)}">` +
            `<td class="pipeline-stage">${escHtml(label)}</td>` +
            `<td class="pipeline-value">${fmt(value)}</td>` +
            `<td class="pipeline-rate">${escHtml(rate)}</td>` +
            `</tr>`;
    }

    // ---------------------------------------------------------------------------
    // Catalogue refresh
    // ---------------------------------------------------------------------------

    const refreshBtn    = document.getElementById('refresh-catalogues-btn');
    const refreshStatus = document.getElementById('refresh-status');

    if (refreshBtn) {
        refreshBtn.addEventListener('click', async () => {
            refreshBtn.disabled = true;
            refreshStatus.style.color = '#adb5bd';
            refreshStatus.textContent = 'Refreshing…';

            try {
                const res  = await fetch('/api/refresh-catalogues', { method: 'POST' });
                const data = await res.json();

                if (data.success) {
                    refreshStatus.style.color = '#57cc99';
                    refreshStatus.textContent = `Updated: ${data.refreshed.join(', ')}`;
                } else {
                    refreshStatus.style.color = '#e63946';
                    refreshStatus.textContent = data.error ?? 'Refresh failed.';
                }
            } catch (e) {
                refreshStatus.style.color = '#e63946';
                refreshStatus.textContent = 'Network error.';
            } finally {
                refreshBtn.disabled = false;
            }
        });
    }

    // ---------------------------------------------------------------------------
    // Utilities
    // ---------------------------------------------------------------------------

    function fmt(n)         { return (typeof n === 'number' ? n : 0).toFixed(2); }
    function pct(num, denom){ return (!denom || denom < 0.001) ? '' : `(${(num / denom * 100).toFixed(1)}%)`; }
    function escHtml(s)     { return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;'); }

})();
