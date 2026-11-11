
var resgrid;
(function (resgrid) {
    var mapping;
    (function (mapping) {
        var viewType;
        (function (viewType) {
            'use strict';
            var map;
            var markers = [];

            function renderTextValue(data, type) {
                if (data === null || data === undefined) {
                    return '';
                }

                if (type === 'display' || type === 'filter') {
                    var safeWrapper = document.createElement('div');
                    safeWrapper.textContent = String(data);
                    return safeWrapper.innerHTML;
                }

                return data;
            }

            function createSafeTextElement(text) {
                var safeElement = document.createElement('span');
                safeElement.textContent = text === null || text === undefined ? '' : String(text);
                return safeElement;
            }

            $(document).ready(function () {
                resgrid.common.analytics.track('Mapping - View Type');

                var bulkForm = canManagePois ? document.getElementById('poiBulkDeleteForm') : null;
                var columns = [
                        { data: 'Name', title: 'Name', defaultContent: '', render: renderTextValue },
                        { data: 'Address', title: 'Address', defaultContent: '', render: renderTextValue },
                        { data: 'Note', title: 'Note', defaultContent: '', render: renderTextValue },
                        { data: 'Latitude', title: 'Latitude' },
                        { data: 'Longitude', title: 'Longitude' },
                        {
                            data: 'PoiId',
                            title: 'Actions',
                            orderable: false,
                            searchable: false,
                            render: function (data) {
                                if (!canManagePois) {
                                    return '';
                                }

                                return '<a class="btn btn-xs btn-info" href="' + resgrid.absoluteBaseUrl + '/User/Mapping/EditPOI?poiId=' + data + '">Edit</a> ' +
                                    '<button type="button" class="btn btn-xs btn-danger" onclick="resgrid.mapping.viewType.deletePoi(' + data + ')">Delete</button>';
                            }
                        }
                    ];

                if (bulkForm) {
                    columns.unshift({
                        data: 'PoiId',
                        title: '<input type="checkbox" id="poiSelectAll" aria-label="' + resgrid.escapeHtml(poiBulkStrings.selectAll) + '" title="' + resgrid.escapeHtml(poiBulkStrings.selectAll) + '" />',
                        orderable: false,
                        searchable: false,
                        width: '20px',
                        render: function (data, type, row) {
                            if (type !== 'display') {
                                return data;
                            }
                            var id = Number(data);
                            // Each box names its row, so moving between boxes says which POI would be removed.
                            var label = poiBulkStrings.selectRow + ': ' + (row && row.Name ? String(row.Name) : '') + ' (' + id + ')';
                            return '<input type="checkbox" class="poi-select" value="' + id + '"' + (bulkSelected[id] ? ' checked' : '') +
                                ' aria-label="' + resgrid.escapeHtml(label) + '" />';
                        }
                    });
                }

                var poisTable = $("#poisGrid").DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Mapping/GetPoisForType?poiTypeId=' + poiTypeId,
                        dataSrc: ''
                    },
                    pageLength: 50,
                    order: bulkForm ? [[1, 'asc']] : [[0, 'asc']],
                    columns: columns
                });

                if (bulkForm) {
                    initBulkRemoval(poisTable, bulkForm);
                }
                if (typeof poiBulkShowList !== 'undefined' && poiBulkShowList) {
                    $('a[href="#tab-3"]').tab('show');
                }

                resgrid.maps.create('map', { center: [parseFloat(centerLat) || 39.7392, parseFloat(centerLon) || -104.9903], zoom: 9 }).then(function (created) {
                    map = created;
                    initMap();
                });
            });

            function createMarkerOptions(poi) {
                var iconClass = resgrid.escapeHtml(poi.ImagePath || 'map-icon-map-pin');
                var color = resgrid.escapeHtml(poi.Color || '#3388ff');
                return {
                    html: '<span class="map-icon ' + iconClass + '" style="color:' + color + '; font-size: 24px;"></span>',
                    iconSize: [24, 24],
                    iconAnchor: [12, 24],
                    title: poi.Title || '',
                    popupHtml: poi.InfoWindowContent ? resgrid.escapeHtml(poi.InfoWindowContent) : null
                };
            }

            function initMap() {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Mapping/GetTypesMapData?poiTypeId=' + poiTypeId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    if (data && data.Pois && map) {
                        var positions = [];
                        data.Pois.forEach(function (poi) {
                            var position = [Number(poi.Latitude), Number(poi.Longitude)];
                            markers.push(map.addMarker(position, createMarkerOptions(poi)));
                            positions.push(position);
                        });

                        if (positions.length > 0) {
                            map.fitBounds(positions, { padding: 20 });
                        }
                    }
                });
            }
            viewType.initMap = initMap;

            function boolToCheckbox(boolean) {
                if (boolean === true)
                    return "on";
                else
                    return "off";
            }
            viewType.boolToCheckbox = boolToCheckbox;

            function checkboxToBool(checkbox) {
                if (checkbox === "on")
                    return true;
                else
                    return false;
            }
            viewType.checkboxToBool = checkboxToBool;

            // Bulk removal: ticked ids live here, not in the DOM, so a selection survives paging and searching.
            var bulkSelected = {};

            function bulkSelectedIds() {
                var ids = [];
                for (var key in bulkSelected) {
                    if (Object.prototype.hasOwnProperty.call(bulkSelected, key) && bulkSelected[key]) {
                        ids.push(Number(key));
                    }
                }
                return ids;
            }

            function initBulkRemoval(table, form) {
                var selectedOption = form.querySelector('input[name="scope"][value="Selected"]');
                var allOption = form.querySelector('input[name="scope"][value="All"]');
                var selectedLabel = document.getElementById('poiBulkSelectedCount');
                var allLabel = document.getElementById('poiBulkAllCount');

                function matchingIds() {
                    return table.rows({ search: 'applied' }).data().toArray().map(function (row) { return Number(row.PoiId); });
                }

                function refresh() {
                    var selectedCount = bulkSelectedIds().length;
                    selectedLabel.textContent = selectedCount.toLocaleString();
                    allLabel.textContent = table.rows().count().toLocaleString();
                    var header = document.getElementById('poiSelectAll');
                    if (header) {
                        var matching = matchingIds();
                        var ticked = matching.filter(function (id) { return bulkSelected[id]; }).length;
                        header.checked = matching.length > 0 && ticked === matching.length;
                        header.indeterminate = ticked > 0 && ticked < matching.length;
                    }
                }

                // The header box ticks (or clears) every row matching the search, on every page.
                $(table.table().header()).on('change', '#poiSelectAll', function () {
                    var check = this.checked;
                    matchingIds().forEach(function (id) {
                        if (check) { bulkSelected[id] = true; } else { delete bulkSelected[id]; }
                    });
                    if (check) { selectedOption.checked = true; }
                    table.rows({ page: 'current' }).invalidate('data').draw(false);
                    refresh();
                });

                $(table.table().body()).on('change', 'input.poi-select', function () {
                    var id = Number(this.value);
                    if (this.checked) { bulkSelected[id] = true; selectedOption.checked = true; } else { delete bulkSelected[id]; }
                    refresh();
                });

                table.on('draw', refresh);
                table.on('xhr', function () { bulkSelected = {}; });

                form.addEventListener('submit', function (e) {
                    var useAll = allOption.checked;
                    var ids = bulkSelectedIds();
                    var count = useAll ? table.rows().count() : ids.length;
                    var typed = parseInt(document.getElementById('poiBulkCount').value, 10);
                    if (!count) { e.preventDefault(); alert(poiBulkStrings.noneSelected); return; }
                    if (typed !== count) { e.preventDefault(); alert(poiBulkStrings.countMismatch.replace('{0}', count.toLocaleString())); return; }
                    if (!confirm(poiBulkStrings.confirm.replace('{0}', count.toLocaleString()))) { e.preventDefault(); return; }
                    document.getElementById('poiBulkIds').value = useAll ? '' : ids.join(',');
                });

                refresh();
            }

            function deletePoi(poiId) {
                if (!confirm('Delete this POI?')) return;
                document.getElementById('deletePoiId').value = poiId;
                document.getElementById('deletePoiForm').submit();
            }
            viewType.deletePoi = deletePoi;

        })(viewType = mapping.viewType || (mapping.viewType = {}));
    })(mapping = resgrid.mapping || (resgrid.mapping = {}));
})(resgrid || (resgrid = {}));
