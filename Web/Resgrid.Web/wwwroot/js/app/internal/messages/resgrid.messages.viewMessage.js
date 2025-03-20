
var resgrid;
(function (resgrid) {
    var message;
    (function (message) {
        var viewMessage;
        (function (viewMessage) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Message - ViewMessage');
            });
            function respond(recipientId, response) {
                resgrid.common.analytics.track('Message - Respond');
                var note = $("#note").val();
                if (note) {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Messages/MessageResponse?recipientId=' + recipientId + "&response=" + response + "&note=" + encodeURIComponent(note),
                        type: 'GET'
                    }).done(function (results) {
                        window.location.assign(resgrid.absoluteBaseUrl + '/User/Messages/Inbox');
                    });
                }
                else {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Messages/MessageResponse?recipientId=' + recipientId + "&response=" + response,
                        type: 'GET'
                    }).done(function (results) {
                        window.location.assign(resgrid.absoluteBaseUrl + '/User/Messages/Inbox');
                    });
                }
            }
            viewMessage.respond = respond;
        })(viewMessage = message.viewMessage || (message.viewMessage = {}));
    })(message = resgrid.message || (resgrid.message = {}));
})(resgrid || (resgrid = {}));
