/*
 * K3 "Practice" panel (Areas/User/Views/Shared/_KnowledgePractice.cshtml). Asks KnowledgePractice/Status and shows the panel only when the
 * department has the tutor; starts a scenario from the protocol's version in force, lets the member answer each question in their own
 * words, and shows each score with feedback linked to the protocol sections it rests on. Everything is written with text nodes only (never
 * HTML from the server). Practice only: nothing is recorded on the transcript.
 */
(function () {
    'use strict';
    if (window.rgKnowledgePracticeReady) {
        return;
    }
    window.rgKnowledgePracticeReady = true;

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

    function safeUrl(url) {
        if (typeof url !== 'string' || url.length === 0) {
            return null;
        }
        if (url.charAt(0) === '/' && url.charAt(1) !== '/') {
            return url;
        }
        return /^https:\/\//i.test(url) ? url : null;
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

    function init(panel) {
        var text = {};
        try {
            text = JSON.parse(panel.getAttribute('data-text') || '{}');
        } catch (e) {
            text = {};
        }

        var form = panel.querySelector('[data-role="start-form"]');
        var area = panel.querySelector('[data-role="practice"]');
        if (!form || !area) {
            return;
        }
        var token = form.querySelector('input[name="__RequestVerificationToken"]');
        var citationsByRef = {};

        fetch(panel.getAttribute('data-status-url'), { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (response) { return response.ok ? response.json() : null; })
            .then(function (status) {
                if (status && status.available) {
                    panel.hidden = false;
                }
            })
            .catch(function () { /* The panel stays hidden. */ });

        function message(value, className) {
            area.textContent = '';
            var p = el('p', className || 'text-muted', value);
            p.setAttribute('role', 'status');
            area.appendChild(p);
        }

        function refLinks(refs) {
            var span = el('span', 'small');
            (refs || []).forEach(function (ref) {
                var citation = citationsByRef[ref];
                var url = citation ? safeUrl(citation.url) : null;
                var node = el(url ? 'a' : 'span', 'label label-default', ref);
                if (url) {
                    node.setAttribute('href', url);
                    node.setAttribute('title', (text.openSource || '') + ' ' + (citation.title || ''));
                }
                span.appendChild(document.createTextNode(' '));
                span.appendChild(node);
            });
            return span;
        }

        function sources(citations) {
            if (!citations || citations.length === 0) {
                return null;
            }
            var box = el('div', 'm-t-sm');
            box.appendChild(el('h6', null, text.sources));
            var list = el('ol', 'list-unstyled');
            citations.forEach(function (c) {
                var item = el('li', 'm-b-xs');
                item.appendChild(el('span', 'label label-default', c.ref));
                item.appendChild(document.createTextNode(' '));
                var url = safeUrl(c.url);
                var link = el(url ? 'a' : 'span', null, c.title || '');
                if (url) {
                    link.setAttribute('href', url);
                }
                item.appendChild(link);
                var meta = [c.section, c.version, c.effective].filter(function (x) { return x; }).join(' · ');
                if (meta) {
                    item.appendChild(el('small', 'rgw-sub text-muted', meta));
                }
                list.appendChild(item);
            });
            box.appendChild(list);
            return box;
        }

        function againButton() {
            var again = el('button', 'btn btn-default btn-sm m-t-sm');
            again.type = 'button';
            again.appendChild(icon('fa-refresh'));
            again.appendChild(document.createTextNode(' ' + (text.again || '')));
            again.addEventListener('click', start);
            return again;
        }

        function renderScore(result, questionsByKey) {
            var box = el('div', 'm-t-sm');
            if (result.message) {
                var lead = el('p', result.outcome === 'Scored' ? 'font-bold' : 'text-warning', result.message);
                lead.setAttribute('role', 'status');
                box.appendChild(lead);
            }
            (result.citations || []).forEach(function (c) { citationsByRef[c.ref] = c; });
            (result.items || []).forEach(function (item) {
                var row = el('div', 'well well-sm m-b-xs');
                var head = el('p', 'm-b-xs');
                head.appendChild(el('strong', null, item.key + ' · ' + (item.score || '')));
                row.appendChild(head);
                var question = questionsByKey[item.key];
                if (question) {
                    row.appendChild(el('p', 'small text-muted m-b-xs', question.text || ''));
                }
                if (item.feedback) {
                    var p = el('p', 'm-b-none', item.feedback);
                    p.style.whiteSpace = 'pre-wrap';
                    p.appendChild(refLinks(item.refs));
                    row.appendChild(p);
                }
                box.appendChild(row);
            });
            (result.notices || []).forEach(function (notice) {
                var n = el('p', 'text-info small');
                n.appendChild(icon('fa-info-circle'));
                n.appendChild(document.createTextNode(' ' + notice));
                box.appendChild(n);
            });
            var dosing = safeUrl(result.dosingToolUrl);
            if (dosing) {
                var d = el('p', 'small');
                var a = el('a', null, text.dosingTool || '');
                a.setAttribute('href', dosing);
                a.setAttribute('target', '_blank');
                a.setAttribute('rel', 'noopener noreferrer');
                d.appendChild(icon('fa-calculator'));
                d.appendChild(document.createTextNode(' '));
                d.appendChild(a);
                box.appendChild(d);
            }
            var list = sources(result.citations);
            if (list) {
                box.appendChild(list);
            }
            box.appendChild(el('p', 'small text-muted', text.practiceOnly || ''));
            box.appendChild(againButton());
            return box;
        }

        function renderScenario(scenario) {
            area.textContent = '';
            citationsByRef = {};
            (scenario.citations || []).forEach(function (c) { citationsByRef[c.ref] = c; });
            if (scenario.outcome !== 'Ready') {
                message(scenario.message || text.error, 'text-warning');
                area.appendChild(againButton());
                return;
            }

            var heading = el('h6', null, text.scenario + (scenario.version ? ' · ' + scenario.version : ''));
            area.appendChild(heading);
            var body = el('p', null, scenario.scenario || '');
            body.style.whiteSpace = 'pre-wrap';
            area.appendChild(body);

            var answers = el('form', null);
            answers.setAttribute('novalidate', 'novalidate');
            var questionsByKey = {};
            (scenario.questions || []).forEach(function (q, index) {
                questionsByKey[q.key] = q;
                var group = el('div', 'form-group');
                var id = panel.id + '-answer-' + index;
                var label = el('label', null, q.key + '. ' + (q.text || ''));
                label.setAttribute('for', id);
                label.appendChild(refLinks(q.refs));
                group.appendChild(label);
                var key = el('input');
                key.type = 'hidden';
                key.name = 'keys';
                key.value = q.key;
                group.appendChild(key);
                var box = el('textarea', 'form-control');
                box.id = id;
                box.name = 'answers';
                box.rows = 2;
                box.maxLength = 1000;
                box.setAttribute('aria-label', text.answer + ' ' + q.key);
                group.appendChild(box);
                answers.appendChild(group);
            });
            var submit = el('button', 'btn btn-primary');
            submit.type = 'submit';
            submit.appendChild(icon('fa-check'));
            submit.appendChild(document.createTextNode(' ' + (text.submit || '')));
            answers.appendChild(submit);
            var result = el('div', null);
            result.setAttribute('aria-live', 'polite');
            area.appendChild(answers);
            area.appendChild(result);
            var list = sources(scenario.citations);
            if (list) {
                area.appendChild(list);
            }

            answers.addEventListener('submit', function (event) {
                event.preventDefault();
                var any = Array.prototype.some.call(answers.querySelectorAll('textarea'), function (t) { return t.value.trim().length > 0; });
                if (!any) {
                    result.textContent = '';
                    result.appendChild(el('p', 'text-danger', text.answerRequired));
                    return;
                }
                var data = new FormData(answers);
                data.append('exchangeId', scenario.exchangeId || '');
                if (token) {
                    data.append('__RequestVerificationToken', token.value);
                }
                submit.disabled = true;
                result.textContent = '';
                result.appendChild(el('p', 'text-muted', text.scoring));
                post(panel.getAttribute('data-score-url'), data)
                    .then(function (scored) {
                        result.textContent = '';
                        result.appendChild(renderScore(scored, questionsByKey));
                        if (scored.outcome !== 'Scored') {
                            submit.disabled = false;
                        }
                    })
                    .catch(function () {
                        result.textContent = '';
                        result.appendChild(el('p', 'text-danger', text.error));
                        submit.disabled = false;
                    });
            });
        }

        function start(event) {
            if (event) {
                event.preventDefault();
            }
            var button = form.querySelector('button[type="submit"]');
            if (button) {
                button.disabled = true;
            }
            message(text.starting);
            post(panel.getAttribute('data-start-url'), new FormData(form))
                .then(function (scenario) {
                    renderScenario(scenario || {});
                    if (button) {
                        button.disabled = false;
                    }
                })
                .catch(function () {
                    message(text.error, 'text-danger');
                    if (button) {
                        button.disabled = false;
                    }
                });
        }

        form.addEventListener('submit', start);
    }

    ready(function () {
        Array.prototype.forEach.call(document.querySelectorAll('.rgw-knowledge-practice'), init);
    });
})();
