var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var nearestboard;
        (function (nearestboard) {
            // The nearest-unit board on New Call and Update Call: every unit and responder the dispatcher may send,
            // ranked by response time to the call (NearestUnitService), with distance, ETA, status, crew and position.
            // It fills the board tables (_NearestUnitsBoard), puts the distance and ETA into the dispatch lists below and
            // sorts them closest first, and draws the resources and the group boundaries on the call map. Its pick boxes
            // and the dispatch lists' checkboxes are one choice. Location changes come in bursts (a click, then a drag,
            // then a geocode), so requests are debounced and only the latest response is drawn.
            var options = {};
            var sequence = 0;
            var timer = null;
            var personnelById = {};
            var unitsById = {};
            var boundaryLayer = null;
            var resourceLayer = null;
            var legend = null;
            // The call location the map was last framed for and the lists last sorted for: a refreshed board must not
            // yank the view or the order the dispatcher has since changed.
            var framedFor = null;
            var sortedFor = null;
            // A view the dispatcher set by placing or dragging the call pin is theirs: no framing for that location.
            var keepViewFor = null;
            var NEAREST_BADGES = 3;
            // The board tables list the closest few of each; the dispatch lists below keep everyone, sorted by the board.
            var BOARD_ROWS = 10;
            // Only resources this close frame the call; one with a far-off fix would zoom the map out to a continent.
            var FRAME_MAX_METERS = 50000;
            var NOT_RANKED = 1000000;

            function text(key) {
                return (typeof nearestUnitStrings !== 'undefined' && nearestUnitStrings && nearestUnitStrings[key]) || key;
            }
            function escapeHtml(value) {
                return $('<span>').text(value === null || value === undefined ? '' : String(value)).html();
            }
            function prop(obj, name) {
                if (!obj) return undefined;
                if (obj[name] !== undefined) return obj[name];
                return obj[name.charAt(0).toUpperCase() + name.slice(1)];
            }
            // Enums arrive as numbers or names depending on the serializer; accept both.
            function isEnum(value, number, name) {
                return value === number || value === name;
            }
            function hexOr(color, fallback) {
                return color && /^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/.test(String(color).trim()) ? String(color).trim() : fallback;
            }
            function coloredText(value, color) {
                var span = $('<span>').text(value === null || value === undefined ? '' : String(value));
                if (hexOr(color, null)) {
                    span.css('color', hexOr(color, null));
                }
                return span.prop('outerHTML');
            }
            function hasDistance(entry) {
                var d = prop(entry, 'distanceMeters');
                return d !== null && d !== undefined && isFinite(d);
            }
            // 1-based rank among the available entries with a distance, in board order; 0 for the rest.
            function nearestRanks(entries) {
                var rank = 0;
                return entries.map(function (entry) { return prop(entry, 'isAvailable') && hasDistance(entry) ? ++rank : 0; });
            }
            function nearestBadge(rank) {
                return rank > 0 && rank <= NEAREST_BADGES
                    ? ' <span class="label label-success">' + escapeHtml(text('nearest').replace('{0}', rank)) + '</span>'
                    : '';
            }
            function formatEta(seconds, source) {
                if (seconds === null || seconds === undefined) {
                    return '&mdash;';
                }
                var html = escapeHtml(Math.max(1, Math.round(seconds / 60)) + ' ' + text('minutes'));
                if (isEnum(source, 1, 'Estimated')) {
                    html += ' <small class="text-muted">(' + escapeHtml(text('estimate')) + ')</small>';
                }
                return html;
            }
            function distanceEtaHtml(entry) {
                var eta = prop(entry, 'etaSeconds');
                var html = eta !== null && eta !== undefined ? formatEta(eta, prop(entry, 'etaSource')) : '';
                var distance = prop(entry, 'distanceMeters');
                if (distance !== null && distance !== undefined) {
                    html += (html ? '<br/>' : '') + '<small class="text-muted">' + escapeHtml((distance / 1000).toFixed(1) + ' km') + '</small>';
                }
                return html || '&mdash;';
            }
            function getMap() {
                return options.getMap ? options.getMap() : null;
            }
            function grid(kind) {
                var getter = kind === 'unit' ? options.getUnitsGrid : options.getPersonnelGrid;
                return getter ? getter() : null;
            }
            // The dispatch list checkbox for a unit or person; a DataTable row filtered out by a search is still found.
            function listBox(kind, id) {
                var selector = '#' + (kind === 'unit' ? 'dispatchUnit_' : 'dispatchUser_') + id;
                var table = grid(kind);
                return table ? table.$(selector) : $(selector);
            }

            // The board's pick boxes follow the lists after a run card recommendation ticks or unticks resources (those
            // set .prop without a change event).
            function syncPicks() {
                $('.board-pick-unit').each(function () { this.checked = listBox('unit', $(this).attr('data-unit-id')).is(':checked'); });
                $('.board-pick-user').each(function () { this.checked = listBox('user', $(this).attr('data-user-id')).is(':checked'); });
            }
            nearestboard.syncPicks = syncPicks;

            // A DataTables column render for the ETA cell of a dispatch list: the board's distance and ETA, sorting
            // closest first; before the board arrives, the list's own value.
            function etaRender(kind) {
                return function (data, type, row) {
                    var entry = kind === 'unit' ? unitsById[row.UnitId] : personnelById[row.UserId];
                    if (type === 'sort' || type === 'type') {
                        return entry ? entry.index : NOT_RANKED;
                    }
                    return entry ? entry.html : escapeHtml(data);
                };
            }
            nearestboard.etaRender = etaRender;

            function drawBoundaries() {
                var map = getMap();
                if (!map || boundaryLayer || typeof groupBoundaries === 'undefined' || !groupBoundaries || !groupBoundaries.length) {
                    return;
                }
                boundaryLayer = L.layerGroup().addTo(map);
                groupBoundaries.forEach(function (boundary) {
                    // Not interactive: a click inside a boundary still places the call.
                    L.polygon(boundary.points, { color: hexOr(boundary.color, '#0ea5e9'), weight: 2, opacity: 0.85, fillOpacity: 0.08, interactive: false }).addTo(boundaryLayer);
                });
                updateLegend(0, 0);
            }
            nearestboard.drawBoundaries = drawBoundaries;

            function updateLegend(unitCount, personCount) {
                var map = getMap();
                if (!map) {
                    return;
                }
                var hasBoundaries = typeof groupBoundaries !== 'undefined' && groupBoundaries && groupBoundaries.length > 0;
                if (!unitCount && !personCount && !hasBoundaries) {
                    return;
                }
                if (!legend) {
                    legend = L.control({ position: 'topleft' });
                    legend.onAdd = function () {
                        var div = L.DomUtil.create('div', 'dispatch-map-legend');
                        div.style.cssText = 'background: rgba(17,24,39,0.85); color: #f9fafb; padding: 6px 8px; border-radius: 4px; font-size: 11px; line-height: 16px;';
                        return div;
                    };
                    legend.addTo(map);
                }
                var lines = [];
                if (unitCount) {
                    lines.push('<span style="display:inline-block;width:11px;height:11px;border-radius:6px;border:2px solid #fff;background:#2563eb;vertical-align:middle;"></span> ' + escapeHtml(text('mapUnits')) + ' (' + unitCount + ')');
                }
                if (personCount) {
                    lines.push('<span style="display:inline-block;width:9px;height:9px;border-radius:5px;border:1.5px solid #fff;background:#7c3aed;vertical-align:middle;"></span> ' + escapeHtml(text('mapPersonnel')) + ' (' + personCount + ')');
                }
                if (hasBoundaries) {
                    lines.push('<span style="display:inline-block;width:11px;height:8px;border:2px solid #0ea5e9;vertical-align:middle;"></span> ' + escapeHtml(text('mapBoundaries')));
                }
                $(legend.getContainer()).html(lines.join('<br/>'));
            }

            // Units and personnel the board placed, around the call; hidden positions come without coordinates.
            function drawMarkers(board, units, personnel) {
                var map = getMap();
                if (!map) {
                    return;
                }
                if (!resourceLayer) {
                    resourceLayer = L.layerGroup().addTo(map);
                } else {
                    resourceLayer.clearLayers();
                }

                var closest = [];
                var unitCount = 0;
                var personCount = 0;
                function place(entry, isUnit, rank) {
                    var lat = prop(entry, 'latitude');
                    var lng = prop(entry, 'longitude');
                    if (lat === null || lat === undefined || lng === null || lng === undefined || (lat === 0 && lng === 0)) {
                        return;
                    }
                    var available = !!prop(entry, 'isAvailable');
                    var marker = L.circleMarker([lat, lng], {
                        radius: isUnit ? 8 : 6,
                        color: '#ffffff',
                        weight: isUnit ? 2.5 : 1.5,
                        fillColor: hexOr(prop(entry, 'statusColor'), isUnit ? '#2563eb' : '#7c3aed'),
                        fillOpacity: available ? 0.95 : 0.45,
                        opacity: available ? 1 : 0.6,
                        // A click on a resource opens its popup; it must not also move the call.
                        bubblingMouseEvents: false
                    });
                    var popup = '<strong>' + escapeHtml(prop(entry, 'name')) + '</strong>' + nearestBadge(rank)
                        + '<br/>' + escapeHtml(prop(entry, 'statusText') || '')
                        + '<br/>' + distanceEtaHtml(entry);
                    marker.bindPopup(popup).bindTooltip(escapeHtml(prop(entry, 'name')), { direction: 'top', offset: [0, -8] }).addTo(resourceLayer);
                    if (isUnit) { unitCount++; } else { personCount++; }
                    if (rank > 0 && rank <= NEAREST_BADGES && prop(entry, 'distanceMeters') <= FRAME_MAX_METERS) {
                        closest.push([lat, lng]);
                    }
                }

                var unitRanks = nearestRanks(units);
                units.forEach(function (unit, i) { place(unit, true, unitRanks[i]); });
                var personRanks = nearestRanks(personnel);
                personnel.forEach(function (person, i) { place(person, false, personRanks[i]); });
                updateLegend(unitCount, personCount);

                // Frame the call with the closest few, once per call location, keeping the call pin in the centre: each
                // resource is mirrored through the call so the bounds are centred on it. With none close enough the call
                // is still brought into view (typed coordinates move no map). A location the dispatcher set on the map
                // keeps their view.
                var lat = prop(board, 'latitude');
                var lng = prop(board, 'longitude');
                var key = locationKey(board);
                if (framedFor !== key && keepViewFor !== key) {
                    framedFor = key;
                    if (closest.length) {
                        var bounds = L.latLngBounds([[lat, lng]]);
                        closest.forEach(function (point) {
                            bounds.extend(point);
                            bounds.extend([2 * lat - point[0], 2 * lng - point[1]]);
                        });
                        map.fitBounds(bounds, { padding: [30, 30], maxZoom: 15 });
                    } else {
                        map.panTo([lat, lng]);
                    }
                }
            }
            function locationKey(board) {
                return coordinateKey(prop(board, 'latitude'), prop(board, 'longitude'));
            }
            function coordinateKey(lat, lng) {
                return Number(lat).toFixed(5) + ',' + Number(lng).toFixed(5);
            }

            // The board's distance, ETA and crew in the dispatch lists, closest first (the first board for a location).
            function applyToLists(board, units, personnel) {
                var resort = sortedFor !== locationKey(board);
                sortedFor = locationKey(board);

                var unitRanks = nearestRanks(units);
                var unitOrder = {};
                unitsById = {};
                units.forEach(function (unit, i) {
                    var id = prop(unit, 'unitId');
                    var html = distanceEtaHtml(unit) + nearestBadge(unitRanks[i]);
                    unitOrder[id] = i;
                    unitsById[id] = { index: i, html: html };
                    // New Call's per-station unit tabs are server-rendered rows with ETA and crew slots.
                    $('.unit-board-eta[data-unit-id="' + id + '"]').html(html);
                    $('.unit-board-crew[data-unit-id="' + id + '"]').text((prop(unit, 'crew') || []).join(', '));
                });
                if (resort) {
                    $('tbody[id^="unitGroups_"]').each(function () {
                        var body = $(this);
                        var rows = body.children('tr').get();
                        rows.sort(function (a, b) {
                            var ia = unitOrder[$(a).find('.unit-board-eta').attr('data-unit-id')];
                            var ib = unitOrder[$(b).find('.unit-board-eta').attr('data-unit-id')];
                            return (ia === undefined ? NOT_RANKED : ia) - (ib === undefined ? NOT_RANKED : ib);
                        });
                        body.append(rows);
                    });
                }

                var personRanks = nearestRanks(personnel);
                personnelById = {};
                personnel.forEach(function (person, i) {
                    personnelById[prop(person, 'userId')] = { index: i, html: distanceEtaHtml(person) + nearestBadge(personRanks[i]) };
                });

                [['user', options.personnelEtaColumn], ['unit', options.unitsEtaColumn]].forEach(function (pair) {
                    var table = grid(pair[0]);
                    if (!table || pair[1] === undefined || pair[1] === null) {
                        return;
                    }
                    // Only the ETA cells re-render: invalidating whole rows would redraw (and clear) the checkboxes.
                    table.cells(null, pair[1]).invalidate('data');
                    if (resort) {
                        table.order([pair[1], 'asc']);
                    }
                    table.draw(false);
                });
            }

            // "Showing the 10 closest of 240" under a table that was cut; nothing when everything fits.
            function moreNote(selector, shown, total) {
                $(selector).text(total > shown ? text('showingClosest').replace('{0}', shown).replace('{1}', total) : '').toggle(total > shown);
            }

            function renderPersonnel(personnel) {
                var ranks = nearestRanks(personnel);
                var rows = personnel.slice(0, BOARD_ROWS).map(function (person, i) {
                    var userId = prop(person, 'userId');
                    var pick = '<input type="checkbox" class="board-pick-user" data-user-id="' + escapeHtml(userId) + '" title="' + escapeHtml(text('pick')) + '"'
                        + (listBox('user', userId).is(':checked') ? ' checked' : '') + ' />';

                    var name = '<strong>' + escapeHtml(prop(person, 'name')) + '</strong>' + nearestBadge(ranks[i]);
                    if (prop(person, 'unitName')) {
                        name += '<br/><small class="text-muted">' + escapeHtml(text('onUnit').replace('{0}', prop(person, 'unitName'))) + '</small>';
                    }

                    var status = $('<span class="label">').addClass(prop(person, 'isAvailable') ? 'label-primary' : 'label-warning').text(prop(person, 'statusText') || '');
                    if (hexOr(prop(person, 'statusColor'), null)) {
                        status.css({ 'background-color': hexOr(prop(person, 'statusColor'), null), 'border': '1px solid rgba(0, 0, 0, 0.2)' });
                    }

                    var position;
                    if (prop(person, 'locationHidden')) {
                        position = escapeHtml(text('hidden'));
                    } else if (!hasDistance(person)) {
                        position = escapeHtml(text('noPosition'));
                    } else {
                        position = escapeHtml(text('live'));
                        if (prop(person, 'locationIsStale')) {
                            position += ' <span class="label label-warning">' + escapeHtml(text('stale')) + '</span>';
                        }
                    }

                    var place = [prop(person, 'groupName')].concat(prop(person, 'roles') || []).filter(function (x) { return !!x; });

                    return '<tr><td>' + pick + '</td><td>' + name + '</td><td>' + status.prop('outerHTML') + '</td><td>'
                        + coloredText(prop(person, 'staffingText'), prop(person, 'staffingColor')) + '</td><td>' + distanceEtaHtml(person)
                        + '</td><td>' + position + '</td><td><small>' + place.map(escapeHtml).join(' &middot; ') + '</small></td></tr>';
                });
                $('#nearestPersonnelTable tbody').html(rows.join(''));
                $('#nearestPersonnelSection').toggle(rows.length > 0);
                moreNote('#nearestPersonnelMore', rows.length, personnel.length);
            }

            function renderUnits(units) {
                var unitRanks = nearestRanks(units);
                var rows = units.slice(0, BOARD_ROWS).map(function (unit, unitIndex) {
                    var unitId = prop(unit, 'unitId');
                    var pick = '<input type="checkbox" class="board-pick-unit" data-unit-id="' + escapeHtml(unitId) + '" title="' + escapeHtml(text('pick')) + '"'
                        + (listBox('unit', unitId).is(':checked') ? ' checked' : '') + ' />';
                    var name = '<strong>' + escapeHtml(prop(unit, 'name')) + '</strong>' + nearestBadge(unitRanks[unitIndex]);
                    if (prop(unit, 'unitType')) {
                        name += ' <small class="text-muted">' + escapeHtml(prop(unit, 'unitType')) + '</small>';
                    }
                    if (prop(unit, 'incidentInGroupBoundary')) {
                        name += ' <span class="label label-info">' + escapeHtml(text('inBoundary')) + '</span>';
                    } else if (prop(unit, 'incidentInParentBoundary')) {
                        name += ' <span class="label label-default">' + escapeHtml(text('inParentBoundary')) + '</span>';
                    }
                    var place = [prop(unit, 'groupName'), prop(unit, 'parentGroupName')].filter(function (x) { return !!x; });
                    if (place.length) {
                        name += '<br/><small class="text-muted">' + place.map(escapeHtml).join(' &middot; ') + '</small>';
                    }

                    // The status's own colours, as everywhere else; the class colour stays as the fallback for a status
                    // that has none. Set through .css() so a colour never lands in the markup unchecked. The border keeps
                    // a white or very light status visible on the table's white rows.
                    var status = $('<span class="label">')
                        .addClass(prop(unit, 'isAvailable') ? 'label-primary' : 'label-warning')
                        .text(prop(unit, 'statusText') || '');
                    if (prop(unit, 'statusColor')) {
                        status.css({ 'background-color': prop(unit, 'statusColor'), 'border': '1px solid rgba(0, 0, 0, 0.2)' });
                    }
                    if (prop(unit, 'statusTextColor')) {
                        status.css('color', prop(unit, 'statusTextColor'));
                    }

                    // With a turnout configured the row shows the response time it is ranked on (turnout + travel).
                    var turnout = prop(unit, 'turnoutSeconds') || 0;
                    var response = prop(unit, 'responseSeconds');
                    var eta = formatEta(turnout > 0 && response !== null && response !== undefined ? response : prop(unit, 'etaSeconds'), prop(unit, 'etaSource'));
                    if (turnout > 0 && response !== null && response !== undefined) {
                        eta += '<br/><small class="text-muted">' + escapeHtml(text('includesTurnout').replace('{0}', turnout)) + '</small>';
                    }
                    var distance = prop(unit, 'distanceMeters');
                    if (distance !== null && distance !== undefined) {
                        eta += '<br/><small class="text-muted">' + escapeHtml((distance / 1000).toFixed(1) + ' km') + '</small>';
                    }

                    var source = prop(unit, 'positionSource');
                    var position = isEnum(source, 1, 'Live')
                        ? escapeHtml(text('live'))
                        : isEnum(source, 2, 'Station') ? escapeHtml(text('station')) : escapeHtml(text('noPosition'));
                    if (prop(unit, 'positionIsStale')) {
                        position += ' <span class="label label-warning">' + escapeHtml(text('stale')) + '</span>';
                    }
                    var latitude = prop(unit, 'latitude');
                    var longitude = prop(unit, 'longitude');
                    if (latitude !== null && latitude !== undefined && longitude !== null && longitude !== undefined) {
                        position += '<br/><small class="text-muted">' + Number(latitude).toFixed(4) + ', ' + Number(longitude).toFixed(4) + '</small>';
                    }

                    var crewSource = prop(unit, 'crewSource');
                    var shift;
                    if (isEnum(crewSource, 0, 'None')) {
                        shift = '<small class="text-muted">' + escapeHtml(text('noCrew')) + '</small>';
                    } else {
                        shift = escapeHtml((prop(unit, 'onShiftCount') || 0) + '/' + (prop(unit, 'crewCount') || 0) + ' ' + text('crewOnShift'));
                        if (isEnum(crewSource, 2, 'StationShift')) {
                            shift += ' <small class="text-muted">(' + escapeHtml(text('stationShift')) + ')</small>';
                        }
                        var shiftNames = prop(unit, 'shiftNames') || [];
                        if (shiftNames.length) {
                            shift += '<br/><small class="text-muted">' + shiftNames.map(escapeHtml).join(', ') + '</small>';
                        }
                        var crew = prop(unit, 'crew') || [];
                        if (crew.length) {
                            shift += '<br/><small class="text-muted">' + crew.map(escapeHtml).join(', ') + '</small>';
                        }
                    }

                    var roleMix = prop(unit, 'roleMix') || [];
                    var roles = roleMix.length
                        ? roleMix.map(function (r) { return escapeHtml(prop(r, 'name') + ' x' + prop(r, 'count')); }).join(', ')
                        : '&mdash;';

                    return '<tr><td>' + pick + '</td><td>' + name + '</td><td>' + status.prop('outerHTML') + '</td><td>' + eta + '</td><td>' + position + '</td><td>' + shift + '</td><td>' + roles + '</td></tr>';
                });
                $('#nearestUnitsTable tbody').html(rows.join(''));
                moreNote('#nearestUnitsMore', rows.length, units.length);
            }

            function render(board) {
                var boundaries = prop(board, 'containingBoundaries') || [];
                var header = boundaries.length
                    ? escapeHtml(text('incidentInside')) + ' <strong>' + boundaries.map(function (b) { return escapeHtml(prop(b, 'name')); }).join(', ') + '</strong>'
                    : escapeHtml(text('noBoundary'));
                if (prop(board, 'isDepartmentWide') === false) {
                    header += ' &middot; ' + escapeHtml(text('scoped'));
                }
                $('#nearestUnitsBoundaries').html(header);

                var units = prop(board, 'units') || [];
                var personnel = prop(board, 'personnel') || [];
                renderUnits(units);
                renderPersonnel(personnel);
                applyToLists(board, units, personnel);
                drawMarkers(board, units, personnel);

                var notes = prop(board, 'notes') || [];
                $('#nearestUnitsNotes').html(notes.map(escapeHtml).join('<br/>'));
            }
            nearestboard.render = render;

            // Loads the board for a location; without one, for the call's #Latitude/#Longitude.
            function check(lat, lng) {
                if (lat === undefined || lng === undefined) {
                    lat = $('#Latitude').val();
                    lng = $('#Longitude').val();
                }
                lat = parseFloat(lat);
                lng = parseFloat(lng);

                var row = $('#nearestUnitsRow');
                var requestSequence = ++sequence;

                if (isNaN(lat) || isNaN(lng) || (lat === 0 && lng === 0)) {
                    row.hide();
                    if (resourceLayer) {
                        resourceLayer.clearLayers();
                    }
                    return;
                }

                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetNearestUnits',
                    data: { latitude: lat, longitude: lng },
                    type: 'GET'
                }).done(function (response) {
                    if (requestSequence !== sequence) {
                        return;
                    }
                    var board = prop(response, 'board');
                    if (!response || !prop(response, 'success') || !board) {
                        row.hide();
                        return;
                    }
                    render(board);
                    row.show();
                }).fail(function () {
                    if (requestSequence === sequence) {
                        row.hide();
                    }
                });
            }
            nearestboard.check = check;

            function schedule(lat, lng) {
                if (timer) {
                    clearTimeout(timer);
                }
                timer = setTimeout(function () { check(lat, lng); }, 400);
            }
            nearestboard.schedule = schedule;

            // The dispatcher put the call here on the map (click or drag): the board must not move the map afterwards.
            function keepView(lat, lng) {
                keepViewFor = coordinateKey(lat, lng);
            }
            nearestboard.keepView = keepView;

            // options: getMap, getPersonnelGrid, getUnitsGrid (DataTables or null), personnelEtaColumn, unitsEtaColumn.
            function init(opts) {
                options = opts || {};

                // A pick on the board is a tick in the dispatch list (the change event lets the page's own handlers,
                // e.g. a run card recommendation the dispatcher overrides, see it), and the other way round.
                $(document).on('change', '.board-pick-unit', function () {
                    listBox('unit', $(this).attr('data-unit-id')).prop('checked', this.checked).trigger('change');
                });
                $(document).on('change', '.board-pick-user', function () {
                    listBox('user', $(this).attr('data-user-id')).prop('checked', this.checked).trigger('change');
                });
                $(document).on('change', 'input[id^="dispatchUnit_"]', function () {
                    $('.board-pick-unit[data-unit-id="' + this.id.substring('dispatchUnit_'.length) + '"]').prop('checked', this.checked);
                });
                $(document).on('change', 'input[id^="dispatchUser_"]', function () {
                    $('.board-pick-user[data-user-id="' + this.id.substring('dispatchUser_'.length) + '"]').prop('checked', this.checked);
                });
            }
            nearestboard.init = init;
        })(nearestboard = dispatch.nearestboard || (dispatch.nearestboard = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
