// army-view.js — weapon selection, combat panel, simulation, pipeline display

// ── Persistent state ──────────────────────────────────────────────────────────

const state = {
    attackerUnitName: null,   // primary attacker unit name (first selection)
    selections: [],           // {cardId, unitName, weaponName, variantName, modelName, weaponType, modelCount}
    lockedPhase: null,        // 'Ranged' | 'Melee' | null
    defenderCardId: null,
    defenderUnitName: null,
};

// Modifier values — persists across weapon selection changes
const mods = {
    attackModifier: 0, blastOverride: false, rerollAttackDice: false,
    withinHalfRange: false, hitRollModifier: 0, bsWsModifier: 0,
    rerollHitOnes: false, rerollHitAll: false, fishForCritHits: false,
    indirectFire: false, critHitOn5Plus: false, sustainedHitsOverride: false, lethalHitsOverride: false,
    woundRollModifier: 0, strengthModifier: 0, toughnessModifier: 0,
    rerollWoundOnes: false, rerollWoundAll: false, fishForCritWounds: false,
    critWoundOn5Plus: false, devastatingWoundsOverride: false, antiKeyword: '', antiThreshold: 0,
    cover: false, ignoresCover: false, apModifier: 0,
    damageModifier: 0, rerollDamageDice: false, fnpOverride: 0,
    defenderModelCount: 0,
};

// Open/closed state of modifier <details> sections — survives panel rebuilds
const sectionState = { attack: false, hit: false, wound: false, save: false, damage: false };

// ── Card toggle ───────────────────────────────────────────────────────────────

function toggleCard(cardId) {
    const card = document.getElementById(cardId);
    if (!card) return;
    const body = card.querySelector('.unit-card-body');
    const isOpen = card.classList.contains('open');
    if (isOpen) {
        body.style.display = 'none';
        card.classList.remove('open');
    } else {
        body.style.display = 'block';
        card.classList.add('open');
    }
}

// ── Defender selection ────────────────────────────────────────────────────────

function onDefenderHeaderClick(cardId) {
    toggleCard(cardId);
    selectDefender(cardId);
}

function selectDefender(cardId) {
    if (state.defenderCardId === cardId) {
        clearDefender();
    } else {
        clearDefender();
        const card = document.getElementById(cardId);
        if (!card) return;
        state.defenderCardId = cardId;
        state.defenderUnitName = card.dataset.unitName || '';
        card.classList.add('selected-defender');
    }
    updateCombatPanel();
}

function clearDefender() {
    if (state.defenderCardId) {
        const prev = document.getElementById(state.defenderCardId);
        if (prev) prev.classList.remove('selected-defender');
    }
    state.defenderCardId = null;
    state.defenderUnitName = null;
}

// ── Weapon row selection ──────────────────────────────────────────────────────

function selectWeaponRow(row) {
    const card = row.closest('.unit-card');
    if (!card || card.dataset.side !== 'attacker') return;
    if (row.classList.contains('weapon-type-locked')) return;

    const weaponName = row.dataset.weapon || '';
    const variantName = row.dataset.variant || '';
    const modelName = row.dataset.model || '';
    const weaponType = row.dataset.weaponType || '';
    const unitName = card.dataset.unitName || '';

    const idx = state.selections.findIndex(s =>
        s.cardId === card.id &&
        s.weaponName === weaponName &&
        s.variantName === variantName &&
        s.modelName === modelName);

    if (idx >= 0) {
        state.selections.splice(idx, 1);
        row.classList.remove('selected-attacker');
    } else {
        const unitData = getUnitData(card);
        let defaultCount = 1;
        if (unitData) {
            const modelEntry = (unitData.models || []).find(m => m.modelName === modelName);
            if (modelEntry) defaultCount = modelEntry.count || 1;
        }
        state.selections.push({ cardId: card.id, unitName, weaponName, variantName, modelName, weaponType, modelCount: defaultCount });
        row.classList.add('selected-attacker');

        if (!state.lockedPhase) {
            state.lockedPhase = weaponType;
            state.attackerUnitName = unitName;
        }
    }

    if (state.selections.length === 0) {
        state.lockedPhase = null;
        state.attackerUnitName = null;
    }

    applyPhaseLock();
    updateCombatPanel();
}

