
var resgrid;
(function (resgrid) {
    var mapping;
    (function (mapping) {
        var destinationrules;
        (function (destinationrules) {
            'use strict';

            function strings() {
                return typeof destinationRuleStrings !== 'undefined' && destinationRuleStrings ? destinationRuleStrings : {};
            }

            function text(key, fallback) {
                var value = strings()[key];
                return value ? value : fallback;
            }

            function escapeHtml(value) {
                var wrapper = document.createElement('div');
                wrapper.textContent = value === null || value === undefined ? '' : String(value);
                return wrapper.innerHTML;
            }

            function list(values, fallback) {
                if (!values || !values.length) {
                    return escapeHtml(fallback);
                }

                return values.map(escapeHtml).join(', ');
            }

            function render(data) {
                var html = [];
                var applied = data.applied || [];
                var setAside = data.setAside || [];

                if (!applied.length && !setAside.length) {
                    html.push('<p style="margin: 0;">' + escapeHtml(text('noRulesApply', 'No rule applies.')) + '</p>');
                } else {
                    html.push('<p><strong>' + escapeHtml(text('appliedRules', 'Rules that apply')) + ':</strong> ' + list(applied, '-') + '</p>');

                    if (setAside.length) {
                        html.push('<p><strong>' + escapeHtml(text('setAsideRules', 'Set aside')) + ':</strong> ' +
                            setAside.map(function (x) { return escapeHtml(x.name) + ' (' + escapeHtml(x.reason) + ')'; }).join(', ') + '</p>');
                    }

                    html.push('<p><strong>' + escapeHtml(text('destination', 'Destination')) + ':</strong> ' + list(data.types, text('anyDestinationType', 'Any destination type')) + '</p>');
                    html.push('<p><strong>' + escapeHtml(text('requires', 'Requires')) + ':</strong> ' + list(data.required, text('nothing', 'nothing')) + '</p>');
                    html.push('<p style="margin: 0;"><strong>' + escapeHtml(text('prefers', 'Prefers')) + ':</strong> ' + list(data.preferred, text('nothing', 'nothing')) + '</p>');
                }

                $('#destinationRulesTryItResult').html(html.join('')).show();
            }

            function tryIt() {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/DestinationRules/Preview',
                    type: 'GET',
                    dataType: 'json',
                    cache: false,
                    data: {
                        callTypeId: $('#tryCallType').val() || null,
                        priority: $('#tryPriority').val(),
                        unitTypeId: $('#tryUnitType').val() || null
                    }
                }).done(function (data) {
                    if (data && data.success) {
                        render(data);
                    }
                });
            }

            $(document).ready(function () {
                $('#tryDestinationRules').on('click', tryIt);
            });

            destinationrules.tryIt = tryIt;
        })(destinationrules = mapping.destinationrules || (mapping.destinationrules = {}));
    })(mapping = resgrid.mapping || (resgrid.mapping = {}));
})(resgrid || (resgrid = {}));
