var resgrid;
(function (resgrid) {
    var user;
    (function (user) {
        function showWizard() {
            $('#setupWizard').load(resgrid.absoluteBaseUrl + '/User/Department/SetupWizard');
        }
        user.showWizard = showWizard;
    })(user = resgrid.user || (resgrid.user = {}));
})(resgrid || (resgrid = {}));
(function (resgrid) {
    var main;
    (function (main) {
        $(document).ready(function () {
            $('#top-icons-area').load(resgrid.absoluteBaseUrl + '/User/Department/TopIconsArea');
            $('#top-upgrade-area').load(resgrid.absoluteBaseUrl + '/User/Department/UpgradeButton');
        });
    })(main = resgrid.main || (resgrid.main = {}));
})(resgrid || (resgrid = {}));
