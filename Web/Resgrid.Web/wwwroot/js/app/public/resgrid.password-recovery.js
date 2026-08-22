(function ($) {
    'use strict';

    $(function () {
        var fragment = window.location.hash || '';
        if (fragment.indexOf('#token=') === 0) {
            var token = decodeURIComponent(fragment.substring(7));
            window.history.replaceState(null, document.title, window.location.pathname + window.location.search);

            if (/^[A-Za-z0-9_-]{40,64}$/.test(token)) {
                var tokenInput = document.getElementById('recovery-fragment-token');
                var tokenForm = document.getElementById('recovery-fragment-form');
                if (tokenInput && tokenForm) {
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
