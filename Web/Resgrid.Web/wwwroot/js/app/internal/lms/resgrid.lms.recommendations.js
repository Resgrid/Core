/*
 * K3 learning recommendations (Areas/User/Views/LmsRequirements/_RecommendationList.cshtml): "Explain with AI" posts the member's id with the
 * page's antiforgery token and writes each returned sentence under its recommendation with text nodes only (never HTML from the server).
 * The ranking itself is already on the page; the explanation is optional and nothing is saved.
 */
(function () {
    'use strict';
    if (window.rgLmsRecommendationsReady) {
        return;
    }
    window.rgLmsRecommendationsReady = true;

    function ready(fn) {
        if (document.readyState !== 'loading') {
            fn();
        } else {
            document.addEventListener('DOMContentLoaded', fn);
        }
    }

    function init(panel) {
        var form = panel.querySelector('[data-role="explain-form"]');
        if (!form) {
            return;
        }
        var text = {};
        try {
            text = JSON.parse(panel.getAttribute('data-text') || '{}');
        } catch (e) {
            text = {};
        }
        var status = form.querySelector('[data-role="explain-status"]');
        var button = form.querySelector('button[type="submit"]');

        form.addEventListener('submit', function (event) {
            event.preventDefault();
            if (button) {
                button.disabled = true;
            }
            if (status) {
                status.textContent = text.explaining || '';
            }
            fetch(panel.getAttribute('data-explain-url'), {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' },
                body: new FormData(form)
            }).then(function (response) {
                if (!response.ok) {
                    throw new Error('HTTP ' + response.status);
                }
                return response.json();
            }).then(function (reply) {
                var texts = (reply && reply.texts) || {};
                var shown = 0;
                Array.prototype.forEach.call(panel.querySelectorAll('[data-explanation]'), function (slot) {
                    var value = texts[slot.getAttribute('data-explanation')];
                    if (typeof value === 'string' && value.length > 0) {
                        slot.textContent = value;
                        slot.hidden = false;
                        shown++;
                    }
                });
                if (status) {
                    status.textContent = shown > 0 ? (text.notice || '') : ((reply && reply.message) || text.failed || '');
                }
                if (button && shown === 0) {
                    button.disabled = false;
                }
            }).catch(function () {
                if (status) {
                    status.textContent = text.failed || '';
                }
                if (button) {
                    button.disabled = false;
                }
            });
        });
    }

    ready(function () {
        Array.prototype.forEach.call(document.querySelectorAll('.rgw-lms-recommendations'), init);
    });
})();
