
var resgrid;
(function (resgrid) {
    var training;
    (function (training) {
        var index;
        (function (index) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Training - Index');
            });
        })(index = training.index || (training.index = {}));
    })(training = resgrid.training || (resgrid.training = {}));
})(resgrid || (resgrid = {}));
