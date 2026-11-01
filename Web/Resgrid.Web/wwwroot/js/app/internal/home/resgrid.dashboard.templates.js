// Dashboard templates list (home-dashboard-redesign-plan.md D7): delete buttons post with the anti-forgery header.
(function () {
    var script = document.currentScript;
    var confirmText = script ? script.getAttribute('data-confirm') : '';
    var deleteUrl = script ? script.getAttribute('data-delete-url') : '';

    function token() {
        var meta = document.querySelector('meta[name="request-verification-token"]');
        return meta ? meta.getAttribute('content') : '';
    }

    document.addEventListener('click', function (e) {
        var button = e.target.closest ? e.target.closest('[data-delete-template]') : null;
        if (!button || !deleteUrl)
            return;

        if (confirmText && !window.confirm(confirmText))
            return;

        button.disabled = true;
        fetch(deleteUrl, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
            body: JSON.stringify({ BoardId: button.getAttribute('data-delete-template') })
        }).then(function () {
            window.location.reload();
        }, function () {
            button.disabled = false;
        });
    });
})();
