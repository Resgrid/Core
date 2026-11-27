/*
 * K2 drafting aids panel (Areas/User/Views/Shared/_AuthoringAids.cshtml). Asks AuthoringAids/Status and shows the panel only when the
 * department has knowledge answers (Enhanced AI); posts one draft request with the page's antiforgery token (the host editor's unsaved
 * text when it names one) and renders the draft with text nodes only (never HTML from the server). Nothing is saved until the author
 * ticks what to keep and presses Accept: the panel then posts the kept items to the host's accept URL (the course builder), or raises the
 * "rg:authoring-aid:accept" event for the host page's script (the training editor), or fills the host's change-summary fields.
 */
(function () {
    'use strict';
    if (window.rgAuthoringAidsReady) {
        return;
    }
    window.rgAuthoringAidsReady = true;

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

    function format(template, value) {
        return String(template || '').replace('{0}', String(value));
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

    /* The host editor's unsaved content: a Quill editor's root, a textarea or input, or any element's HTML. */
    function contentOf(selector) {
        if (!selector) {
            return null;
        }
        var node = document.querySelector(selector);
        if (!node) {
            return null;
        }
        var quill = node.querySelector('.ql-editor');
        if (quill) {
            return quill.innerHTML;
        }
        if ('value' in node && node.tagName !== 'DIV') {
            return node.value;
        }
        return node.innerHTML;
    }

    function init(panel) {
        var text = {};
        try {
            text = JSON.parse(panel.getAttribute('data-text') || '{}');
        } catch (e) {
            text = {};
        }
        var result = panel.querySelector('[data-role="result"]');
        var statusMessage = panel.querySelector('[data-role="status-message"]');
        var tokenInput = panel.querySelector('input[name="__RequestVerificationToken"]');
        var buttons = Array.prototype.slice.call(panel.querySelectorAll('[data-aid]'));
        if (!result || !tokenInput) {
            return;
        }

        fetch(panel.getAttribute('data-status-url'), { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (response) { return response.ok ? response.json() : null; })
            .then(function (status) {
                if (!status) {
                    return; // The free tier (no knowledge answers) never sees the panel.
                }
                panel.hidden = false;
                if (!status.available) {
                    buttons.forEach(function (b) { b.disabled = true; });
                    if (status.message) {
                        statusMessage.textContent = status.message;
                        statusMessage.hidden = false;
                    }
                }
            })
            .catch(function () { /* stays hidden */ });

        function busy(on) {
            buttons.forEach(function (b) { b.disabled = on; });
        }

        function message(className, value) {
            result.textContent = '';
            if (value) {
                result.appendChild(el('p', className, value));
            }
        }

        buttons.forEach(function (button) {
            button.addEventListener('click', function () {
                var kind = parseInt(button.getAttribute('data-aid'), 10);
                var excludeSelector = panel.getAttribute('data-exclude-selector');
                var exclude = excludeSelector ? document.querySelector(excludeSelector) : null;
                if (exclude && exclude.checked) {
                    message('text-warning', text.excluded);
                    return;
                }
                var data = new FormData();
                data.append('__RequestVerificationToken', tokenInput.value);
                data.append('Kind', String(kind));
                data.append('SourceKind', panel.getAttribute('data-source-kind'));
                data.append('SourceId', panel.getAttribute('data-source-id') || '0');
                var count = panel.querySelector('[data-role="count"]');
                data.append('QuestionCount', count ? count.value : '5');
                var content = contentOf(panel.getAttribute('data-content-selector'));
                if (content !== null) {
                    data.append('ContentHtml', content);
                }
                var titleSelector = panel.getAttribute('data-title-selector');
                var title = titleSelector ? document.querySelector(titleSelector) : null;
                if (title && 'value' in title) {
                    data.append('ContentTitle', title.value);
                }
                data.append('ExcludeFromAi', exclude && exclude.checked ? 'true' : 'false');

                busy(true);
                message('text-muted', text.drafting);
                post(panel.getAttribute('data-draft-url'), data)
                    .then(function (draft) { render(draft); })
                    .catch(function () { message('text-danger', text.error); })
                    .then(function () { busy(false); });
            });
        });

        function checkbox(label, checked) {
            var wrap = el('label', 'rgw-aid-keep');
            var box = el('input');
            box.type = 'checkbox';
            box.checked = checked;
            wrap.appendChild(box);
            wrap.appendChild(document.createTextNode(' ' + label));
            return { wrap: wrap, box: box };
        }

        function render(draft) {
            result.textContent = '';
            if (!draft || draft.outcome !== 'Drafted') {
                message(draft && draft.outcome === 'Excluded' ? 'text-warning' : 'text-muted', draft && draft.message ? draft.message : text.error);
                return;
            }
            var intro = el('p', 'text-info small');
            intro.appendChild(icon('fa-info-circle'));
            intro.appendChild(document.createTextNode(' ' + draft.message));
            result.appendChild(intro);
            if (draft.notice) {
                result.appendChild(el('p', 'text-warning small', draft.notice));
            }

            var items = [];
            if (draft.kind === 0) {
                draft.questions.forEach(function (q, index) {
                    var card = el('div', 'rgw-builder-section m-b-sm');
                    var keep = checkbox(format(text.questionNumber, index + 1), true);
                    card.appendChild(keep.wrap);
                    card.appendChild(el('p', 'm-b-xs', q.text));
                    var list = el('ol', 'm-b-xs');
                    list.setAttribute('type', 'a');
                    q.options.forEach(function (option, optionIndex) {
                        var li = el('li', optionIndex === q.correctIndex ? 'text-navy' : null, option);
                        if (optionIndex === q.correctIndex) {
                            li.appendChild(document.createTextNode(' '));
                            var badge = el('span', 'label label-primary', text.correct);
                            li.appendChild(badge);
                        }
                        list.appendChild(li);
                    });
                    card.appendChild(list);
                    card.appendChild(el('p', 'small text-muted', text.explanation + ': ' + q.explanation));
                    result.appendChild(card);
                    items.push({ box: keep.box, value: q });
                });
            } else if (draft.kind === 1) {
                draft.outline.forEach(function (module, index) {
                    var card = el('div', 'rgw-builder-section m-b-sm');
                    var keep = checkbox(format(text.moduleNumber, index + 1) + ': ' + module.title, true);
                    card.appendChild(keep.wrap);
                    if (module.objective) {
                        card.appendChild(el('p', 'small text-muted', text.objective + ': ' + module.objective));
                    }
                    var list = el('ol', 'm-b-xs');
                    module.lessons.forEach(function (lesson) { list.appendChild(el('li', null, lesson)); });
                    card.appendChild(list);
                    result.appendChild(card);
                    items.push({ box: keep.box, value: module });
                });
            } else {
                var list = el('ul', 'm-b-xs');
                draft.changes.forEach(function (change) {
                    var li = el('li');
                    var keep = checkbox(change, true);
                    li.appendChild(keep.wrap);
                    list.appendChild(li);
                    items.push({ box: keep.box, value: change });
                });
                result.appendChild(list);
            }

            var actions = el('div', 'rgw-toolbar m-t-sm');
            var accept = el('button', 'btn btn-primary btn-sm');
            accept.type = 'button';
            accept.appendChild(icon('fa-check'));
            accept.appendChild(document.createTextNode(' ' + text.accept));
            var discard = el('button', 'btn btn-white btn-sm');
            discard.type = 'button';
            discard.appendChild(icon('fa-times'));
            discard.appendChild(document.createTextNode(' ' + text.discard));
            actions.appendChild(accept);
            actions.appendChild(discard);
            result.appendChild(actions);

            discard.addEventListener('click', function () { result.textContent = ''; });
            accept.addEventListener('click', function () {
                var kept = items.filter(function (i) { return i.box.checked; }).map(function (i) { return i.value; });
                if (kept.length === 0) {
                    message('text-warning', text.nothingSelected);
                    return;
                }
                acceptDraft(draft.kind, kept);
            });
        }

        function acceptDraft(kind, kept) {
            if (kind === 2) {
                var targets = panel.getAttribute('data-summary-target');
                var summary = kept.join(' ');
                if (targets) {
                    Array.prototype.forEach.call(document.querySelectorAll(targets), function (input) {
                        input.value = summary.substring(0, input.maxLength > 0 ? input.maxLength : summary.length);
                        input.dispatchEvent(new Event('change', { bubbles: true }));
                    });
                }
                document.dispatchEvent(new CustomEvent('rg:authoring-aid:accept', { detail: { kind: 'changeSummary', changes: kept, summary: summary } }));
                message('text-success', text.acceptedSummary);
                return;
            }

            var url = panel.getAttribute(kind === 0 ? 'data-accept-questions-url' : 'data-accept-outline-url');
            if (!url) {
                document.dispatchEvent(new CustomEvent('rg:authoring-aid:accept', kind === 0
                    ? { detail: { kind: 'quiz', questions: kept } }
                    : { detail: { kind: 'outline', modules: kept } }));
                message('text-success', format(text.accepted, kept.length));
                return;
            }

            var data = new FormData();
            data.append('__RequestVerificationToken', tokenInput.value);
            data.append(kind === 0 ? 'questions' : 'outline', JSON.stringify(kept));
            busy(true);
            post(url, data)
                .then(function (outcome) {
                    message(outcome && outcome.saved ? 'text-success' : 'text-danger', outcome && outcome.message ? outcome.message : text.error);
                    if (outcome && outcome.saved && outcome.link) {
                        var link = el('a', null, outcome.linkText || outcome.link);
                        if (outcome.link.charAt(0) === '/' && outcome.link.charAt(1) !== '/') {
                            link.href = outcome.link;
                            result.appendChild(link);
                        }
                    }
                    if (outcome && outcome.saved && outcome.reload) {
                        window.setTimeout(function () { window.location.reload(); }, 800);
                    }
                })
                .catch(function () { message('text-danger', text.error); })
                .then(function () { busy(false); });
        }
    }

    ready(function () {
        Array.prototype.forEach.call(document.querySelectorAll('.rgw-authoring-aids'), init);
    });
})();
