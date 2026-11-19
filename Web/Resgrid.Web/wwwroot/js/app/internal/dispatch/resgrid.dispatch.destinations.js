
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var destinations;
        (function (destinations) {
            'use strict';

            // Suggested destinations under the call form's Destination field (Mapping -> Destination Rules). The server ranks
            // the department's destinations for the call type and priority from the incident location; with "suggest and fill"
            // (New Call only) an empty destination the dispatcher has not touched takes the best one, and follows later changes
            // of type, priority or location until the dispatcher picks or clears a destination by hand.
            var mode = null;          // null until the first answer; 0 = off (no more requests)
            var sequence = 0;
            var timer = null;
            var touched = false;      // the dispatcher picked or cleared a destination by hand
            var autoFilledId = null;  // the destination the script last filled in
            var applying = false;     // set while the script itself changes the select
            var shown = null;         // { key, data }: the answer on screen; null while a new lookup runs or after one failed
            var cached = null;        // { key, data, at }: the latest answer, reused for the same inputs while fresh
            var pendingKey = null;    // inputs of the request in flight
            var initialized = false;

            // Answers carry live availability (diversion, closed), so one is reused for the same inputs for a minute at most.
            var cacheMilliseconds = 60000;

            function strings() {
                return typeof destinationSuggestionStrings !== 'undefined' && destinationSuggestionStrings ? destinationSuggestionStrings : {};
            }

            function text(key, fallback) {
                var value = strings()[key];
                return value ? value : fallback;
            }

            function format(template) {
                var args = Array.prototype.slice.call(arguments, 1);
                return String(template).replace(/\{(\d+)\}/g, function (match, index) {
                    var value = args[Number(index)];
                    return value === undefined || value === null ? match : value;
                });
            }

            // Safe in text and in quoted attribute values (an availability note such as: Use "East" entrance).
            function escapeHtml(value) {
                var wrapper = document.createElement('div');
                wrapper.textContent = value === null || value === undefined ? '' : String(value);
                return wrapper.innerHTML.replace(/"/g, '&quot;').replace(/'/g, '&#39;');
            }

            function row() {
                return $('#destinationSuggestionsRow');
            }

            function select() {
                return $('#Call_DestinationPoiId');
            }

            function canFill() {
                return row().data('fill') === true || row().data('fill') === 'true';
            }

            function selectedId() {
                var value = select().val();
                return value ? String(value) : '';
            }

            function hasOption(poiId) {
                return select().find('option').filter(function () { return this.value === String(poiId); }).length > 0;
            }

            function setDestination(poiId) {
                applying = true;
                try {
                    select().val(poiId === null || poiId === undefined ? '' : String(poiId)).trigger('change');
                } finally {
                    applying = false;
                }
            }

            function minutesText(seconds) {
                return format(text('minutes', '{0} min'), Math.max(1, Math.round(seconds / 60)));
            }

            function kilometersText(meters) {
                return (meters / 1000).toFixed(meters < 10000 ? 1 : 0);
            }

            function metricsHtml(destination) {
                var parts = [];
                if (destination.travelSeconds >= 0) {
                    parts.push('<strong>' + escapeHtml(minutesText(destination.travelSeconds)) + '</strong>');
                }

                if (destination.travelDistanceMeters >= 0) {
                    parts.push(escapeHtml(format(text('kilometers', '{0} km'), kilometersText(destination.travelDistanceMeters))));
                } else if (destination.distanceMeters >= 0) {
                    parts.push(escapeHtml(format(text('straightLine', '{0} km straight line'), kilometersText(destination.distanceMeters))));
                }

                return parts.join(' &middot; ');
            }

            function statusHtml(destination) {
                if (!destination.statusText) {
                    return '';
                }

                var css = destination.status === 3 ? 'label-danger' : 'label-warning';
                var style = destination.status === 2 ? ' style="background-color: #e8590c;"' : '';
                var title = destination.availabilityNote ? ' title="' + escapeHtml(destination.availabilityNote) + '"' : '';
                return ' <span class="label ' + css + '"' + style + title + '>' + escapeHtml(destination.statusText) + '</span>';
            }

            function chipsHtml(destination) {
                var html = '';
                if (destination.missing && destination.missing.length) {
                    html += ' <span class="label label-danger">' + escapeHtml(format(text('missing', 'Missing: {0}'), destination.missing.join(', '))) + '</span>';
                }

                if (destination.down && destination.down.length) {
                    html += ' <span class="label label-danger">' + escapeHtml(format(text('down', 'Unavailable now: {0}'), destination.down.join(', '))) + '</span>';
                }

                (destination.preferred || []).forEach(function (name) {
                    html += ' <span class="label label-success">' + escapeHtml(name) + '</span>';
                });

                return html;
            }

            function requirementsHtml(data) {
                var parts = [];
                if (data.rules && data.rules.length) {
                    parts.push(escapeHtml(format(text('rules', 'Rules: {0}'), data.rules.join(', '))));
                }

                if (data.required && data.required.length) {
                    parts.push(escapeHtml(format(text('needs', 'Needs: {0}'), data.required.join(', '))));
                }

                if (data.preferred && data.preferred.length) {
                    parts.push(escapeHtml(format(text('prefers', 'Prefers: {0}'), data.preferred.join(', '))));
                }

                return parts.join(' &middot; ');
            }

            function render(data) {
                var list = data.destinations || [];
                var current = selectedId();
                var html = [];

                $('#destinationSuggestionsRequirements').html(requirementsHtml(data));

                if (!list.length) {
                    $('#destinationSuggestionsList').html('<p class="text-muted" style="margin: 0;">' + escapeHtml(text('none', 'No destination found.')) + '</p>');
                    return;
                }

                if (data.suggestedPoiId === null || data.suggestedPoiId === undefined) {
                    html.push('<p class="text-warning small" style="margin: 0 0 4px 0;">' + escapeHtml(text('noneMeet', 'No destination nearby offers everything required.')) + '</p>');
                }

                html.push('<table class="table table-condensed" style="margin-bottom: 0;"><tbody>');
                list.forEach(function (destination) {
                    var id = String(destination.poiId);
                    var isSuggested = data.suggestedPoiId !== null && data.suggestedPoiId !== undefined && String(data.suggestedPoiId) === id;
                    var detail = [destination.typeName, destination.address].filter(function (x) { return !!x; }).map(escapeHtml).join(' &middot; ');

                    html.push('<tr' + (isSuggested ? ' class="info"' : '') + ' data-poi-id="' + escapeHtml(id) + '">');
                    html.push('<td>' + escapeHtml(destination.name));
                    if (isSuggested) {
                        html.push(' <span class="label label-primary">' + escapeHtml(text('suggested', 'Suggested')) + '</span>');
                    }
                    html.push(statusHtml(destination) + chipsHtml(destination));
                    if (detail) {
                        html.push('<div class="text-muted small">' + detail + '</div>');
                    }
                    html.push('</td>');
                    html.push('<td style="white-space: nowrap; width: 1%;">' + metricsHtml(destination) + '</td>');
                    html.push('<td style="white-space: nowrap; width: 1%;">');
                    if (current === id) {
                        html.push('<button type="button" class="btn btn-xs btn-default" disabled="disabled">' + escapeHtml(text('inUse', 'Selected')) + '</button>');
                    } else if (hasOption(id)) {
                        html.push('<button type="button" class="btn btn-xs btn-primary destination-suggestion-use" data-poi-id="' + escapeHtml(id) + '">' + escapeHtml(text('use', 'Use')) + '</button>');
                    }
                    html.push('</td></tr>');
                });
                html.push('</tbody></table>');

                $('#destinationSuggestionsList').html(html.join(''));
            }

            function showNote(html, css) {
                var note = $('#destinationSuggestionsNote');
                if (!html) {
                    note.hide().empty();
                    return;
                }

                note.attr('class', 'small ' + (css || 'text-info')).html(html).show();
            }

            // "Suggest and fill": an empty destination, or the one the script filled in, follows the best suggestion until the
            // dispatcher picks or clears one by hand.
            function autoFill(data) {
                if (!canFill() || touched || data.mode !== 2) {
                    return;
                }

                var current = selectedId();
                if (current && current !== autoFilledId) {
                    return;
                }

                var suggested = data.suggestedPoiId === null || data.suggestedPoiId === undefined ? null : String(data.suggestedPoiId);
                if (suggested && hasOption(suggested)) {
                    if (current !== suggested) {
                        setDestination(suggested);
                    }
                    autoFilledId = suggested;
                    showNote(escapeHtml(text('filled', 'Filled in with the suggested destination.')), 'text-info');
                } else if (current && current === autoFilledId) {
                    // Nothing meets the new requirements: the earlier pick no longer fits, so it is taken back out.
                    setDestination(null);
                    autoFilledId = null;
                    showNote('', null);
                }
            }

            function hide() {
                shown = null;
                row().hide();
            }

            // The lookup inputs on the form now, or null when there is no usable incident location.
            function inputs() {
                var lat = parseFloat($('#Latitude').val());
                var lng = parseFloat($('#Longitude').val());
                if (isNaN(lat) || isNaN(lng) || (lat === 0 && lng === 0) || Math.abs(lat) > 90 || Math.abs(lng) > 180) {
                    return null;
                }

                var priority = $('#CallPriority').val();
                var type = $('#Call_Type').val();
                return { latitude: lat, longitude: lng, priority: priority, type: type, key: [lat, lng, priority, type].join('|') };
            }

            function currentKey() {
                var current = inputs();
                return current ? current.key : null;
            }

            function isFresh(entry, key) {
                return !!entry && entry.key === key && (Date.now() - entry.at) < cacheMilliseconds;
            }

            // The inputs moved away from the answer on screen: its Use buttons go, and a destination this script filled in for
            // the old inputs comes back out (an answer for the new ones fills it again). A manual choice is left alone.
            function invalidate() {
                shown = null;
                if (autoFilledId && !touched && selectedId() === autoFilledId) {
                    setDestination(null);
                }
                autoFilledId = null;
                showNote('', null);

                if (row().is(':visible')) {
                    $('#destinationSuggestionsRequirements').empty();
                    $('#destinationSuggestionsList').html('<p class="text-muted" style="margin: 0;">' + escapeHtml(text('loading', 'Finding destinations...')) + '</p>');
                }
            }

            function apply(key, data) {
                shown = { key: key, data: data };
                // Fill before rendering, so the row marked "Selected" is the destination now in the field.
                autoFill(data);
                render(data);
                row().show();
            }

            function showFailure() {
                shown = null;
                $('#destinationSuggestionsRequirements').empty();
                $('#destinationSuggestionsList').html('<p class="text-muted" style="margin: 0;">' + escapeHtml(text('failed', 'Destination suggestions are unavailable right now.')) +
                    ' <button type="button" class="btn btn-xs btn-default destination-suggestion-retry">' + escapeHtml(text('retry', 'Try again')) + '</button></p>');
                row().show();
            }

            function check() {
                if (!row().length || mode === 0) {
                    return;
                }

                var current = inputs();
                if (!current) {
                    // No location: a lookup in flight is for one no longer on the form, and must not reopen the panel.
                    sequence++;
                    pendingKey = null;
                    invalidate();
                    hide();
                    return;
                }

                // Every suggestion looks up drive times: the call form fires several change hooks for one edit (the map
                // marker, the location fields), so the same inputs are not asked for twice while the answer is fresh.
                var key = current.key;
                if (key === pendingKey || (shown && shown.key === key && isFresh(cached, key))) {
                    return;
                }

                if (isFresh(cached, key)) {
                    // Back to inputs answered a moment ago while another lookup was pending: that answer is shown again.
                    sequence++;
                    pendingKey = null;
                    apply(key, cached.data);
                    return;
                }

                if (!shown || shown.key !== key) {
                    invalidate();
                }

                var requestSequence = ++sequence;
                pendingKey = key;

                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/DestinationRules/Suggest',
                    type: 'GET',
                    dataType: 'json',
                    cache: false,
                    data: {
                        latitude: current.latitude,
                        longitude: current.longitude,
                        priority: current.priority,
                        type: current.type
                    }
                }).done(function (data) {
                    if (requestSequence !== sequence) {
                        return;
                    }

                    pendingKey = null;
                    if (data && data.success && data.mode === 0) {
                        mode = 0;
                        hide();
                        return;
                    }

                    if (data && data.success) {
                        mode = data.mode;
                        cached = { key: key, data: data, at: Date.now() };
                    }

                    // The form changed after this request went out (the next check asks for the new inputs).
                    if (currentKey() !== key) {
                        return;
                    }

                    if (!data || !data.success) {
                        hide();
                        return;
                    }

                    apply(key, data);
                }).fail(function () {
                    if (requestSequence !== sequence) {
                        return;
                    }

                    pendingKey = null;
                    if (currentKey() !== key) {
                        return;
                    }

                    // Shown even before a first answer, so a failure is not mistaken for suggestions being off; the
                    // dispatcher can retry, and the Destination picker stays usable either way.
                    showFailure();
                });
            }
            destinations.check = check;

            function schedule() {
                if (mode === 0) {
                    return;
                }

                if (timer) {
                    clearTimeout(timer);
                }
                timer = setTimeout(check, 400);
            }
            destinations.schedule = schedule;

            function init() {
                if (initialized || !row().length) {
                    return;
                }
                initialized = true;

                // A destination already chosen when the page opens (an edit, or a form shown again after a failed save) is the
                // dispatcher's: it is never replaced.
                if (selectedId()) {
                    touched = true;
                }

                select().on('change', function () {
                    if (applying) {
                        return;
                    }

                    touched = true;
                    autoFilledId = null;
                    showNote('', null);
                    // Only an answer for the inputs on the form is drawn again (never an older one's Use buttons mid-lookup).
                    if (shown && shown.key === currentKey()) {
                        render(shown.data);
                    }
                });

                $('#destinationSuggestionsList').on('click', '.destination-suggestion-use', function () {
                    if (!shown || shown.key !== currentKey()) {
                        return;
                    }

                    var poiId = String($(this).data('poi-id'));
                    setDestination(poiId);
                    touched = true;
                    autoFilledId = null;
                    showNote('', null);
                    render(shown.data);
                });

                $('#destinationSuggestionsList').on('click', '.destination-suggestion-retry', function () {
                    cached = null;
                    pendingKey = null;
                    check();
                });

                $('#CallPriority, #Call_Type, #Latitude, #Longitude').on('change', schedule);
                schedule();
            }
            destinations.init = init;

            $(document).ready(init);
        })(destinations = dispatch.destinations || (dispatch.destinations = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