function applyPhaseLock() {
    document.querySelectorAll('.unit-card[data-side="attacker"] .weapon-row').forEach(row => {
        const rowType = row.dataset.weaponType || '';
        if (state.lockedPhase && rowType && rowType !== state.lockedPhase) {
            row.classList.add('weapon-type-locked');
        } else {
            row.classList.remove('weapon-type-locked');
        }
    });
}

function getUnitData(card) {
    try { return JSON.parse(card.dataset.unit || '{}'); } catch { return null; }
}

// ── Combat panel ──────────────────────────────────────────────────────────────

function updateCombatPanel() {
    const panel = document.getElementById('combat-panel');
    if (!panel) return;

    if (state.selections.length === 0 || !state.defenderCardId) {
        panel.style.display = 'none';
        return;
    }

    panel.style.display = 'block';
    panel.innerHTML = buildPanelHtml();
    attachPanelListeners();
}

function buildPanelHtml() {
    const attackerNames = [...new Set(state.selections.map(s => s.unitName))];
    const attackerDisplay = attackerNames.join(' + ');

    let html = `<div class="panel-header">
        <span class="panel-title">${escHtml(attackerDisplay)} <span class="panel-arrow">→</span> ${escHtml(state.defenderUnitName || '')}</span>
    </div>`;

    // Weapon selections with per-selection model count inputs
    html += `<div class="panel-selections">`;
    state.selections.forEach((sel, i) => {
        const label = sel.variantName ? `${sel.weaponName} (${sel.variantName})` : sel.weaponName;
        html += `<div class="panel-selection">
            <span class="panel-sel-name">${escHtml(label)}</span>
            <span class="panel-sel-model">${escHtml(sel.modelName)}</span>
            <label class="panel-count-label">×<input type="number" class="panel-count-input" min="1" data-sel-idx="${i}" value="${sel.modelCount}"></label>
        </div>`;
    });
    html += `</div>`;

    html += buildSection('attack', 'Attack', buildAttackControls(), attackSummary());
    html += buildSection('hit', 'Hit', buildHitControls(), hitSummary());
    html += buildSection('wound', 'Wound', buildWoundControls(), woundSummary());
    html += buildSection('save', 'Save', buildSaveControls(), saveSummary());
    html += buildSection('damage', 'Damage', buildDamageControls(), damageSummary());

    html += `<div class="panel-row">
        <label class="panel-label">Surviving defenders (0 = use profile count):
            <input type="number" class="mod-num" id="mod-defenderModelCount" min="0" value="${mods.defenderModelCount}">
        </label>
    </div>`;

    html += `<button class="btn-run" onclick="runSimulation()">Run Simulation</button>`;
    html += `<div id="pipeline-content"></div>`;

    return html;
}

function buildSection(id, title, controls, summary) {
    const isOpen = sectionState[id] ? ' open' : '';
    return `<details class="mod-section" id="section-${id}"${isOpen} ontoggle="onSectionToggle('${id}', this.open)">
        <summary class="mod-section-header">
            <span class="mod-section-title">${title}</span>
            <span class="mod-section-summary" id="summary-${id}">${escHtml(summary)}</span>
        </summary>
        <div class="mod-section-body">${controls}</div>
    </details>`;
}

function onSectionToggle(id, open) {
    sectionState[id] = open;
}

// ── Modifier controls ─────────────────────────────────────────────────────────

function tog(field, hidden) {
    const style = hidden ? ' style="display:none"' : '';
    return `<button class="mod-toggle${mods[field] ? ' active' : ''}" id="tog-${field}"${style} onclick="toggleMod('${field}')">${modLabel(field)}</button>`;
}

