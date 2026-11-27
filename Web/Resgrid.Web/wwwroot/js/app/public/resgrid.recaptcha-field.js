/*
 * The reCAPTCHA answer into the form field the server validates (GoogleReCaptchaModelBase.GoogleReCaptchaResponse): the Account/Register
 * standard copies the widget's g-recaptcha-response into the hidden GoogleReCaptchaResponse input when the form is sent. The learner portal
 * has no jQuery, so this is the same step in plain ES5 (it runs in the capture phase, before resgrid.submit-once.js).
 */
(function () {
    'use strict';

    document.addEventListener('submit', function (event) {
        var form = event.target;
        if (!form || !form.querySelector) {
            return;
        }
        var field = form.querySelector('input[name="GoogleReCaptchaResponse"]');
        if (!field) {
            return;
        }
        var answer = form.querySelector('textarea[name="g-recaptcha-response"]') || document.getElementById('g-recaptcha-response');
        if (answer) {
            field.value = answer.value;
        }
    }, true);
})();
