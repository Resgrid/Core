
var resgrid;
(function (resgrid) {
    var types;
    (function (types) {
        var aibasetype;
        (function (aibasetype) {
            // Enhanced AI base-type suggestions on the type and priority edit pages. Fetched after the page renders so it never
            // waits on the model; shown only when the server returns one (entitled department, confident answer). A hint only:
            // nothing changes until the administrator picks the class and saves.
            $(document).ready(function () {
                $('[data-ai-base-type-url]').each(function () {
                    var hint = $(this);
                    $.ajax({ url: hint.data('aiBaseTypeUrl'), type: 'GET', dataType: 'json', cache: false })
                        .done(function (result) {
                            if (!result || result.baseType === null || result.baseType === undefined)
                                return;
                            var name = $(hint.data('aiBaseTypeSelect')).find('option').filter(function () {
                                return String($(this).val()) === String(result.baseType);
                            }).first().text();
                            if (!name)
                                return;
                            hint.text(String(hint.data('aiBaseTypeFormat')).replace('{0}', name)).show();
                        });
                });
            });
        })(aibasetype = types.aibasetype || (types.aibasetype = {}));
    })(types = resgrid.types || (resgrid.types = {}));
})(resgrid || (resgrid = {}));
