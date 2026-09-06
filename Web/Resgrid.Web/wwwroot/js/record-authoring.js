(function () {
    'use strict';
    var form = document.getElementById('record-edit-form');
    if (!form) return;
    var text = form.querySelector('[name="Details.Narrative"]');
    var host = document.getElementById('record-narrative-editor');
    var template = document.getElementById('record-narrative-initial');
    if (text && host && window.Quill) {
        host.hidden = false;
        var editor = new Quill(host, { theme: 'snow', formats: ['bold', 'italic', 'underline', 'header', 'list', 'blockquote'], modules: { toolbar: [['bold', 'italic', 'underline'], [{ header: [1, 2, 3, false] }], [{ list: 'ordered' }, { list: 'bullet' }], ['blockquote', 'clean']] } });
        if (template) editor.setContents(editor.clipboard.convert(template.innerHTML));
        else editor.setText(text.value);
        text.hidden = true;
        function collect() { text.value = editor.getText().trim() ? editor.root.innerHTML : ''; }
        editor.on('text-change', function (_, __, source) { collect(); if (source === 'user') form.dispatchEvent(new Event('input', { bubbles: true })); });
        form.addEventListener('rms:collect', collect); form.addEventListener('submit', collect);
    }
    var status = document.getElementById('record-autosave-status');
    var version = form.querySelector('[name="RowVersion"]');
    var url = form.dataset.autosaveUrl;
    var generation = 0, savedGeneration = 0, timer, pending = null, blocked = false, manual = false, leaving = false, needsGrant = false;
    // Protected Data (RMS plan 5.9.3): a save refused for a missing or expired grant is not a stop. The
    // text stays, autosave pauses, and the ADP reveal module bound to this form prompts for a fresh
    // verification in place; adp:grant-renewed resumes saving with the new grant in the form's field.
    var GRANT_CODES = { step_up_required: true, grant_expired: true, grant_revoked: true };
    function say(message) { if (status) status.textContent = message; }
    function schedule() { clearTimeout(timer); if (url && !blocked && !manual && !needsGrant) timer = setTimeout(save, 2000); }
    form.addEventListener('input', function (event) { if (event.target.type === 'file') return; generation++; say('Unsaved changes'); schedule(); });
    form.addEventListener('change', function (event) { if (event.target.type === 'file') { say('Selected attachments will upload when you choose Save Draft.'); return; } generation++; schedule(); });
    form.addEventListener('adp:grant-renewed', function () { if (!needsGrant) return; needsGrant = false; say(generation === savedGeneration ? 'Verification renewed.' : 'Unsaved changes'); schedule(); });
    form.addEventListener('adp:submit-cancelled', function () { leaving = false; manual = false; schedule(); });
    async function grantRefusal(response) {
        if (response.status !== 403) return false;
        try { var payload = await response.clone().json(); return !!(payload && GRANT_CODES[payload.error]); } catch (_) { return false; }
    }
    async function save() {
        if (pending || blocked || manual || needsGrant || generation === savedGeneration) return pending;
        form.dispatchEvent(new Event('rms:collect'));
        var sentGeneration = generation, data = new FormData(form);
        Array.from(data.keys()).forEach(function (key) { if (data.get(key) instanceof File) data.delete(key); });
        data.delete('FinalizeAfterSave'); data.delete('Attested');
        say('Saving draft…');
        pending = (async function () {
            try {
                var response = await fetch(url, { method: 'POST', body: data, credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
                if (await grantRefusal(response)) { needsGrant = true; say('Verification needed. Automatic saving is paused; your text stays here until you re-verify.'); form.dispatchEvent(new CustomEvent('adp:grant-required')); return; }
                if (response.redirected || response.status === 401 || response.status === 403 || response.status === 404) { blocked = true; say('Automatic saving stopped. Your session or access changed. Keep your text and reload before continuing.'); return; }
                var result = await response.json();
                if (!response.ok) { blocked = response.status === 409; say(result.error || 'Draft could not be saved. Correct the form and try again.'); return; }
                if (!Number.isSafeInteger(result.rowVersion) || result.rowVersion <= Number(version.value)) throw new Error('Invalid version response');
                version.value = String(result.rowVersion); savedGeneration = sentGeneration;
                say(generation === savedGeneration ? 'Draft saved. Attachments require Save Draft.' : 'Unsaved changes');
            } catch (_) { blocked = true; say('Save could not be confirmed. Your text remains here. Reload to check the saved draft before continuing.'); }
            finally { pending = null; if (!manual && generation !== savedGeneration && !blocked && !needsGrant && savedGeneration === sentGeneration) schedule(); }
        })();
        return pending;
    }
    form.addEventListener('submit', function (event) {
        if (leaving) return;
        if (blocked) { event.preventDefault(); say('Saving stopped. Reload the draft to resolve the conflict or confirm the previous save before continuing.'); return; }
        clearTimeout(timer); manual = true;
        if (pending) {
            event.preventDefault(); var submitter = event.submitter;
            pending.then(function () { if (!blocked) { leaving = true; form.requestSubmit(submitter); } else manual = false; });
        } else leaving = true;
    });
    window.addEventListener('beforeunload', function (event) {
        if (!leaving && (generation !== savedGeneration || pending || Array.from(form.querySelectorAll('input[type=file]')).some(function (input) { return input.files.length; }))) { event.preventDefault(); event.returnValue = ''; }
    });
})();
