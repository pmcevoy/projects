// army-view.js — unit card expand/collapse
// Weapon selection and combat panel are implemented in Session 6.

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
