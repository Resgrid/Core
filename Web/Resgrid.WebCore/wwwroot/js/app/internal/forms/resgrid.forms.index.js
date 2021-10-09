
var resgrid;
(function (resgrid) {
	var forms;
    (function (forms) {
		var index;
		(function (index) {
			$(document).ready(function () {
				resgrid.common.analytics.track('Forms - Index');
			});
        })(index = forms.index || (forms.index = {}));
    })(forms = resgrid.forms || (resgrid.forms = {}));
})(resgrid || (resgrid = {}));
