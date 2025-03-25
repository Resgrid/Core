var resgrid;
(function (resgrid) {
    var public;
    (function (public) {
        var mailer;
        (function (mailer) {
            function addPublicSubscriber(emailAddress) {
                $.post("/Public/SubscribeToNewsletter", {
                    emailAddress: emailAddress
                }, function (data) {
                    if (data) {
                        $('#newsletter_input').val('');
                        $("#newsletterResult").empty().html(data);
                    }
                });
                return false;
            }
            mailer.addPublicSubscriber = addPublicSubscriber;
        })(mailer = public.mailer || (public.mailer = {}));
    })(public = resgrid.public || (resgrid.public = {}));
})(resgrid || (resgrid = {}));
