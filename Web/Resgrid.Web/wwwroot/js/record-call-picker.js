(function () {
    'use strict';
    document.querySelectorAll('[data-call-picker]').forEach(function (picker) {
        var value = picker.querySelector('[data-call-value]');
        var browser = picker.querySelector('[data-call-browser]');
        if (!browser) return;
        var term = picker.querySelector('[data-call-term]');
        var status = picker.querySelector('[data-call-status]');
        var from = picker.querySelector('[data-call-from]');
        var to = picker.querySelector('[data-call-to]');
        var results = picker.querySelector('[data-call-results]');
        var message = picker.querySelector('[data-call-message]');
        var previous = picker.querySelector('[data-call-prev]');
        var next = picker.querySelector('[data-call-next]');
        var offset = 0, history = [], nextOffset = null, generation = 0, pending, timer, loaded = false;

        function url(params) {
            var target = new URL(picker.dataset.url, window.location.href);
            Object.keys(params).forEach(function (key) { target.searchParams.set(key, params[key]); });
            return target;
        }
        function select(item) {
            while (value.options.length > 1) value.remove(1);
            value.add(new Option(item.text, String(item.id), true, true));
            value.dispatchEvent(new Event('change', { bubbles: true }));
            browser.open = false;
            value.focus();
        }
        function invalidate() {
            generation++;
            if (pending) pending.abort();
            clearTimeout(timer);
            results.replaceChildren();
            next.disabled = previous.disabled = true;
        }
        async function search() {
            invalidate();
            var request = generation;
            if (!from.checkValidity() || !to.checkValidity() || (from.value && to.value && from.value > to.value)) {
                message.textContent = picker.dataset.dates;
                return;
            }
            pending = new AbortController();
            message.textContent = picker.dataset.loading;
            results.setAttribute('aria-busy', 'true');
            try {
                var response = await fetch(url({ term: term.value, status: status.value, from: from.value, to: to.value, offset: offset }),
                    { signal: pending.signal, credentials: 'same-origin', cache: 'no-store' });
                if (!response.ok) throw new Error('Call lookup failed');
                var data = await response.json();
                if (request !== generation) return;
                data.items.forEach(function (item) {
                    var button = document.createElement('button');
                    button.type = 'button';
                    button.className = 'list-group-item';
                    button.style.width = '100%';
                    button.style.textAlign = 'left';
                    button.style.overflowWrap = 'anywhere';
                    button.textContent = item.text;
                    button.addEventListener('click', function () { select(item); });
                    results.appendChild(button);
                });
                nextOffset = data.nextOffset;
                next.disabled = nextOffset == null;
                previous.disabled = history.length === 0;
                message.textContent = (data.items.length ? '' : picker.dataset.empty) + (data.metadataOnly ? ' ' + picker.dataset.metadata : '');
                loaded = true;
            } catch (error) {
                if (request === generation && error.name !== 'AbortError') {
                    message.textContent = picker.dataset.error;
                    previous.disabled = history.length === 0;
                }
            } finally {
                if (request === generation) results.removeAttribute('aria-busy');
            }
        }
        function reset(delay) {
            invalidate();
            offset = 0;
            history = [];
            message.textContent = picker.dataset.loading;
            timer = setTimeout(search, delay);
        }
        picker.querySelector('[data-call-filters]').addEventListener('input', function (event) {
            event.stopPropagation(); // Searching is not an edit to the record and must not autosave it.
            reset(event.target === term ? 300 : 0);
        });
        picker.querySelector('[data-call-filters]').addEventListener('change', function (event) { event.stopPropagation(); reset(0); });
        picker.querySelector('[data-call-filters]').addEventListener('keydown', function (event) {
            if (event.key === 'Enter') { event.preventDefault(); reset(0); }
        });
        picker.querySelector('[data-call-search]').addEventListener('click', function () { reset(0); });
        previous.addEventListener('click', function () { offset = history.pop() || 0; search(); });
        next.addEventListener('click', function () { if (nextOffset != null) { history.push(offset); offset = nextOffset; search(); } });
        browser.addEventListener('toggle', function () { if (browser.open && !loaded) search(); });

        // Resolve a preselected/deep-linked or reposted call without loading the department's call history.
        var initial = value.value;
        if (initial) {
            fetch(url({ selectedId: initial }), { credentials: 'same-origin', cache: 'no-store' })
                .then(function (response) { if (!response.ok) throw new Error('Unavailable call'); return response.json(); })
                .then(function (data) { if (value.value === initial && data.selected) value.selectedOptions[0].textContent = data.selected.text; })
                .catch(function () { /* Preserve the binding when source access was revoked. */ });
        }
    });
}());
