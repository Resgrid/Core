(function () {
    'use strict';
    var configNode = document.getElementById('work-order-page');
    if (!configNode) return;
    var config = JSON.parse(configNode.textContent);
    function hidden(form, name, value) {
        var input = form.querySelector('input[name="' + name + '"]');
        if (!input) { input = document.createElement('input'); input.type = 'hidden'; input.name = name; form.appendChild(input); }
        input.value = value || '';
    }
    document.querySelectorAll('form.work-order-form').forEach(function (form) {
        if (config.protectedData && config.grant) {
            hidden(form, '__ResgridProtectedGrant', config.grant);
            hidden(form, '__ResgridProtectedGrantExpiresOn', config.expiry);
        }
    });
    config.grant = null; configNode.textContent = '';
    var concealed = false;
    window.resgridAdpPageConcealed = function () {
        concealed = true;
        document.querySelectorAll('.work-order-protected').forEach(function (node) { node.replaceChildren(); });
        window.location.replace(config.index);
    };
    function showError(message) {
        var error = document.querySelector('.work-order-error');
        if (error) { error.textContent = message || config.error; error.hidden = false; error.scrollIntoView({ block: 'nearest' }); }
    }
    $(function () {
    document.querySelectorAll('form.work-order-command').forEach(function (form) {
        if (config.protectedData && window.resgridAdpReveal) window.resgridAdpReveal.bindForm(form);
        form.addEventListener('submit', async function (event) {
            if (event.defaultPrevented) return;
            event.preventDefault();
            if (form.dataset.busy === 'true') return;
            form.dataset.busy = 'true';
            var buttons = Array.from(form.querySelectorAll('button[type="submit"]'));
            try {
                buttons.forEach(function (b) { b.disabled = true; });
                var response = await fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin', cache: 'no-store' });
                var result = await response.json();
                if (concealed || !form.isConnected) return;
                if (!response.ok || !Number.isInteger(result.id)) { showError(result.message); if (result.code === 'ProtectedDataRequired') form.dispatchEvent(new Event('adp:grant-required')); return; }
                var reopen = document.createElement('form'); reopen.method = 'POST'; reopen.action = config.reopen;
                hidden(reopen, 'destination', form.dataset.destination || 'Detail'); hidden(reopen, 'id', String(result.id));
                ['__RequestVerificationToken', '__ResgridProtectedGrant', '__ResgridProtectedGrantExpiresOn'].forEach(function (name) {
                    var field = form.querySelector('input[name="' + name + '"]'); if (field) hidden(reopen, name, field.value);
                });
                document.body.appendChild(reopen); reopen.submit();
            } catch (_) { showError(config.error); }
            finally { form.dataset.busy = 'false'; buttons.forEach(function (b) { b.disabled = false; }); }
        });
    });
    });
    document.querySelectorAll('a.work-order-evidence').forEach(function (link) {
        link.addEventListener('click', function (event) {
            if (config.protectedData) { event.preventDefault(); if (window.resgridAdpReveal) window.resgridAdpReveal.download(link.href); }
        });
    });
    document.querySelectorAll('.work-order-user, .work-order-role').forEach(function (select) {
        select.addEventListener('change', function () { if (select.value) select.form.querySelector(select.classList.contains('work-order-user') ? '.work-order-role' : '.work-order-user').value = ''; });
    });
    document.querySelectorAll('.maintenance-weekday').forEach(function (box) { box.addEventListener('change', function () { var mask = 0; document.querySelectorAll('.maintenance-weekday:checked').forEach(function (day) { mask |= 1 << Number(day.value); }); document.getElementById('maintenance-weekdays').value = String(mask); }); });
    document.querySelectorAll('.inventory-part-load').forEach(function (button) {
        button.addEventListener('click', async function () {
            var root = button.closest('.work-order-inventory'), form = root.closest('form');
            var kind = button.dataset.kind, select = root.querySelector('select[data-kind="' + kind + '"]');
            var item = root.querySelector('select[data-kind="item"]').value;
            if ((kind === 'lot' || kind === 'asset') && !item) return;
            var data = new FormData();
            ['__RequestVerificationToken', '__ResgridProtectedGrant', '__ResgridProtectedGrantExpiresOn'].forEach(function (name) {
                var field = form.querySelector('input[name="' + name + '"]'); if (field) data.set(name, field.value);
            });
            data.set('kind', kind); data.set('page', button.dataset.page || '0'); if (item) data.set('itemId', item);
            var headers = new Headers(); if (window.resgridAdpReveal) window.resgridAdpReveal.applyGrantHeader(headers);
            button.disabled = true;
            try {
                var response = await fetch(root.dataset.url, { method: 'POST', body: data, headers: headers, credentials: 'same-origin', cache: 'no-store' });
                var result = await response.json();
                if (concealed || !root.isConnected || ((kind === 'lot' || kind === 'asset') && root.querySelector('select[data-kind="item"]').value !== item)) return;
                if (!response.ok) { showError(result.message); if (result.code === 'ProtectedDataRequired') form.dispatchEvent(new Event('adp:grant-required')); return; }
                (result.items || result.Items || []).forEach(function (choice) {
                    var id = choice.id || choice.Id;
                    if (Array.from(select.options).some(function (option) { return option.value === id; })) return;
                    var option = document.createElement('option'); option.value = id; option.textContent = choice.name || choice.Name; select.appendChild(option);
                });
                var more = result.hasMore === true || result.HasMore === true;
                button.dataset.page = String(Number(button.dataset.page || '0') + 1); button.dataset.complete = more ? 'false' : 'true';
            } catch (_) { if (!concealed) showError(config.error); }
            finally { button.disabled = button.dataset.complete === 'true'; }
        });
    });
    document.querySelectorAll('.inventory-part-choice[data-kind="item"]').forEach(function (select) {
        select.addEventListener('change', function () {
            var root = select.closest('.work-order-inventory');
            ['asset', 'lot'].forEach(function (kind) {
                var child = root.querySelector('select[data-kind="' + kind + '"]'); while (child.options.length > 1) child.remove(1); child.value = '';
                var load = root.querySelector('button[data-kind="' + kind + '"]'); load.dataset.page = '0'; load.dataset.complete = 'false'; load.disabled = false;
            });
        });
    });
    var add = document.getElementById('work-order-add-step'), steps = document.getElementById('work-order-steps');
    if (add && steps) add.addEventListener('click', function () {
        var index = steps.children.length; if (index >= 100) return;
        var row = document.createElement('div'); row.className = 'form-group';
        var input = document.createElement('input'); input.name = (add.closest('form')?.dataset.stepPrefix || 'Input.Content.Steps') + '[' + index + '].Text'; input.className = 'form-control'; input.maxLength = 1000; input.required = true;
        row.appendChild(input); steps.appendChild(row); input.focus();
    });
}());
