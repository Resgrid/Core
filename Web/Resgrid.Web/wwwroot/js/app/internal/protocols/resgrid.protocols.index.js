
var resgrid;
(function (resgrid) {
	var protocols;
    (function (protocols) {
		var index;
		(function (index) {
			$(document).ready(function () {
				resgrid.common.analytics.track('Protocols - Index');
			});
        })(index = protocols.index || (protocols.index = {}));
    })(protocols = resgrid.protocols || (resgrid.protocols = {}));
})(resgrid || (resgrid = {}));