function stepCtrl(field, label) {
    const val = mods[field] || 0;
    const display = val > 0 ? `+${val}` : `${val}`;
    return `<span class="mod-step">
        <button class="mod-step-btn" onclick="stepMod('${field}',-1)">−</button>
        <span class="mod-step-val" id="val-${field}">${display}</span>
        <button class="mod-step-btn" onclick="stepMod('${field}',1)">+</button>
        <span class="mod-step-label">${label}</span>
    </span>`;
}

function buildAttackControls() {
    return `<div class="mod-controls">
        ${stepCtrl('attackModifier', 'Attacks')}
        ${tog('blastOverride')}
        ${tog('rerollAttackDice')}
    </div>`;
}

function buildHitControls() {
    return `<div class="mod-controls">
        ${tog('withinHalfRange')}
        ${stepCtrl('hitRollModifier', '+/− Hit')}
        ${stepCtrl('bsWsModifier', '+/− BS/WS')}
    </div>
    <div class="mod-controls">
        ${tog('rerollHitOnes')}
        ${tog('rerollHitAll')}
        ${tog('fishForCritHits', !mods.rerollHitAll)}
        ${tog('indirectFire')}
        ${tog('critHitOn5Plus')}
        ${tog('sustainedHitsOverride')}
        ${tog('lethalHitsOverride')}
    </div>`;
}

function buildWoundControls() {
    return `<div class="mod-controls">
        ${stepCtrl('woundRollModifier', '+/− Wound')}
        ${stepCtrl('strengthModifier', '+/− Str')}
        ${stepCtrl('toughnessModifier', '+/− Tgh')}
    </div>
    <div class="mod-controls">
        ${tog('rerollWoundOnes')}
        ${tog('rerollWoundAll')}
        ${tog('fishForCritWounds', !mods.rerollWoundAll)}
        ${tog('critWoundOn5Plus')}
        ${tog('devastatingWoundsOverride')}
    </div>
    <div class="mod-controls anti-row">
        <span class="mod-step-label">Anti:</span>
        <input type="text" class="mod-text" id="mod-antiKeyword" value="${escHtml(mods.antiKeyword)}" placeholder="Keyword" oninput="setModText('antiKeyword',this.value,'wound')">
        <input type="number" class="mod-num" id="mod-antiThreshold" min="2" max="6" value="${mods.antiThreshold || ''}" placeholder="thr" oninput="setModNum('antiThreshold',parseInt(this.value)||0,'wound')">
    </div>`;
}

function buildSaveControls() {
    return `<div class="mod-controls">
        ${tog('cover')}
        ${tog('ignoresCover')}
        ${stepCtrl('apModifier', '+/− AP')}
    </div>`;
}

function buildDamageControls() {
    const fnpOpts = [
        [0, 'None'], [4, '4+++'], [5, '5+++'], [6, '6+++'],
    ].map(([v, l]) => `<option value="${v}"${mods.fnpOverride === v ? ' selected' : ''}>${l}</option>`).join('');
    return `<div class="mod-controls">
        ${stepCtrl('damageModifier', '+/− Dmg')}
        ${tog('rerollDamageDice')}
        <span class="mod-step">
            <span class="mod-step-label">FNP:</span>
            <select class="mod-select" id="mod-fnpOverride" onchange="setModNum('fnpOverride',parseInt(this.value),'damage')">${fnpOpts}</select>
        </span>
    </div>`;
}

const modLabels = {
    blastOverride: 'Blast', rerollAttackDice: 'RR Attacks',
    withinHalfRange: '½ Range',
    rerollHitOnes: 'RR 1s', rerollHitAll: 'RR All', fishForCritHits: 'Fish Crits',
    indirectFire: 'Indirect', critHitOn5Plus: 'Crit 5+',
    sustainedHitsOverride: 'SH+1', lethalHitsOverride: 'Lethal Hits',
    rerollWoundOnes: 'RR 1s', rerollWoundAll: 'RR All', fishForCritWounds: 'Fish Crits',
    critWoundOn5Plus: 'Crit W 5+', devastatingWoundsOverride: 'Dev Wounds',
    cover: 'Cover', ignoresCover: 'Ign Cover',
    rerollDamageDice: 'RR Damage',
};
function modLabel(field) { return modLabels[field] || field; }

