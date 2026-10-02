// Continues a return from the department's identity provider (passkey plan section 7.7.2; workbook section 12). The browser
// arrives on Account/SsoReturn from the provider, a cross-site navigation that carries none of this site's SameSite=Strict
// cookies, so that page reads and changes nothing. This script posts its code and state on from this site's own page: a
// same-site request, which carries them. Without script, the page's button does the same.
(function () {
    'use strict';

    function go() {
        var form = document.getElementById('ssoReturnContinue');
        if (!form || form.getAttribute('data-sent') === '1')
            return;

        form.setAttribute('data-sent', '1');
        form.submit();
    }

    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', go);
    else
        go();
})();
