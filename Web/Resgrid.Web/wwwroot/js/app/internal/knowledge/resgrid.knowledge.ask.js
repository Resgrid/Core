/*
 * K1 "Ask" panel (Areas/User/Views/Shared/_KnowledgeAsk.cshtml). Asks Knowledge/Status and shows the panel only when the department has
 * Ai.Knowledge; posts the question with the page's antiforgery token and renders the answer with text nodes only (never HTML from the
 * server): each part with links to the sources it cites, the sources with version and effective date (linking to the exact section),
 * conflicting sources labelled, notices (keyword-only retrieval, quoted doses, stale copies), and thumbs up/down.
 */
(function () {
    'use strict';
    if (window.rgKnowledgeAskReady) {
        return;
    }
    window.rgKnowledgeAskReady = true;

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
        // Only the app's own paths and https links (the Academy) are followed.
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

        var form = panel.querySelector('[data-role="ask-form"]');
        var result = panel.querySelector('[data-role="result"]');
        if (!form || !result) {
            return;
        }

        fetch(panel.getAttribute('data-status-url'), { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (response) { return response.ok ? response.json() : null; })
            .then(function (status) {
                if (!status || !status.available) {
                    return;
                }
                panel.hidden = false;
                var local = panel.querySelector('[data-role="kept-local"]');
                if (local && status.keptLocal) {
                    local.hidden = false;
                }
                var gaps = panel.querySelector('[data-role="gaps"]');
                var gapsUrl = safeUrl(status.gapsUrl);
                if (gaps && gapsUrl) {
                    gaps.querySelector('a').setAttribute('href', gapsUrl);
                    gaps.hidden = false;
                }
            })
            .catch(function () { /* The panel stays hidden. */ });

        function message(value, className) {
            result.textContent = '';
            result.appendChild(el('p', className || 'text-muted', value));
        }

        function sourceLink(citation) {
            var url = safeUrl(citation.url);
            var link = el(url ? 'a' : 'span', null, citation.title || '');
            if (url) {
                link.setAttribute('href', url);
            }
            return link;
        }

        function refLinks(refs, citationsByRef) {
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

        function feedback(answer) {
            if (!answer.exchangeId) {
                return null;
            }
            var bar = el('div', 'm-t-sm');
            var status = el('span', 'text-muted small m-l-sm');
            status.setAttribute('role', 'status');
            [[true, 'fa-thumbs-up', text.helpful], [false, 'fa-thumbs-down', text.notHelpful]].forEach(function (choice) {
                var button = el('button', 'btn btn-xs btn-default m-r-xs');
                button.type = 'button';
                button.appendChild(icon(choice[1]));
                button.appendChild(document.createTextNode(' ' + (choice[2] || '')));
                button.addEventListener('click', function () {
                    var data = new FormData();
                    data.append('exchangeId', answer.exchangeId);
                    data.append('helpful', choice[0] ? 'true' : 'false');
                    data.append('surface', panel.getAttribute('data-surface') || '0');
                    var token = form.querySelector('input[name="__RequestVerificationToken"]');
                    if (token) {
                        data.append('__RequestVerificationToken', token.value);
                    }
                    bar.querySelectorAll('button').forEach(function (b) { b.disabled = true; });
                    post(panel.getAttribute('data-feedback-url'), data)
                        .then(function (reply) { status.textContent = reply && reply.message ? reply.message : ''; })
                        .catch(function () { status.textContent = text.error || ''; });
                });
                bar.appendChild(button);
            });
            bar.appendChild(status);
            return bar;
        }

        function render(answer) {
            result.textContent = '';
            if (!answer) {
                message(text.error, 'text-danger');
                return;
            }

            var citationsByRef = {};
            (answer.citations || []).forEach(function (c) { citationsByRef[c.ref] = c; });

            if (answer.message) {
                var lead = el('p', answer.outcome === 'NotCovered' ? 'text-warning' : 'text-muted');
                lead.setAttribute('role', 'status');
                if (answer.outcome === 'NotCovered') {
                    lead.appendChild(icon('fa-info-circle'));
                    lead.appendChild(document.createTextNode(' '));
                }
                lead.appendChild(document.createTextNode(answer.message));
                result.appendChild(lead);
            }

            (answer.parts || []).forEach(function (part) {
                var p = el('p', null, part.text || '');
                p.style.whiteSpace = 'pre-wrap';
                p.appendChild(refLinks(part.refs, citationsByRef));
                result.appendChild(p);
            });

            (answer.conflicts || []).forEach(function (conflict) {
                var box = el('div', 'alert alert-warning');
                var strong = el('strong', null, text.conflict || '');
                box.appendChild(icon('fa-exchange'));
                box.appendChild(document.createTextNode(' '));
                box.appendChild(strong);
                box.appendChild(document.createTextNode(': ' + (conflict.note || '')));
                box.appendChild(refLinks(conflict.refs, citationsByRef));
                result.appendChild(box);
            });

            (answer.notices || []).forEach(function (notice) {
                var n = el('p', 'text-info small');
                n.appendChild(icon('fa-info-circle'));
                n.appendChild(document.createTextNode(' ' + notice));
                result.appendChild(n);
            });

            var dosing = safeUrl(answer.dosingToolUrl);
            if (dosing) {
                var d = el('p', 'small');
                var a = el('a', null, text.dosingTool || '');
                a.setAttribute('href', dosing);
                a.setAttribute('target', '_blank');
                a.setAttribute('rel', 'noopener noreferrer');
                d.appendChild(icon('fa-calculator'));
                d.appendChild(document.createTextNode(' '));
                d.appendChild(a);
                result.appendChild(d);
            }

            if ((answer.citations || []).length > 0) {
                result.appendChild(el('h6', 'm-t-sm', answer.outcome === 'NotCovered' ? text.nearest : text.sources));
                var list = el('ol', 'list-unstyled');
                answer.citations.forEach(function (c) {
                    var item = el('li', 'm-b-xs');
                    item.appendChild(el('span', 'label label-default', c.ref));
                    item.appendChild(document.createTextNode(' '));
                    item.appendChild(sourceLink(c));
                    var meta = [c.kind, c.section, c.version, c.effective].filter(function (x) { return x; }).join(' · ');
                    if (meta) {
                        item.appendChild(el('small', 'rgw-sub text-muted', meta));
                    }
                    if (c.excerpt) {
                        item.appendChild(el('small', 'rgw-sub text-muted', '“' + c.excerpt + '”'));
                    }
                    list.appendChild(item);
                });
                result.appendChild(list);
            }

            var bar = feedback(answer);
            if (bar) {
                result.appendChild(bar);
            }
        }

        form.addEventListener('submit', function (event) {
            event.preventDefault();
            var question = form.querySelector('textarea');
            if (!question || question.value.trim().length === 0) {
                message(text.questionRequired, 'text-danger');
                return;
            }
            var button = form.querySelector('button[type="submit"]');
            if (button) {
                button.disabled = true;
            }
            message(text.asking);
            post(panel.getAttribute('data-ask-url'), new FormData(form))
                .then(function (answer) {
                    render(answer);
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
        });
    }

    ready(function () {
        Array.prototype.forEach.call(document.querySelectorAll('.rgw-knowledge-ask'), init);
    });
})();
