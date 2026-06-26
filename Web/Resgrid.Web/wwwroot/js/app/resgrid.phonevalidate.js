// Shared live phone-number validation for entry forms.
// Any <input class="js-phone-validate"> is validated on blur against the server's ValidatePhoneNumber endpoint
// (read from data-validate-url, default /User/Home/ValidatePhoneNumber). Inline feedback (a sibling
// .js-phone-feedback element, or the first one within the surrounding .form-group) shows a warning and a
// one-click "Use this" button to apply the canonical E.164 form. An optional data-region-source attribute
// names the id of a country <select>/<input> whose value is sent as the region hint so a national-format
// number (e.g. "082446...") can be recognized and normalized.
(function () {
    var DEFAULT_URL = '/User/Home/ValidatePhoneNumber';

    function urlFor(input) {
        return input.getAttribute('data-validate-url') || DEFAULT_URL;
    }

    function regionFor(input) {
        // Explicit data-region-source wins; otherwise fall back to a physical-country dropdown if the form has one
        // (the profile, contact and personnel forms all use the id "PhysicalCountry"), so locals can be normalized.
        var id = input.getAttribute('data-region-source') || 'PhysicalCountry';
        var el = document.getElementById(id);
        return el ? (el.value || '') : '';
    }

    function feedbackFor(input) {
        // Prefer an adjacent feedback element (handles several phone fields in one .form-group)...
        var n = input.nextElementSibling;
        while (n) {
            if (n.classList && n.classList.contains('js-phone-feedback')) return n;
            n = n.nextElementSibling;
        }
        // ...otherwise fall back to the first one in the surrounding form group.
        var scope = (input.closest && input.closest('.form-group')) || input.parentNode;
        return scope ? scope.querySelector('.js-phone-feedback') : null;
    }

    function makeUseButton(input, value, feedback) {
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'btn btn-xs btn-default';
        btn.style.marginLeft = '6px';
        btn.textContent = 'Use this';
        btn.addEventListener('click', function () {
            input.value = value;
            feedback.innerHTML = '<span class="text-success">Updated to ' + value + '</span>';
        });
        return btn;
    }

    function render(feedback, input, data) {
        feedback.innerHTML = '';
        if (!data) return;
        if (data.isValid) {
            if (data.formatted && data.formatted !== (input.value || '').trim()) {
                var ok = document.createElement('div');
                ok.className = 'text-success';
                ok.innerHTML = 'Looks good — standard format: <strong>' + data.formatted + '</strong>';
                ok.appendChild(makeUseButton(input, data.formatted, feedback));
                feedback.appendChild(ok);
            }
            return;
        }
        var warn = document.createElement('div');
        warn.className = 'text-danger';
        warn.textContent = data.message || 'This number does not look valid.';
        feedback.appendChild(warn);
        if (data.formatted) {
            var sug = document.createElement('div');
            sug.className = 'text-warning';
            sug.innerHTML = 'Did you mean <strong>' + data.formatted + '</strong>?';
            sug.appendChild(makeUseButton(input, data.formatted, feedback));
            feedback.appendChild(sug);
        }
    }

    function validate(input) {
        var feedback = feedbackFor(input);
        if (!feedback) return;
        var num = (input.value || '').trim();
        if (!num) { feedback.innerHTML = ''; return; }
        fetch(urlFor(input), {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ number: num, country: regionFor(input) })
        }).then(function (r) { return r.ok ? r.json() : null; })
          .then(function (data) { render(feedback, input, data); })
          .catch(function () { /* network error: server-side save still validates */ });
    }

    function wire() {
        var inputs = document.querySelectorAll('.js-phone-validate');
        for (var i = 0; i < inputs.length; i++) {
            (function (input) {
                input.addEventListener('blur', function () { validate(input); });
            })(inputs[i]);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', wire);
    } else {
        wire();
    }
})();
