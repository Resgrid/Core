/*
 * Practice cards in the course player (Training T5, Areas/User/Views/Courses/_PracticeCards.cshtml). Reads the learner's deck once and runs
 * a simple spaced practice (Leitner boxes: a card marked "again" comes back soon, a known card moves to a later box). Which cards a learner
 * knows is kept in this browser only (localStorage, guarded): nothing is sent back, graded or recorded. Text nodes only.
 */
(function () {
    'use strict';

    var BOXES = 3;

    function ready(fn) {
        if (document.readyState !== 'loading') {
            fn();
        } else {
            document.addEventListener('DOMContentLoaded', fn);
        }
    }

    function load(key) {
        try {
            var raw = window.localStorage.getItem(key);
            var value = raw ? JSON.parse(raw) : null;
            return value && typeof value === 'object' ? value : {};
        } catch (e) {
            return {};
        }
    }

    function save(key, value) {
        try {
            window.localStorage.setItem(key, JSON.stringify(value));
        } catch (e) {
            /* Private windows and blocked storage: practice still works for this visit. */
        }
    }

    ready(function () {
        var panel = document.getElementById('lms-practice-cards');
        if (!panel || !window.fetch) {
            return;
        }
        var storageKey = 'rg-lms-cards-' + panel.getAttribute('data-course');
        var boxes = load(storageKey);
        var front = document.getElementById('lms-practice-front');
        var back = document.getElementById('lms-practice-back');
        var answer = document.getElementById('lms-practice-answer');
        var source = document.getElementById('lms-practice-source');
        var progress = document.getElementById('lms-practice-progress');
        var show = document.getElementById('lms-practice-show');
        var again = document.getElementById('lms-practice-again');
        var known = document.getElementById('lms-practice-known');
        var restart = document.getElementById('lms-practice-restart');
        var cards = [];
        var queue = [];
        var current = null;

        function box(card) {
            var value = parseInt(boxes[String(card.id)], 10);
            return isNaN(value) ? 0 : Math.max(0, Math.min(BOXES, value));
        }

        function build() {
            /* Lower boxes first; cards in the last box are done for now. */
            queue = cards.filter(function (c) { return box(c) < BOXES; }).sort(function (a, b) { return box(a) - box(b); });
        }

        function next() {
            back.hidden = true;
            again.hidden = true;
            known.hidden = true;
            show.hidden = false;
            var done = cards.filter(function (c) { return box(c) >= BOXES; }).length;
            progress.textContent = String(panel.getAttribute('data-card-format') || '').replace('{0}', String(done)).replace('{1}', String(cards.length));
            current = queue.shift() || null;
            if (!current) {
                front.textContent = panel.getAttribute('data-done-text') || '';
                show.hidden = true;
                return;
            }
            front.textContent = current.front;
            answer.textContent = current.back;
            source.textContent = '';
            if (current.source) {
                if (current.url) {
                    var link = document.createElement('a');
                    link.href = current.url;
                    link.textContent = current.source;
                    source.appendChild(document.createTextNode((panel.getAttribute('data-source-text') || '') + ' '));
                    source.appendChild(link);
                } else {
                    source.textContent = (panel.getAttribute('data-source-text') || '') + ' ' + current.source;
                }
            }
        }

        show.addEventListener('click', function () {
            back.hidden = false;
            show.hidden = true;
            again.hidden = false;
            known.hidden = false;
            known.focus();
        });
        again.addEventListener('click', function () {
            if (!current) {
                return;
            }
            boxes[String(current.id)] = 0;
            save(storageKey, boxes);
            /* Seen again after a couple of other cards. */
            queue.splice(Math.min(2, queue.length), 0, current);
            next();
        });
        known.addEventListener('click', function () {
            if (!current) {
                return;
            }
            boxes[String(current.id)] = box(current) + 1;
            save(storageKey, boxes);
            if (box(current) < BOXES) {
                queue.push(current);
            }
            next();
        });
        restart.addEventListener('click', function () {
            boxes = {};
            save(storageKey, boxes);
            build();
            next();
        });

        fetch(panel.getAttribute('data-deck-url'), { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (response) { return response.ok ? response.json() : { cards: [] }; })
            .then(function (deck) {
                cards = (deck && deck.cards) || [];
                if (cards.length === 0) {
                    return;
                }
                panel.hidden = false;
                build();
                next();
            })
            .catch(function () { /* No deck: the panel stays hidden. */ });
    });
})();
