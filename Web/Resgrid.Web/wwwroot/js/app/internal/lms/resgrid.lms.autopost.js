/*
 * Resgrid LTI launch (Training T5): posts the signed id_token form to the tool's registered redirect URI as soon as the page loads (the page
 * allows no inline script; without script the learner presses Continue).
 */
(function (document) {
    'use strict';
    var form = document.getElementById('lti-launch');
    if (form) {
        form.submit();
    }
})(document);
