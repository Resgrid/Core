var resgrid;
(function (resgrid) {
    var protocols;
    (function (protocols) {
        var translation;
        (function (translation) {
            // The translation form (Protocols P3): rich wording is edited in place and copied into its hidden field on submit. Without
            // script the hidden fields keep the stored wording, so nothing is lost. The server sanitizes and re-aligns everything.
            $(document).ready(function () {
                $('#translationForm').on('submit', function () {
                    $(this).find('input[data-rich-target]').each(function () {
                        var target = document.getElementById($(this).attr('data-rich-target'));
                        if (target) {
                            $(this).val(target.innerHTML);
                        }
                    });
                    return true;
                });
            });
        })(translation = protocols.translation || (protocols.translation = {}));
    })(protocols = resgrid.protocols || (resgrid.protocols = {}));
})(resgrid || (resgrid = {}));