// ── Summary strings ───────────────────────────────────────────────────────────

function attackSummary() {
    const p = [];
    if (mods.attackModifier) p.push(`${mods.attackModifier > 0 ? '+' : ''}${mods.attackModifier} Atk`);
    if (mods.blastOverride) p.push('Blast');
    if (mods.rerollAttackDice) p.push('RR');
    return p.join(' · ');
}
function hitSummary() {
    const p = [];
    if (mods.withinHalfRange) p.push('½ Range');
    if (mods.hitRollModifier) p.push(`${mods.hitRollModifier > 0 ? '+' : ''}${mods.hitRollModifier} Hit`);
    if (mods.bsWsModifier) p.push(`${mods.bsWsModifier > 0 ? '+' : ''}${mods.bsWsModifier} BS/WS`);
    if (mods.rerollHitAll) p.push('RR All'); else if (mods.rerollHitOnes) p.push('RR 1s');
    if (mods.fishForCritHits) p.push('Fish');
    if (mods.indirectFire) p.push('Indirect');
    if (mods.critHitOn5Plus) p.push('Crit 5+');
    if (mods.sustainedHitsOverride) p.push('SH+1');
    if (mods.lethalHitsOverride) p.push('LH');
    return p.join(' · ');
}
function woundSummary() {
    const p = [];
    if (mods.woundRollModifier) p.push(`${mods.woundRollModifier > 0 ? '+' : ''}${mods.woundRollModifier} W`);
    if (mods.strengthModifier) p.push(`S${mods.strengthModifier > 0 ? '+' : ''}${mods.strengthModifier}`);
    if (mods.toughnessModifier) p.push(`T${mods.toughnessModifier > 0 ? '+' : ''}${mods.toughnessModifier}`);
    if (mods.rerollWoundAll) p.push('RR All'); else if (mods.rerollWoundOnes) p.push('RR 1s');
    if (mods.fishForCritWounds) p.push('Fish');
    if (mods.critWoundOn5Plus) p.push('Crit W 5+');
    if (mods.devastatingWoundsOverride) p.push('DevW');
    if (mods.antiKeyword && mods.antiThreshold) p.push(`Anti-${mods.antiKeyword} ${mods.antiThreshold}+`);
    return p.join(' · ');
}
function saveSummary() {
    const p = [];
    if (mods.cover) p.push('Cover');
    if (mods.ignoresCover) p.push('Ign Cover');
    if (mods.apModifier) p.push(`AP${mods.apModifier > 0 ? '+' : ''}${mods.apModifier}`);
    return p.join(' · ');
}
function damageSummary() {
    const p = [];
    if (mods.damageModifier) p.push(`${mods.damageModifier > 0 ? '+' : ''}${mods.damageModifier}D`);
    if (mods.rerollDamageDice) p.push('RR');
    if (mods.fnpOverride) p.push(`FNP${mods.fnpOverride}+`);
    return p.join(' · ');
}

function updateSummary(section) {
    const el = document.getElementById(`summary-${section}`);
    if (!el) return;
    const fns = { attack: attackSummary, hit: hitSummary, wound: woundSummary, save: saveSummary, damage: damageSummary };
    el.textContent = fns[section]?.() || '';
}

// ── Modifier actions (called from onclick attributes in generated HTML) ────────

const fieldSection = {
    blastOverride: 'attack', rerollAttackDice: 'attack',
    withinHalfRange: 'hit', rerollHitOnes: 'hit', rerollHitAll: 'hit',
    fishForCritHits: 'hit', indirectFire: 'hit', critHitOn5Plus: 'hit',
    sustainedHitsOverride: 'hit', lethalHitsOverride: 'hit',
    rerollWoundOnes: 'wound', rerollWoundAll: 'wound', fishForCritWounds: 'wound',
    critWoundOn5Plus: 'wound', devastatingWoundsOverride: 'wound',
    cover: 'save', ignoresCover: 'save',
    rerollDamageDice: 'damage',
};

