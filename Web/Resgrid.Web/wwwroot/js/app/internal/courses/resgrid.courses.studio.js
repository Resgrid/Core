/*
 * The course studio (Training T5, Areas/User/Views/LmsStudio): the "draft a course" form (protocol or uploaded SOP, one submit while the
 * model works) and a course's flashcard drafting. Drafted cards are rendered with text nodes only (never HTML from the server); nothing is
 * saved until the author ticks the cards to keep and accepts them, and the server then reads the cards back from its own stored draft.
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

    /* The draft form: show the panel of the chosen source, and submit once (the model may take a minute). */
    function draftForm() {
        var form = document.getElementById('studio-draft-form');
        if (!form) {
            return;
        }
        var file = document.getElementById('studio-file');
        function sync() {
            var chosen = form.querySelector('input[name="sourceKind"]:checked');
            var kind = chosen ? chosen.getAttribute('data-studio-source') : 'protocol';
            Array.prototype.forEach.call(form.querySelectorAll('[data-studio-panel]'), function (panel) {
                panel.hidden = panel.getAttribute('data-studio-panel') !== kind;
            });
            if (file) {
                file.required = kind === 'document';
            }
        }
        Array.prototype.forEach.call(form.querySelectorAll('input[name="sourceKind"]'), function (radio) {
            radio.addEventListener('change', sync);
        });
        sync();

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
            var busy = document.getElementById('studio-draft-busy');
            if (busy) {
                busy.textContent = form.getAttribute('data-busy-text') || '';
            }
        });
    }

    /* Flashcards: draft from the chosen source, tick the ones to keep, accept. */
    function flashcards() {
        var panel = document.getElementById('studio-cards');
        var form = document.getElementById('studio-cards-form');
        if (!panel || !form) {
            return;
        }
        var status = document.getElementById('studio-cards-status');
        var host = document.getElementById('studio-cards-draft');
        var source = document.getElementById('studio-card-source');
        var count = document.getElementById('studio-card-count');

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
            var parts = String(source.value || '').split(':');
            var data = new FormData();
            data.append('__RequestVerificationToken', token(form));
            data.append('sourceKind', parts[0] || '0');
            data.append('protocolId', parts[1] || '0');
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
        draftForm();
        flashcards();
    });
})();
