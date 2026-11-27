// The shared signature panel (S0). "Sign" is a plain form post: the action requires a fresh step-up, and the step-up flow
// holds the form and replays it. "Sign with passkey" asks the server for passkey options whose challenge embeds the hash of
// the content being signed, runs the browser's passkey prompt, and posts the assertion with the same form to the passkey
// action, which checks the binding before it records the signature. Nothing here decides a level: the server does.
(function (window, $) {
    'use strict';

    function token() {
        var meta = document.querySelector('meta[name="request-verification-token"]');
        if (meta) {
            return meta.getAttribute('content');
        }
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function texts($panel) {
        var node = $panel.nextAll('script.rg-signature-text').first();
        try {
            return JSON.parse(node.text() || '{}');
        } catch (e) {
            return {};
        }
    }

    function showError($form, message) {
        var $error = $form.find('.rg-signature-error');
        $error.text(message || '');
        $error.prop('hidden', !message);
    }

    $(document).on('click', '.rg-signature-passkey', function () {
        var $button = $(this);
        var $form = $button.closest('form');
        var $panel = $button.closest('.rg-signature-panel');
        var text = texts($panel);
        var optionsUrl = $panel.data('passkey-options');

        if (!window.resgridPasskeys || !window.resgridPasskeys.isSupported()) {
            showError($form, text.unsupported);
            return;
        }

        showError($form, '');
        $button.prop('disabled', true);

        $.ajax({
            url: optionsUrl,
            type: 'POST',
            data: { purpose: $form.find('input[name="purpose"]').val(), id: $form.find('input[name="id"]').val() },
            headers: { 'RequestVerificationToken': token() }
        }).then(function (start) {
            if (!start || !start.success) {
                throw { outcome: (start && start.error) || 'failed' };
            }
            return window.resgridPasskeys.authenticate(start.options).then(function (credential) {
                return { requestId: start.requestId, credential: credential };
            });
        }).then(function (result) {
            $form.attr('action', $button.data('action'));
            $('<input type="hidden" name="passkeyRequestId" />').val(result.requestId).appendTo($form);
            $('<input type="hidden" name="passkeyCredential" />').val(JSON.stringify(result.credential)).appendTo($form);
            $form.trigger('submit');
        }, function (error) {
            $button.prop('disabled', false);
            var outcome = error && error.outcome;
            showError($form, outcome === 'cancelled' ? text.cancelled : outcome === 'not_supported' ? text.unsupported : text.failed);
        });
    });
})(window, jQuery);
