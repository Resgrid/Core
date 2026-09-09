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
    window.resgridAdpPageConcealed = function () {
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
                if (!response.ok || !Number.isInteger(result.id)) { showError(result.message); if (result.code === 'ProtectedDataRequired') form.dispatchEvent(new Event('adp:grant-required')); return; }
                var reopen = document.createElement('form'); reopen.method = 'POST'; reopen.action = config.reopen;
                hidden(reopen, 'destination', 'Detail'); hidden(reopen, 'id', String(result.id));
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
    var add = document.getElementById('work-order-add-step'), steps = document.getElementById('work-order-steps');
    if (add && steps) add.addEventListener('click', function () {
        var index = steps.children.length; if (index >= 100) return;
        var row = document.createElement('div'); row.className = 'form-group';
        var input = document.createElement('input'); input.name = 'Input.Content.Steps[' + index + '].Text'; input.className = 'form-control'; input.maxLength = 1000; input.required = true;
        row.appendChild(input); steps.appendChild(row); input.focus();
    });
}());
