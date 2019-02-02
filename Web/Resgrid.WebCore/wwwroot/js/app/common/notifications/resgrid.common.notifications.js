var resgrid;
(function (resgrid) {
    var common;
    (function (common) {
        var notifications;
        (function (notifications) {
            var options = {
                closeButton: true,
                debug: false,
                progressBar: true,
                positionClass: "toast-top-right",
                onclick: null,
                showDuration: 400,
                hideDuration: 1000,
                timeOut: 7000,
                extendedTimeOut: 1000,
                showEasing: "swing",
                hideEasing: "linear",
                showMethod: "fadeIn",
                hideMethod: "fadeOut"
            };
            function showInformational(title, message) {
                toastr.info(message, title, options);
            }
            notifications.showInformational = showInformational;
            function showSuccess(title, message) {
                toastr.success(message, title, options);
            }
            notifications.showSuccess = showSuccess;
            function showWarning(title, message) {
                toastr.warning(message, title, options);
            }
            notifications.showWarning = showWarning;
            function showError(title, message) {
                toastr.error(message, title, options);
            }
            notifications.showError = showError;
        })(notifications = common.notifications || (common.notifications = {}));
    })(common = resgrid.common || (resgrid.common = {}));
})(resgrid || (resgrid = {}));
