(function () {
    'use strict';
    const settings = JSON.parse(document.getElementById('inventory-settings').textContent);
    const message = document.getElementById('inventory-message');
    function hidden(form, name, value) {
        let field = form.querySelector('input[name="' + name + '"]');
        if (!field) { field = document.createElement('input'); field.type = 'hidden'; field.name = name; form.appendChild(field); }
        field.value = value || '';
    }
    document.querySelectorAll('form.inventory-navigation, form.inventory-command').forEach(form => {
        if (form.method.toLowerCase() !== 'post') return;
        if (settings.grant) { hidden(form, '__ResgridProtectedGrant', settings.grant); hidden(form, '__ResgridProtectedGrantExpiresOn', settings.expiry); }
    });
    settings.grant = null; document.getElementById('inventory-settings').textContent = '';
    $(function () {
    document.querySelectorAll('form.inventory-navigation, form.inventory-command').forEach(form => {
        if (form.method.toLowerCase() === 'post' && settings.protectedData && window.resgridAdpReveal) window.resgridAdpReveal.bindForm(form);
    });
    document.querySelectorAll('form.inventory-command').forEach(form => form.addEventListener('submit', async event => {
        if (event.defaultPrevented) return;
        event.preventDefault(); if (form.dataset.sending) return; form.dataset.sending = 'true';
        try {
            if (form.classList.contains('inventory-kit-issue')) {
                const assets = Array.from(form.querySelectorAll('select[name$=".AssetId"]')).map(field => field.value);
                if (assets.some(value => !value) || new Set(assets).size !== assets.length) throw new Error(settings.failed);
            }
            const response = await fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin', cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            const result = await response.json();
            if (!response.ok) throw new Error(result.message || settings.failed);
            const pendingAsset = form.dataset.inventoryCreateAsset === 'true' && !(result.currentLocationId || result.CurrentLocationId);
            if (result.awaitingWitness || result.AwaitingWitness || pendingAsset) { message.textContent = settings.witness + ' ' + form.querySelector('[name="RequestId"]').value; message.hidden = false; return; }
            const page = document.getElementById('inventory-page');
            ['__ResgridProtectedGrant', '__ResgridProtectedGrantExpiresOn'].forEach(name => { const field = form.querySelector('[name="' + name + '"]'); if (field) hidden(page, name, field.value); });
            page.submit();
        } catch (error) { message.textContent = error.message || settings.failed; message.hidden = false; }
        finally { delete form.dataset.sending; }
    }));
    });
    let fieldSequence = 0;
    function refreshKitLines(lines) {
        Array.from(lines.children).forEach((row, index) => {
            row.querySelectorAll('[name]').forEach(field => { field.name = field.name.replace(/^Lines\[\d+\]/, 'Lines[' + index + ']'); });
            row.querySelector('.inventory-remove-line').disabled = lines.children.length === 1;
        });
    }
    document.querySelectorAll('form.inventory-kit').forEach(form => {
        const lines = form.querySelector('.inventory-kit-lines');
        refreshKitLines(lines);
        form.addEventListener('click', event => {
            const add = event.target.closest('.inventory-add-line'), remove = event.target.closest('.inventory-remove-line');
            if (add && lines.children.length < 100) {
                const row = lines.firstElementChild.cloneNode(true);
                row.querySelectorAll('[name]').forEach(field => {
                    const label = row.querySelector('label[for="' + field.id + '"]');
                    field.id = 'inventory-added-' + (++fieldSequence);
                    if (label) label.htmlFor = field.id;
                    field.value = field.type === 'number' ? '1' : '';
                });
                lines.appendChild(row);
            }
            if (remove && lines.children.length > 1) remove.closest('.inventory-kit-line').remove();
            refreshKitLines(lines);
        });
    });
    window.resgridAdpPageRevealed = () => { if (window.resgridAdpReveal) window.resgridAdpReveal.submitWithGrant(document.getElementById('inventory-page')); };
    window.resgridAdpPageConcealed = () => { document.querySelector('.wrapper-content').replaceChildren(); window.location.replace(settings.index); };
})();