function toggleMod(field) {
    mods[field] = !mods[field];

    // Mutual exclusivity: reroll ones/all
    if (field === 'rerollHitOnes' && mods.rerollHitOnes) { mods.rerollHitAll = false; _updateBtn('rerollHitAll'); }
    if (field === 'rerollHitAll' && mods.rerollHitAll) { mods.rerollHitOnes = false; _updateBtn('rerollHitOnes'); }
    if (field === 'rerollWoundOnes' && mods.rerollWoundOnes) { mods.rerollWoundAll = false; _updateBtn('rerollWoundAll'); }
    if (field === 'rerollWoundAll' && mods.rerollWoundAll) { mods.rerollWoundOnes = false; _updateBtn('rerollWoundOnes'); }

    // Fish for crits only valid when reroll all is on
    if (!mods.rerollHitAll) { mods.fishForCritHits = false; _updateBtn('fishForCritHits'); }
    if (!mods.rerollWoundAll) { mods.fishForCritWounds = false; _updateBtn('fishForCritWounds'); }

    // Sync Fish visibility
    _setVisible('tog-fishForCritHits', mods.rerollHitAll);
    _setVisible('tog-fishForCritWounds', mods.rerollWoundAll);

    _updateBtn(field);
    const section = fieldSection[field];
    if (section) updateSummary(section);
}

function stepMod(field, delta) {
    mods[field] = (mods[field] || 0) + delta;
    if (field === 'hitRollModifier') mods[field] = Math.max(-1, Math.min(1, mods[field]));
    if (field === 'woundRollModifier') mods[field] = Math.max(-1, Math.min(1, mods[field]));
    const valEl = document.getElementById(`val-${field}`);
    if (valEl) valEl.textContent = mods[field] > 0 ? `+${mods[field]}` : `${mods[field]}`;
    const section = { attackModifier: 'attack', hitRollModifier: 'hit', bsWsModifier: 'hit', woundRollModifier: 'wound', strengthModifier: 'wound', toughnessModifier: 'wound', apModifier: 'save', damageModifier: 'damage' }[field];
    if (section) updateSummary(section);
}

function setModText(field, val, section) {
    mods[field] = val;
    if (section) updateSummary(section);
}
function setModNum(field, val, section) {
    mods[field] = val;
    if (section) updateSummary(section);
}

function _updateBtn(field) {
    const btn = document.getElementById(`tog-${field}`);
    if (btn) btn.classList.toggle('active', !!mods[field]);
}
function _setVisible(id, visible) {
    const el = document.getElementById(id);
    if (el) el.style.display = visible ? '' : 'none';
}

// ── Panel listeners (inputs that update state without full rebuild) ────────────

function attachPanelListeners() {
    document.querySelectorAll('.panel-count-input').forEach(input => {
        input.addEventListener('change', () => {
            const idx = parseInt(input.dataset.selIdx);
            if (idx >= 0 && idx < state.selections.length)
                state.selections[idx].modelCount = parseInt(input.value) || 1;
        });
    });

    const defCount = document.getElementById('mod-defenderModelCount');
    if (defCount) defCount.addEventListener('change', () => {
        mods.defenderModelCount = parseInt(defCount.value) || 0;
    });
}

// ── Run simulation ────────────────────────────────────────────────────────────

