var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var nearestboard;
        (function (nearestboard) {
            // The nearest-unit board on New Call and Update Call: every unit and responder the dispatcher may send,
            // ranked by response time to the call (NearestUnitService), with distance, ETA, status, crew and position.
            // The dispatch lists are the board: the personnel grid and the unit tabs (_DispatchUnitLists: "Units" with
            // every unit, then "Ungrouped Units" and one per station) show the board's ETA, position, crew and role mix on
            // each row and sort closest first, and a position opens a map of it and the call. The board also draws the
            // resources and the group boundaries on the call map. Location changes come in bursts (a click, then a drag,
            // then a geocode), so requests are debounced and only the latest response is drawn.
            var options = {};
            var sequence = 0;
            var timer = null;
            // The board last drawn, and its entries by user and unit id: { index (board order), rank (Nearest #n), person/unit }.
            var lastBoard = null;
            var personnelById = {};
            var unitsById = {};
            // The unit list rows by unit id, and one DataTable per unit tab.
            var unitRowsById = {};
            var unitGrids = [];
            var boundaryLayer = null;
            var resourceLayer = null;
            var legend = null;
            var positionModal = null;
            var positionMap = null;
            var positionLayer = null;
            var pendingPosition = null;
            // The call location the map was last framed for and the lists last sorted for: a refreshed board must not
            // yank the view or the order the dispatcher has since changed.
            var framedFor = null;
            var sortedFor = null;
            // A view the dispatcher set by placing or dragging the call pin is theirs: no framing for that location.
            var keepViewFor = null;
            var NEAREST_BADGES = 3;
            // Only resources this close frame the call; one with a far-off fix would zoom the map out to a continent.
            var FRAME_MAX_METERS = 50000;
            var NOT_RANKED = 1000000;
            // Columns of personnelColumns() and unitColumns(): the ETA column sorts the list, the others are filled by the board.
            var PERSONNEL_ETA_COLUMN = 4;
            var PERSONNEL_BOARD_COLUMNS = [1, 4, 5];
            var UNIT_ETA_COLUMN = 3;
            var UNIT_BOARD_COLUMNS = [1, 3, 4, 5, 6];

            function text(key) {
                return (typeof nearestUnitStrings !== 'undefined' && nearestUnitStrings && nearestUnitStrings[key]) || key;
            }
            function getText(key, fallback) {
                return (resgrid.dispatch && typeof resgrid.dispatch.getText === 'function')
                    ? resgrid.dispatch.getText(key, fallback)
                    : fallback;
            }
            function escapeHtml(value) {
                return $('<span>').text(value === null || value === undefined ? '' : String(value)).html();
            }
            // escapeHtml leaves quotes alone, so attribute values need this.
            function escapeAttr(value) {
                return escapeHtml(value).replace(/"/g, '&quot;');
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
            // A status in its own colours, as everywhere else; older built-in statuses carry a label class instead of a
            // colour. Set through .css() so a colour never lands in the markup unchecked. The border keeps a white or very
            // light status visible on the table's white rows.
            function statusLabel(value, color, textColor) {
                var label = $('<span class="label">').text(value === null || value === undefined ? '' : String(value));
                var background = $.trim(color || '');
                if (/^label-[a-z]+$/.test(background)) {
                    label.addClass(background);
                } else {
                    label.addClass('label-default');
                    if (background) {
                        label.css({ 'background-color': background, 'border': '1px solid rgba(0, 0, 0, 0.2)' });
                        if (textColor) {
                            label.css('color', textColor);
                        }
                    }
                }
                return label.prop('outerHTML');
            }
            function hasDistance(entry) {
                var d = prop(entry, 'distanceMeters');
                return d !== null && d !== undefined && isFinite(d);
            }
            // Hidden positions come without coordinates.
            function hasCoordinates(entry) {
                var lat = prop(entry, 'latitude');
                var lng = prop(entry, 'longitude');
                return lat !== null && lat !== undefined && lng !== null && lng !== undefined && !(lat === 0 && lng === 0);
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
            function distanceText(entry) {
                return (prop(entry, 'distanceMeters') / 1000).toFixed(1) + ' km';
            }
            function distanceEtaHtml(entry) {
                var eta = prop(entry, 'etaSeconds');
                var html = eta !== null && eta !== undefined ? formatEta(eta, prop(entry, 'etaSource')) : '';
                if (hasDistance(entry)) {
                    html += (html ? '<br/>' : '') + '<small class="text-muted">' + escapeHtml(distanceText(entry)) + '</small>';
                }
                return html || '&mdash;';
            }
            // A unit's ETA parts: with a turnout configured, the response time it is ranked on (turnout + travel).
            function unitEtaParts(unit) {
                var turnout = prop(unit, 'turnoutSeconds') || 0;
                var response = prop(unit, 'responseSeconds');
                var withTurnout = turnout > 0 && response !== null && response !== undefined;
                var parts = [formatEta(withTurnout ? response : prop(unit, 'etaSeconds'), prop(unit, 'etaSource'))];
                if (withTurnout) {
                    parts.push('<small class="text-muted">' + escapeHtml(text('includesTurnout').replace('{0}', turnout)) + '</small>');
                }
                if (hasDistance(unit)) {
                    parts.push('<small class="text-muted">' + escapeHtml(distanceText(unit)) + '</small>');
                }
                return parts;
            }
            function getMap() {
                return options.getMap ? options.getMap() : null;
            }
            function personnelGrid() {
                return options.getPersonnelGrid ? options.getPersonnelGrid() : null;
            }
            // A unit's dispatch box: one per unit in #dispatchUnitInputs, whatever tab it is ticked on.
            function unitBox(id) {
                return $('#dispatchUnit_' + id);
            }

            // ── Positions ──
            function positionLink(kind, id, label) {
                // Kept on one line: a link wrapped in a narrow column leaves a dead gap between its lines.
                return '<a href="#" class="dispatch-position-link" style="white-space: nowrap;" data-kind="' + kind + '" data-id="' + escapeAttr(id) + '" title="' + escapeAttr(text('showOnMap')) + '">'
                    + '<i class="fa fa-map-marker"></i> ' + escapeHtml(label) + '</a>';
            }
            function staleLabel(isStale) {
                return isStale ? ' <span class="label label-warning">' + escapeHtml(text('stale')) + '</span>' : '';
            }
            function unitPositionText(unit) {
                var source = prop(unit, 'positionSource');
                return isEnum(source, 1, 'Live') ? text('live') : isEnum(source, 2, 'Station') ? text('station') : text('noPosition');
            }
            function unitPositionHtml(unit) {
                if (!hasCoordinates(unit) && prop(unit, 'locationHidden')) {
                    return escapeHtml(text('hidden'));
                }
                if (!hasCoordinates(unit)) {
                    return escapeHtml(unitPositionText(unit)) + staleLabel(prop(unit, 'positionIsStale'));
                }
                return positionLink('unit', prop(unit, 'unitId'), unitPositionText(unit)) + staleLabel(prop(unit, 'positionIsStale'))
                    + '<br/><small class="text-muted">' + Number(prop(unit, 'latitude')).toFixed(4) + ', ' + Number(prop(unit, 'longitude')).toFixed(4) + '</small>';
            }
            function personPositionHtml(person) {
                if (prop(person, 'locationHidden')) {
                    return escapeHtml(text('hidden'));
                }
                if (!hasDistance(person)) {
                    return escapeHtml(text('noPosition'));
                }
                var html = hasCoordinates(person) ? positionLink('user', prop(person, 'userId'), text('live')) : escapeHtml(text('live'));
                return html + staleLabel(prop(person, 'locationIsStale'));
            }

            // ── Personnel grid ──
            // The page owns the personnel grid; these are its columns: the person's dispatch box, then the list's name,
            // status, staffing, group and roles, with the board's Nearest badge, unit, ETA and position.
            function personNameCell(data, type, row) {
                var entry = personnelById[row.UserId];
                var unitName = entry ? prop(entry.person, 'unitName') : null;
                if (type === 'filter') {
                    return [data, unitName].filter(function (x) { return !!x; }).join(' ');
                }
                if (type !== 'display') {
                    return data || '';
                }
                var html = '<strong>' + escapeHtml(data) + '</strong>' + nearestBadge(entry ? entry.rank : 0);
                if (unitName) {
                    html += '<br/><small class="text-muted">' + escapeHtml(text('onUnit').replace('{0}', unitName)) + '</small>';
                }
                return html;
            }
            function personEtaCell(data, type, row) {
                var entry = personnelById[row.UserId];
                if (type === 'sort' || type === 'type') {
                    return entry ? entry.index : NOT_RANKED;
                }
                return type === 'display' && entry ? distanceEtaHtml(entry.person) : '&mdash;';
            }
            function personPositionCell(data, type, row) {
                var entry = personnelById[row.UserId];
                return type === 'display' && entry ? personPositionHtml(entry.person) : '&mdash;';
            }
            function plainCell(data, type) {
                var value = $.isArray(data) ? data.join(', ') : (data === null || data === undefined ? '' : String(data));
                return type === 'display' ? escapeHtml(value) : value;
            }
            function personnelColumns() {
                return [
                    {
                        data: 'UserId', title: '<label><input type="checkbox" id="checkAllPersonnel"/></label>', orderable: false, searchable: false,
                        render: function (data) { return '<input type="checkbox" id="dispatchUser_' + escapeAttr(data) + '" name="dispatchUser_' + escapeAttr(data) + '" />'; }
                    },
                    { data: 'Name', title: getText('name', 'Name'), render: personNameCell },
                    {
                        data: 'Status', title: getText('status', 'Status'),
                        render: function (data, type, row) { return type === 'display' ? (data ? statusLabel(data, row.StatusColor) : '') : (data || ''); }
                    },
                    {
                        data: 'Staffing', title: text('staffingHeader'),
                        render: function (data, type, row) { return type === 'display' ? coloredText(data, row.StaffingColor) : (data || ''); }
                    },
                    { data: 'Eta', title: text('etaHeader'), searchable: false, render: personEtaCell },
                    { data: null, title: text('positionHeader'), orderable: false, searchable: false, render: personPositionCell },
                    { data: 'Group', title: getText('group', 'Group'), render: plainCell },
                    { data: 'Roles', title: getText('roles', 'Roles'), render: plainCell }
                ];
            }
            // DataTables options for the personnel grid at a list url: closest first once the board is in.
            function personnelTableOptions(url) {
                return {
                    ajax: { url: url, dataSrc: '' },
                    paging: false,
                    order: [[PERSONNEL_ETA_COLUMN, 'asc']],
                    columns: personnelColumns()
                };
            }
            nearestboard.personnelTableOptions = personnelTableOptions;

            // ── Unit lists ──
            function unitPickCell(data, type) {
                if (type !== 'display') {
                    return '';
                }
                return '<input type="checkbox" class="dispatch-unit-pick" data-unit-id="' + escapeAttr(data) + '" title="' + escapeAttr(text('pick')) + '"'
                    + (unitBox(data).is(':checked') ? ' checked' : '') + ' />';
            }
            function unitNameCell(data, type, row) {
                var entry = unitsById[row.UnitId];
                var unit = entry ? entry.unit : null;
                if (type === 'filter') {
                    return [data, row.Type, row.Station].concat(unit ? prop(unit, 'crew') || [] : []).filter(function (x) { return !!x; }).join(' ');
                }
                if (type !== 'display') {
                    return data || '';
                }
                var html = '';
                if (row.TypeIconUrl) {
                    html += '<img src="' + escapeAttr(row.TypeIconUrl) + '" alt="" aria-hidden="true" class="rg-type-icon" style="width:20px;height:20px;object-fit:contain;vertical-align:middle;margin-right:4px;" />';
                }
                html += '<strong>' + escapeHtml(data) + '</strong>' + nearestBadge(entry ? entry.rank : 0);
                if (row.Type) {
                    html += ' <small class="text-muted">' + escapeHtml(row.Type) + '</small>';
                }
                if (unit && prop(unit, 'incidentInGroupBoundary')) {
                    html += ' <span class="label label-info">' + escapeHtml(text('inBoundary')) + '</span>';
                } else if (unit && prop(unit, 'incidentInParentBoundary')) {
                    html += ' <span class="label label-default">' + escapeHtml(text('inParentBoundary')) + '</span>';
                }
                var place = (unit ? [prop(unit, 'groupName'), prop(unit, 'parentGroupName')] : [row.Station]).filter(function (x) { return !!x; });
                if (place.length) {
                    html += '<br/><small class="text-muted">' + place.map(escapeHtml).join(' &middot; ') + '</small>';
                }
                return html;
            }
            // The list's status (the department's names and colours); the board's for a unit the list has none for.
            function unitStatusCell(data, type, row) {
                var entry = unitsById[row.UnitId];
                var value = data || (entry ? prop(entry.unit, 'statusText') : '');
                if (type !== 'display') {
                    return value || '';
                }
                if (data) {
                    return statusLabel(data, row.StateColor, row.TextColor);
                }
                return value ? statusLabel(value, prop(entry.unit, 'statusColor'), prop(entry.unit, 'statusTextColor')) : '&mdash;';
            }
            function unitEtaCell(data, type, row) {
                var entry = unitsById[row.UnitId];
                if (type === 'sort' || type === 'type') {
                    return entry ? entry.index : NOT_RANKED;
                }
                return type === 'display' && entry ? unitEtaParts(entry.unit).join('<br/>') : '&mdash;';
            }
            function unitPositionCell(data, type, row) {
                var entry = unitsById[row.UnitId];
                return type === 'display' && entry ? unitPositionHtml(entry.unit) : '&mdash;';
            }
            function unitShiftCell(data, type, row) {
                var entry = unitsById[row.UnitId];
                if (type !== 'display' || !entry) {
                    return '&mdash;';
                }
                var unit = entry.unit;
                var crewSource = prop(unit, 'crewSource');
                if (isEnum(crewSource, 0, 'None')) {
                    return '<small class="text-muted">' + escapeHtml(text('noCrew')) + '</small>';
                }
                var shift = escapeHtml((prop(unit, 'onShiftCount') || 0) + '/' + (prop(unit, 'crewCount') || 0) + ' ' + text('crewOnShift'));
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
                return shift;
            }
            function unitRoleMixCell(data, type, row) {
                var entry = unitsById[row.UnitId];
                var roleMix = entry ? prop(entry.unit, 'roleMix') || [] : [];
                if (type !== 'display' || !roleMix.length) {
                    return '&mdash;';
                }
                return roleMix.map(function (r) { return escapeHtml(prop(r, 'name') + ' x' + prop(r, 'count')); }).join(', ');
            }
            function unitColumns() {
                return [
                    {
                        data: 'UnitId', title: '<input type="checkbox" class="dispatch-unit-check-all" title="' + escapeAttr(text('pick')) + '" />',
                        orderable: false, searchable: false, render: unitPickCell
                    },
                    { data: 'Name', title: text('unitHeader'), render: unitNameCell },
                    { data: 'State', title: text('statusHeader'), render: unitStatusCell },
                    { data: null, title: text('etaHeader'), searchable: false, render: unitEtaCell },
                    { data: null, title: text('positionHeader'), orderable: false, searchable: false, render: unitPositionCell },
                    { data: null, title: text('shiftHeader'), orderable: false, searchable: false, render: unitShiftCell },
                    { data: null, title: text('roleMixHeader'), orderable: false, searchable: false, render: unitRoleMixCell }
                ];
            }

            // The table boxes are copies of the units' dispatch boxes; DataTables keeps rows a search filtered out, so
            // those are set too.
            function syncGridPicks(grid) {
                $(grid.rows().nodes()).find('.dispatch-unit-pick').each(function () {
                    this.checked = unitBox($(this).attr('data-unit-id')).is(':checked');
                });
            }
            // The table boxes follow the dispatch boxes after a run card recommendation or the call's own dispatches tick or
            // untick units (those set .prop without a change event).
            function syncPicks() {
                unitGrids.forEach(syncGridPicks);
            }
            nearestboard.syncPicks = syncPicks;

            function addUnitGrid(table, units) {
                var grid = table.DataTable({
                    data: units,
                    paging: false,
                    order: [[UNIT_ETA_COLUMN, 'asc']],
                    columns: unitColumns()
                });
                grid.on('draw', function () { syncGridPicks(grid); });
                unitGrids.push(grid);
            }
            function addUnitTab(id, name, units) {
                var paneId = 'unitsTab' + id;
                $('#unitDispatchTabs').append($('<li role="presentation">').append(
                    $('<a role="tab" data-toggle="tab">').attr({ href: '#' + paneId, 'aria-controls': paneId }).text(name)));
                var table = $('<table class="table table-striped table-bordered table-hover table-condensed" width="100%">');
                $('#unitDispatchTabContent').append($('<div role="tabpanel" class="tab-pane">').attr('id', paneId).append(table));
                addUnitGrid(table, units);
            }
            // Every unit the dispatcher may see, once: a dispatch box each, the "Units" table with all of them, then
            // "Ungrouped Units" and a tab per station (by name) for the units there.
            function loadUnitLists() {
                var table = $('#unitsGrid');
                if (!table.length) {
                    return;
                }
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Units/GetUnitsForCallGrid',
                    type: 'GET'
                }).done(function (units) {
                    units = $.isArray(units) ? units : [];
                    var inputs = $('#dispatchUnitInputs');
                    var stations = {};
                    var ungrouped = [];
                    units.forEach(function (unit) {
                        unitRowsById[unit.UnitId] = unit;
                        inputs.append($('<input type="checkbox" />').attr({ id: 'dispatchUnit_' + unit.UnitId, name: 'dispatchUnit_' + unit.UnitId }));
                        if (unit.StationGroupId) {
                            (stations[unit.StationGroupId] = stations[unit.StationGroupId] || { id: unit.StationGroupId, name: unit.Station || '', units: [] }).units.push(unit);
                        } else {
                            ungrouped.push(unit);
                        }
                    });

                    addUnitGrid(table, units);
                    if (ungrouped.length) {
                        addUnitTab(0, text('ungroupedUnits'), ungrouped);
                    }
                    Object.keys(stations).map(function (key) { return stations[key]; })
                        .sort(function (a, b) { return a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' }); })
                        .forEach(function (station) { addUnitTab(station.id, station.name, station.units); });

                    if (options.onUnitsLoaded) {
                        options.onUnitsLoaded();
                    }
                }).fail(function () {
                    table.replaceWith($('<div class="alert alert-danger" style="margin: 8px 0 0 0;">').text(text('unitsLoadFailed')));
                    // The other lists still get the call's dispatches ticked; with no unit boxes posted, the save leaves the
                    // call's units as they are.
                    if (options.onUnitsLoaded) {
                        options.onUnitsLoaded();
                    }
                });
            }

            // ── Call map ──
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
                    // A refresh that leaves nobody available must not keep the last counts on an empty map.
                    if (legend) {
                        legend.remove();
                        legend = null;
                    }
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

            // Available units and personnel the board placed, around the call; hidden positions come without coordinates. The
            // map shows only who can be sent; the dispatch lists keep everyone, with their status, for reassigning someone busy.
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
                    if (!hasCoordinates(entry) || !prop(entry, 'isAvailable')) {
                        return;
                    }
                    var lat = prop(entry, 'latitude');
                    var lng = prop(entry, 'longitude');
                    var marker = L.circleMarker([lat, lng], {
                        radius: isUnit ? 8 : 6,
                        color: '#ffffff',
                        weight: isUnit ? 2.5 : 1.5,
                        fillColor: hexOr(prop(entry, 'statusColor'), isUnit ? '#2563eb' : '#7c3aed'),
                        fillOpacity: 0.95,
                        opacity: 1,
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

            // ── Position map ──
            function ensurePositionModal() {
                if (positionModal) {
                    return positionModal;
                }
                positionModal = $('<div class="modal fade" tabindex="-1" role="dialog" aria-hidden="true" id="dispatchPositionModal">'
                    + '<div class="modal-dialog modal-lg"><div class="modal-content">'
                    + '<div class="modal-header"><button type="button" class="close" data-dismiss="modal" aria-hidden="true">&times;</button><h4 class="modal-title"></h4></div>'
                    + '<div class="modal-body"><div class="dispatch-position-details" style="margin-bottom: 8px;"></div><div class="dispatch-position-map" style="height: 420px;"></div></div>'
                    + '<div class="modal-footer"><button type="button" class="btn btn-default" data-dismiss="modal"></button></div>'
                    + '</div></div></div>');
                positionModal.find('.modal-footer .btn').text(text('close'));
                // Leaflet measures its container, so the map is drawn once the modal is on screen.
                positionModal.on('shown.bs.modal', drawPositionMap);
                $('body').append(positionModal);
                return positionModal;
            }
            function positionDetailsHtml(kind, resource) {
                var parts = [];
                if (kind === 'unit') {
                    var row = unitRowsById[prop(resource, 'unitId')];
                    parts.push(row && row.State
                        ? statusLabel(row.State, row.StateColor, row.TextColor)
                        : statusLabel(prop(resource, 'statusText'), prop(resource, 'statusColor'), prop(resource, 'statusTextColor')));
                    parts = parts.concat(unitEtaParts(resource));
                    parts.push(escapeHtml(unitPositionText(resource)) + staleLabel(prop(resource, 'positionIsStale')));
                } else {
                    parts.push(statusLabel(prop(resource, 'statusText'), hexOr(prop(resource, 'statusColor'), null)));
                    parts.push(formatEta(prop(resource, 'etaSeconds'), prop(resource, 'etaSource')));
                    if (hasDistance(resource)) {
                        parts.push('<small class="text-muted">' + escapeHtml(distanceText(resource)) + '</small>');
                    }
                    parts.push(escapeHtml(text('live')) + staleLabel(prop(resource, 'locationIsStale')));
                }
                parts.push('<small class="text-muted">' + Number(prop(resource, 'latitude')).toFixed(5) + ', ' + Number(prop(resource, 'longitude')).toFixed(5) + '</small>');
                return parts.join(' &middot; ');
            }
            // A unit's or person's position and the call's (the location the board was worked out for) on one map.
            function showPosition(kind, id) {
                var entry = kind === 'unit' ? unitsById[id] : personnelById[id];
                var resource = entry ? (kind === 'unit' ? entry.unit : entry.person) : null;
                if (!resource || !lastBoard || !hasCoordinates(resource) || typeof L === 'undefined') {
                    return;
                }
                pendingPosition = { kind: kind, resource: resource, board: lastBoard };
                var modal = ensurePositionModal();
                modal.find('.modal-title').text(text('positionTitle').replace('{0}', prop(resource, 'name') || ''));
                modal.find('.dispatch-position-details').html(positionDetailsHtml(kind, resource));
                modal.modal('show');
            }
            nearestboard.showPosition = showPosition;
            function drawPositionMap() {
                if (!pendingPosition) {
                    return;
                }
                if (!positionMap) {
                    positionMap = L.map(positionModal.find('.dispatch-position-map')[0], { scrollWheelZoom: false });
                    if (typeof osmTileUrl !== 'undefined' && osmTileUrl) {
                        L.tileLayer(osmTileUrl, { maxZoom: 19, attribution: typeof osmTileAttribution !== 'undefined' ? osmTileAttribution : '' }).addTo(positionMap);
                    }
                    positionLayer = L.layerGroup().addTo(positionMap);
                }
                positionMap.invalidateSize();
                positionLayer.clearLayers();

                var isUnit = pendingPosition.kind === 'unit';
                var resource = pendingPosition.resource;
                var point = [prop(resource, 'latitude'), prop(resource, 'longitude')];
                var callPoint = [prop(pendingPosition.board, 'latitude'), prop(pendingPosition.board, 'longitude')];
                L.polyline([point, callPoint], { color: '#374151', weight: 3, opacity: 0.8, dashArray: '6 8', interactive: false }).addTo(positionLayer);
                L.marker(callPoint, { keyboard: false })
                    .bindTooltip(escapeHtml(text('callLocation')), { permanent: true, direction: 'top' })
                    .addTo(positionLayer);
                // A dark outline: a light status colour would otherwise vanish on light tiles.
                L.circleMarker(point, {
                    radius: isUnit ? 9 : 7,
                    color: '#111827',
                    weight: isUnit ? 3 : 2,
                    fillColor: hexOr(prop(resource, 'statusColor'), isUnit ? '#2563eb' : '#7c3aed'),
                    fillOpacity: 0.95
                }).bindTooltip(escapeHtml(prop(resource, 'name')), { permanent: true, direction: 'top', offset: [0, -8] }).addTo(positionLayer);
                positionMap.fitBounds(L.latLngBounds([point, callPoint]), { padding: [50, 50], maxZoom: 16, animate: false });
            }

            // ── Board ──
            function refreshGrid(grid, columns, etaColumn, resort) {
                if (!grid) {
                    return;
                }
                // Only the board's cells re-render: re-rendering a personnel row's dispatch box would clear its tick.
                grid.cells(null, columns).invalidate('data');
                if (resort) {
                    grid.order([etaColumn, 'asc']);
                }
                grid.draw(false);
            }
            // The board's entries into the lists, closest first (re-sorted for the first board at a location only).
            function applyToLists(board, units, personnel) {
                var key = board ? locationKey(board) : null;
                var resort = !!board && sortedFor !== key;
                sortedFor = key;

                var unitRanks = nearestRanks(units);
                unitsById = {};
                units.forEach(function (unit, i) {
                    unitsById[prop(unit, 'unitId')] = { index: i, rank: unitRanks[i], unit: unit };
                });
                var personRanks = nearestRanks(personnel);
                personnelById = {};
                personnel.forEach(function (person, i) {
                    personnelById[prop(person, 'userId')] = { index: i, rank: personRanks[i], person: person };
                });

                refreshGrid(personnelGrid(), PERSONNEL_BOARD_COLUMNS, PERSONNEL_ETA_COLUMN, resort);
                unitGrids.forEach(function (grid) { refreshGrid(grid, UNIT_BOARD_COLUMNS, UNIT_ETA_COLUMN, resort); });
            }

            function render(board) {
                lastBoard = board;
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
                    // No call location, so no distances: the lists drop the last location's ETAs and positions.
                    if (lastBoard) {
                        lastBoard = null;
                        applyToLists(null, [], []);
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

            // options: getMap, getPersonnelGrid (DataTables or null), onUnitsLoaded (called once the unit lists and their
            // dispatch boxes exist, so the page can tick the call's or a run card's units).
            function init(opts) {
                options = opts || {};

                // A tick in any unit table is a tick of the unit's dispatch box (the change event lets the page's own
                // handlers, e.g. a run card recommendation the dispatcher overrides, see it), and the other way round.
                $(document).on('change', '.dispatch-unit-pick', function () {
                    unitBox($(this).attr('data-unit-id')).prop('checked', this.checked).trigger('change');
                });
                $(document).on('change', 'input[id^="dispatchUnit_"]', function () {
                    $('.dispatch-unit-pick[data-unit-id="' + this.id.substring('dispatchUnit_'.length) + '"]').prop('checked', this.checked);
                });
                // A table's header box ticks or unticks the units that table shows (after its search).
                $(document).on('change', '.dispatch-unit-check-all', function () {
                    var checked = this.checked;
                    $(this).closest('table').DataTable().rows({ search: 'applied' }).data().each(function (row) {
                        var box = unitBox(row.UnitId);
                        if (box.prop('checked') !== checked) {
                            box.prop('checked', checked).trigger('change');
                        }
                    });
                });
                $(document).on('click', '.dispatch-position-link', function (evt) {
                    evt.preventDefault();
                    showPosition($(this).attr('data-kind'), $(this).attr('data-id'));
                });
                // A table drawn in a hidden tab has no column widths until it is shown.
                $(document).on('shown.bs.tab', '#unitDispatchTabs a[data-toggle="tab"]', function () {
                    $.fn.dataTable.tables({ visible: true, api: true }).columns.adjust();
                });

                loadUnitLists();
            }
            nearestboard.init = init;
        })(nearestboard = dispatch.nearestboard || (dispatch.nearestboard = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
