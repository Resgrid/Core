
var resgrid;
(function (resgrid) {
    var training;
    (function (training) {
        var quiz;
        (function (quiz) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Training - Quiz');
            });
        })(quiz = training.quiz || (training.quiz = {}));
    })(training = resgrid.training || (resgrid.training = {}));
})(resgrid || (resgrid = {}));