function runSimulation() {
    if (!state.selections.length || !state.defenderCardId) return;
    const btn = document.querySelector('.btn-run');
    if (btn) { btn.disabled = true; btn.textContent = 'Running…'; }

    fetch('/api/simulate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(buildRequest()),
    })
        .then(r => r.ok ? r.json() : r.json().then(e => { throw new Error(e.error || `HTTP ${r.status}`); }))
        .then(resp => {
            displayPipeline(resp);
            if (btn) { btn.disabled = false; btn.textContent = 'Run Simulation'; }
            document.getElementById('pipeline-content')?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        })
        .catch(err => {
            const pc = document.getElementById('pipeline-content');
            if (pc) pc.innerHTML = `<div class="sim-error">Error: ${escHtml(err.message)}</div>`;
            if (btn) { btn.disabled = false; btn.textContent = 'Run Simulation'; }
        });
}

function buildRequest() {
    return {
        attackerName: state.attackerUnitName || '',
        defenderName: state.defenderUnitName || '',
        weaponSelections: state.selections.map(s => ({
            weaponName: s.weaponName, variantName: s.variantName,
            modelName: s.modelName, modelCount: s.modelCount,
            weaponType: s.weaponType, unitName: s.unitName,
        })),
        defenderModelCount: mods.defenderModelCount,
        attackModifier: mods.attackModifier, blastOverride: mods.blastOverride,
        rerollAttackDice: mods.rerollAttackDice,
        withinHalfRange: mods.withinHalfRange, hitRollModifier: mods.hitRollModifier,
        bsWsModifier: mods.bsWsModifier, rerollHitOnes: mods.rerollHitOnes,
        rerollHitAll: mods.rerollHitAll, fishForCritHits: mods.fishForCritHits,
        indirectFire: mods.indirectFire, critHitOn5Plus: mods.critHitOn5Plus,
        sustainedHitsOverride: mods.sustainedHitsOverride, lethalHitsOverride: mods.lethalHitsOverride,
        woundRollModifier: mods.woundRollModifier, strengthModifier: mods.strengthModifier,
        toughnessModifier: mods.toughnessModifier, rerollWoundOnes: mods.rerollWoundOnes,
        rerollWoundAll: mods.rerollWoundAll, fishForCritWounds: mods.fishForCritWounds,
        critWoundOn5Plus: mods.critWoundOn5Plus, devastatingWoundsOverride: mods.devastatingWoundsOverride,
        antiKeyword: mods.antiKeyword, antiThreshold: mods.antiThreshold,
        cover: mods.cover, ignoresCover: mods.ignoresCover, apModifier: mods.apModifier,
        damageModifier: mods.damageModifier, rerollDamageDice: mods.rerollDamageDice,
        fnpOverride: mods.fnpOverride,
    };
}

// ── Pipeline display ──────────────────────────────────────────────────────────

function displayPipeline(resp) {
    const el = document.getElementById('pipeline-content');
    if (!el) return;

    let html = `<div class="sim-results">
        <div class="sim-stats">
            <div class="sim-stat"><div class="sim-stat-label">Mean Damage</div><div class="sim-stat-value">${fmt(resp.meanDamage)}</div></div>
            <div class="sim-stat"><div class="sim-stat-label">Exp. Kills</div><div class="sim-stat-value">${fmt(resp.expectedKills)}</div></div>
            <div class="sim-stat"><div class="sim-stat-label">P(kill ≥ 1)</div><div class="sim-stat-value">${pct(resp.pKillAtLeastOne)}</div></div>
            <div class="sim-stat"><div class="sim-stat-label">Std Dev</div><div class="sim-stat-value">${fmt(resp.stdDev)}</div></div>
        </div>`;

    if (resp.weaponBreakdown && resp.weaponBreakdown.length > 0) {
        resp.weaponBreakdown.forEach(g => {
            const groupFinal = g.stats.avgDamageBeforeFnp - g.stats.avgFnpSaved;
            html += `<div class="pipeline-weapon-header">${escHtml(g.weaponName)}</div>`;
            html += buildFullFunnel(g.stats, groupFinal);
        });
        html += `<div class="pipeline-weapon-header">Combined</div>`;
        html += buildCompactFunnel(resp.stageStats, resp.meanDamage);
    } else {
        html += buildFullFunnel(resp.stageStats, resp.meanDamage);
    }

    html += `</div>`;
    el.innerHTML = html;
}

