/*
 * Enhanced AI on protocols (round 7A; Areas/User/Views/ProtocolAi): a protocol's flashcard drafting (draft, tick the cards to keep, accept) and
 * the training draft form (one submit while the model works). Drafted cards are rendered with text nodes only (never HTML from the server);
 * nothing is saved until the author accepts, and the server then reads the cards back from its own stored draft.
 */
(function () {
    'use strict';

    function ready(fn) {
        if (document.readyState !== 'loading') {
            fn();
        } else {
            document.addEventListener('DOMContentLoaded', fn);
        }
    }

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) {
            node.className = className;
        }
        if (text !== undefined && text !== null) {
            node.textContent = text;
        }
        return node;
    }

    function icon(name) {
        var i = el('i', 'fa ' + name);
        i.setAttribute('aria-hidden', 'true');
        return i;
    }

    function token(scope) {
        var input = (scope || document).querySelector('input[name="__RequestVerificationToken"]') || document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function post(url, data) {
        return fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Accept': 'application/json' },
            body: data
        }).then(function (response) {
            if (!response.ok) {
                throw new Error('HTTP ' + response.status);
            }
            return response.json();
        });
    }

    /* The training and translation draft forms: submit once (the model may take a minute). */
    function submitOnce(formId, busyId) {
        var form = document.getElementById(formId);
        if (!form) {
            return;
        }
        form.addEventListener('submit', function (event) {
            if (form.getAttribute('data-submitting') === 'true') {
                event.preventDefault();
                return;
            }
            form.setAttribute('data-submitting', 'true');
            var button = form.querySelector('button[type="submit"]');
            if (button) {
                button.disabled = true;
            }
            var busy = document.getElementById(busyId);
            if (busy) {
                busy.textContent = form.getAttribute('data-busy-text') || '';
            }
        });
    }

    /* Flashcards: draft from the version in force, tick the ones to keep, accept. */
    function flashcards() {
        var panel = document.getElementById('protocol-ai-cards');
        var form = document.getElementById('protocol-ai-cards-form');
        if (!panel || !form || !window.fetch) {
            return;
        }
        var status = document.getElementById('protocol-ai-cards-status');
        var host = document.getElementById('protocol-ai-cards-draft');
        var count = document.getElementById('protocol-ai-card-count');

        function say(text) {
            status.textContent = text || '';
        }

        function render(result) {
            host.textContent = '';
            if (result.source) {
                host.appendChild(el('p', 'small text-muted', String(panel.getAttribute('data-source-text') || '').replace('{0}', result.source)));
            }
            if (result.droppedText) {
                host.appendChild(el('p', 'small text-muted', result.droppedText));
            }
            var list = el('ul', 'list-unstyled');
            (result.cards || []).forEach(function (card) {
                var item = el('li', 'm-b-sm');
                var label = el('label', '');
                var box = document.createElement('input');
                box.type = 'checkbox';
                box.checked = true;
                box.value = String(card.index);
                label.appendChild(box);
                label.appendChild(document.createTextNode(' '));
                label.appendChild(el('strong', '', card.front));
                item.appendChild(label);
                item.appendChild(el('div', '', card.back));
                if (card.heading) {
                    item.appendChild(el('div', 'small text-muted', card.heading));
                }
                list.appendChild(item);
            });
            host.appendChild(list);

            var accept = el('button', 'btn btn-primary');
            accept.type = 'button';
            accept.appendChild(icon('fa-check'));
            accept.appendChild(document.createTextNode(' ' + (panel.getAttribute('data-accept-text') || '')));
            accept.addEventListener('click', function () {
                var ticked = Array.prototype.filter.call(list.querySelectorAll('input[type="checkbox"]'), function (b) { return b.checked; });
                if (ticked.length === 0) {
                    say(panel.getAttribute('data-none-text'));
                    return;
                }
                accept.disabled = true;
                var data = new FormData();
                data.append('__RequestVerificationToken', token(form));
                data.append('exchangeId', result.exchangeId);
                ticked.forEach(function (b) { data.append('indexes', b.value); });
                post(panel.getAttribute('data-accept-url'), data).then(function (reply) {
                    say(reply.message);
                    if (reply.ok && reply.reload) {
                        window.location.reload();
                    } else {
                        accept.disabled = false;
                    }
                }).catch(function () {
                    say(panel.getAttribute('data-failed-text'));
                    accept.disabled = false;
                });
            });
            host.appendChild(accept);
            accept.focus();
        }

        form.addEventListener('submit', function (event) {
            event.preventDefault();
            var button = form.querySelector('button[type="submit"]');
            var data = new FormData();
            data.append('__RequestVerificationToken', token(form));
            data.append('count', count.value || '10');
            button.disabled = true;
            host.textContent = '';
            say(panel.getAttribute('data-busy-text'));
            post(panel.getAttribute('data-draft-url'), data).then(function (result) {
                button.disabled = false;
                if (!result.ok) {
                    say(result.message);
                    return;
                }
                say('');
                render(result);
            }).catch(function () {
                button.disabled = false;
                say(panel.getAttribute('data-failed-text'));
            });
        });
    }

    ready(function () {
        submitOnce('protocol-ai-training-form', 'protocol-ai-training-busy');
        submitOnce('protocol-ai-translation-form', 'protocol-ai-translation-busy');
        flashcards();
    });
})();
