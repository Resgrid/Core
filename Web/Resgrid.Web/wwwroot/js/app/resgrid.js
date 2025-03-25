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