function buildFullFunnel(s, finalDamage) {
    let rows = '';
    rows += fRow('Attacks', s.avgAttacks, null, true);
    rows += fRow('Hits', s.avgHits, s.avgAttacks);
    if (s.avgCritHits >= 0.001) rows += fSub('Crit hits', s.avgCritHits);
    if (s.avgSustainedHitsBonus >= 0.001) rows += fSub('Sustained +', s.avgSustainedHitsBonus);
    rows += fRow('Wounds', s.avgWounds, s.avgHits);
    if (s.avgLethalHitsAutoWounds >= 0.001) rows += fSub('Lethal auto', s.avgLethalHitsAutoWounds);
    if (s.avgCritWounds >= 0.001) rows += fSub('Crit wounds', s.avgCritWounds);
    if (s.avgAntiCritWounds >= 0.001) rows += fSub('Anti crits', s.avgAntiCritWounds);
    const totalSaveFails = (s.avgFailedSaves || 0) + (s.avgDevastatingWoundsTriggers || 0);
    rows += fRow('Failed saves', totalSaveFails, s.avgWounds);
    if (s.avgArmourSaveRolls >= 0.001) rows += fSub('Armour saves', s.avgArmourSaveRolls);
    if (s.avgInvulnSaveRolls >= 0.001) rows += fSub('Invuln saves', s.avgInvulnSaveRolls);
    if (s.avgDevastatingWoundsTriggers >= 0.001) rows += fSub('DevW bypass', s.avgDevastatingWoundsTriggers);
    rows += fRow('Dmg pre-FNP', s.avgDamageBeforeFnp, null);
    if (s.avgFnpSaved >= 0.001) rows += fSub('FNP saved', s.avgFnpSaved);
    rows += fRow('Final Damage', finalDamage, null, true, 'final');
    return `<table class="pipeline-table"><thead><tr><th>Stage</th><th>Avg</th><th>Rate</th></tr></thead><tbody>${rows}</tbody></table>`;
}

function buildCompactFunnel(s, finalDamage) {
    let rows = '';
    rows += fRow('Attacks', s.avgAttacks);
    rows += fRow('Hits', s.avgHits);
    rows += fRow('Wounds', s.avgWounds);
    rows += fRow('Failed saves', (s.avgFailedSaves || 0) + (s.avgDevastatingWoundsTriggers || 0));
    rows += fRow('Dmg pre-FNP', s.avgDamageBeforeFnp);
    if (s.avgFnpSaved >= 0.001) rows += fRow('FNP saved', s.avgFnpSaved);
    rows += fRow('Final Damage', finalDamage, null, true, 'final');
    return `<table class="pipeline-table compact"><thead><tr><th>Stage</th><th>Avg</th><th></th></tr></thead><tbody>${rows}</tbody></table>`;
}

function fRow(label, val, prev, bold, cls) {
    const rate = (prev != null && prev > 0.001) ? pct(val / prev) : '';
    const c = cls ? ` class="${cls}"` : '';
    const w = bold ? ' style="font-weight:600"' : '';
    return `<tr${c}><td${w}>${label}</td><td${w}>${fmt(val)}</td><td class="rate">${rate}</td></tr>`;
}
function fSub(label, val) {
    return `<tr class="sub-row"><td class="sub-label">↳ ${label}</td><td>${fmt(val)}</td><td></td></tr>`;
}

// ── Utilities ─────────────────────────────────────────────────────────────────

function fmt(v) { return (typeof v === 'number' && isFinite(v)) ? v.toFixed(2) : '—'; }
function pct(v) { return (typeof v === 'number' && isFinite(v)) ? (v * 100).toFixed(1) + '%' : '—'; }
function escHtml(s) {
    return String(s || '')
        .replace(/&/g, '&amp;').replace(/</g, '&lt;')
        .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

// refreshCatalogues is defined inline in ArmyView.cshtml Scripts section
