var resgrid;
(function (resgrid) {
    resgrid.absoluteBaseUrl = "";
    resgrid.absoluteApiBaseUrl = "";
    resgrid.absoluteEventingBaseUrl = "";

    function init() {
        $.validator.setDefaults({
            //errorElement: "span",
            errorClass: "has-error",
            highlight: function (element) {
                $(element).closest('.form-group').removeClass('has-success').addClass('has-error');
            },
            unhighlight: function (element) {
                $(element).closest('.form-group').removeClass('has-error').addClass('has-success');
            } //,
        });
    }
    resgrid.init = init;

    /**
     * Show or hide a loading overlay on the given jQuery selector element.
     * Replaces kendo.ui.progress($(sel), show).
     */
    function showProgress(selector, show) {
        var el = typeof selector === 'string' ? $(selector) : selector;
        if (show) {
            if (el.find('.rg-loading-overlay').length === 0) {
                el.css('position', 'relative').append(
                    '<div class="rg-loading-overlay" style="position:absolute;top:0;left:0;width:100%;height:100%;background:rgba(255,255,255,0.7);z-index:9999;display:flex;align-items:center;justify-content:center;">' +
                    '<div class="sk-spinner sk-spinner-wave"><div class="sk-rect1"></div><div class="sk-rect2"></div><div class="sk-rect3"></div><div class="sk-rect4"></div><div class="sk-rect5"></div></div>' +
                    '</div>'
                );
            }
        } else {
            el.find('.rg-loading-overlay').remove();
        }
    }
    resgrid.showProgress = showProgress;
})(resgrid || (resgrid = {}));
(function (resgrid) {
    var helpers;
    (function (helpers) {
        function GetURLParameter(sParam) {
            if (sParam = (new RegExp('[?&]' + encodeURIComponent(sParam) + '=([^&]*)')).exec(location.search))
                return decodeURIComponent(sParam[1]);
            return "";
        }
        helpers.GetURLParameter = GetURLParameter;
    })(helpers = resgrid.helpers || (resgrid.helpers = {}));
})(resgrid || (resgrid = {}));


