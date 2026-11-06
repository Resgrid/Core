(function ($) {
    'use strict';

    $(function () {
        var fragment = window.location.hash || '';
        if (fragment.indexOf('#token=') === 0) {
            var token = '';
            try {
                token = decodeURIComponent(fragment.substring(7));
            } catch (e) {
                // A mangled link (an email client re-encoding it) is treated like any other invalid link.
                token = '';
            }
            window.history.replaceState(null, document.title, window.location.pathname + window.location.search);

            if (/^[A-Za-z0-9_-]{40,64}$/.test(token)) {
                var tokenInput = document.getElementById('recovery-fragment-token');
                var tokenForm = document.getElementById('recovery-fragment-form');
                if (tokenInput && tokenForm) {
                    // The server has not seen the token yet, so the page it served says the link is invalid. Say what is
                    // happening instead while the token is exchanged.
                    var invalid = document.getElementById('recovery-invalid');
                    var checking = document.getElementById('recovery-checking');
                    if (invalid && checking) {
                        invalid.hidden = true;
                        checking.hidden = false;
                    }

                    tokenInput.value = token;
                    tokenForm.submit();
                    return;
                }
            }
        }

        $('.pr-password').each(function () {
            var minimumLength = parseInt($(this).attr('data-min-length'), 10) || 8;
            $(this).passwordRequirements({
                numCharacters: minimumLength,
                useLowercase: true,
                useUppercase: true,
                useNumbers: true,
                useSpecial: false
            });
        });
    });
}(window.jQuery));
