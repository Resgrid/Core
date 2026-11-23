// Facility maps on the New Call / Update Call map: the department's custom maps (site plans) and indoor maps
// (building floors) can be shown over the call map, and a click inside a room or region sets the call location to
// that spot and the call's Indoor Location to that room. Picking a room in the Indoor Location search does the same
// the other way round: the pin moves to the room and its floor is shown.
//
//   resgrid.dispatch.facilitymaps.mapReady(map, { setLocation: function (lat, lng) {...} })
//   resgrid.dispatch.facilitymaps.zoneAt(lat, lng)          -> the room under a click, or null
//   resgrid.dispatch.facilitymaps.selectZone(zone, lat, lng) -> fills the indoor location, pin and empty address
//   resgrid.dispatch.facilitymaps.zonePicked(searchResult)  -> after a pick in the Indoor Location search
//
// Data comes from the v4 mapping API through the website's same-origin facade (/api/web-bff).
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var facilitymaps;
        (function (facilitymaps) {
            var apiBase = '/api/web-bff/api/v4/Mapping/';
            var map = null;
            var options = {};
            var maps = [];
            var layersByMap = {};
            // What is drawn: { map, layer, zones (FeatureCollection), raster, shapes }.
            var shown = null;
            // The address a picked room filled in (a later room replaces it; a typed address is kept).
            var roomAddress = null;
            var mapSelect = null;
            var layerSelect = null;

            function text(key, fallback) {
                return resgrid.dispatch && typeof resgrid.dispatch.getText === 'function' ? resgrid.dispatch.getText(key, fallback) : fallback;
            }

            function getJson(path) {
                return fetch(apiBase + path, { credentials: 'same-origin', headers: { Accept: 'application/json' } }).then(function (response) {
                    if (!response.ok) {
                        throw new Error(path + ' failed with HTTP ' + response.status);
                    }
                    return response.json();
                });
            }

            // The API hands out an absolute, signed tile URL on its own host; the website loads it through its facade.
            function sameOriginTiles(url) {
                if (!url) {
                    return null;
                }
                var at = url.toLowerCase().indexOf('/api/v4/');
                return at < 0 ? null : '/api/web-bff' + url.slice(at);
            }

            function boundsOf(source) {
                if (!source) {
                    return null;
                }
                var n = Number(source.BoundsNELat), e = Number(source.BoundsNELon), s = Number(source.BoundsSWLat), w = Number(source.BoundsSWLon);
                if (!isFinite(n) || !isFinite(e) || !isFinite(s) || !isFinite(w) || n <= s || e <= w) {
                    return null;
                }
                return [w, s, e, n];
            }

            function sortedLayers(mapId) {
                return (layersByMap[mapId] || []).slice().sort(function (a, b) { return a.FloorOrder - b.FloorOrder; });
            }

            function loadLayers(mapId) {
                if (layersByMap[mapId]) {
                    return Promise.resolve(layersByMap[mapId]);
                }
                return getJson('GetCustomMap/' + encodeURIComponent(mapId)).then(function (result) {
                    layersByMap[mapId] = (result && result.Data && result.Data.Layers) || [];
                    return layersByMap[mapId];
                });
            }

            function clear() {
                if (shown && shown.raster) {
                    shown.raster.remove();
                }
                if (shown && shown.shapes) {
                    shown.shapes.remove();
                }
                shown = null;
            }

            function draw(mapInfo, layer, fit) {
                clear();
                if (!map || !mapInfo || !layer) {
                    return;
                }
                var bounds = boundsOf(layer) || boundsOf(mapInfo);
                var state = { map: mapInfo, layer: layer, zones: null, raster: null, shapes: null };
                shown = state;

                var tiles = layer.HasImage ? sameOriginTiles(layer.MercatorTileUrl) : null;
                if (tiles && typeof map.addRasterOverlay === 'function') {
                    var opacity = Number(layer.Opacity);
                    state.raster = map.addRasterOverlay('facility', tiles, { bounds: bounds, opacity: opacity > 0 ? Math.min(opacity, 1) : 0.85 });
                }

                getJson('GetCustomMapRegionsGeoJSON/' + encodeURIComponent(layer.IndoorMapFloorId)).then(function (regions) {
                    if (shown !== state || !regions || regions.type !== 'FeatureCollection') {
                        return;
                    }
                    state.zones = regions;
                    state.shapes = map.addGeoJson('facility-zones', regions, { color: '#1c7ed6', weight: 1.5, opacity: 0.9, fillOpacity: 0.1 });
                }).catch(function (error) {
                    console.error('Unable to load the rooms of ' + layer.Name, error);
                });

                if (fit && bounds) {
                    map.fitBounds([[bounds[1], bounds[0]], [bounds[3], bounds[2]]], { padding: 20, maxZoom: 21, animate: true });
                }
            }

            function fillLayerSelect(mapInfo, selectedLayerId) {
                var layers = sortedLayers(mapInfo.IndoorMapId);
                $(layerSelect).empty();
                layers.forEach(function (layer) {
                    $(layerSelect).append($('<option>').val(layer.IndoorMapFloorId).text(layer.Name));
                });
                layerSelect.style.display = layers.length > 1 ? '' : 'none';
                var chosen = layers.filter(function (l) { return l.IndoorMapFloorId === selectedLayerId; })[0]
                    || layers.filter(function (l) { return l.IndoorMapFloorId === mapInfo.DefaultFloorId; })[0]
                    || layers[0];
                if (chosen) {
                    layerSelect.value = chosen.IndoorMapFloorId;
                }
                return chosen || null;
            }

            function showMap(mapId, layerId, fit) {
                var mapInfo = maps.filter(function (m) { return m.IndoorMapId === mapId; })[0];
                if (!mapInfo) {
                    clear();
                    if (layerSelect) {
                        layerSelect.style.display = 'none';
                    }
                    return Promise.resolve(null);
                }
                mapSelect.value = mapId;
                return loadLayers(mapId).then(function () {
                    var layer = fillLayerSelect(mapInfo, layerId);
                    draw(mapInfo, layer, fit);
                    return layer;
                });
            }

            function buildControl() {
                var box = document.createElement('div');
                box.className = 'rg-facility-maps';
                box.style.cssText = 'background:#fff;padding:6px;border-radius:4px;box-shadow:0 1px 4px rgba(0,0,0,.3);font-size:12px;max-width:220px;';

                var label = document.createElement('div');
                label.textContent = text('facilityMaps', 'Facility maps');
                label.style.cssText = 'font-weight:600;margin-bottom:4px;';
                box.appendChild(label);

                mapSelect = document.createElement('select');
                mapSelect.className = 'form-control input-sm';
                mapSelect.setAttribute('aria-label', text('facilityMaps', 'Facility maps'));
                $(mapSelect).append($('<option>').val('').text(text('facilityMapsNone', 'None')));
                maps.forEach(function (m) {
                    $(mapSelect).append($('<option>').val(m.IndoorMapId).text(m.Name));
                });
                box.appendChild(mapSelect);

                layerSelect = document.createElement('select');
                layerSelect.className = 'form-control input-sm';
                layerSelect.setAttribute('aria-label', text('facilityMapsFloor', 'Floor or layer'));
                layerSelect.style.cssText = 'display:none;margin-top:4px;';
                box.appendChild(layerSelect);

                var hint = document.createElement('div');
                hint.textContent = text('facilityMapsHint', 'Click a room to set the call location.');
                hint.style.cssText = 'color:#6b7280;margin-top:4px;';
                box.appendChild(hint);

                mapSelect.addEventListener('change', function () {
                    showMap(mapSelect.value, null, true);
                });
                layerSelect.addEventListener('change', function () {
                    var mapInfo = maps.filter(function (m) { return m.IndoorMapId === mapSelect.value; })[0];
                    var layer = sortedLayers(mapSelect.value).filter(function (l) { return l.IndoorMapFloorId === layerSelect.value; })[0];
                    draw(mapInfo, layer, false);
                });

                map.addControl(box, 'top-right');
            }

            // Ray casting on one ring of [lng, lat] positions.
            function inRing(lng, lat, ring) {
                var inside = false;
                for (var i = 0, j = ring.length - 1; i < ring.length; j = i++) {
                    var xi = ring[i][0], yi = ring[i][1], xj = ring[j][0], yj = ring[j][1];
                    if (((yi > lat) !== (yj > lat)) && (lng < (xj - xi) * (lat - yi) / (yj - yi) + xi)) {
                        inside = !inside;
                    }
                }
                return inside;
            }

            function polygons(geometry) {
                if (!geometry) {
                    return [];
                }
                if (geometry.type === 'Polygon') {
                    return [geometry.coordinates];
                }
                if (geometry.type === 'MultiPolygon') {
                    return geometry.coordinates;
                }
                return [];
            }

            function ringArea(ring) {
                var area = 0;
                for (var i = 0, j = ring.length - 1; i < ring.length; j = i++) {
                    area += (ring[j][0] + ring[i][0]) * (ring[j][1] - ring[i][1]);
                }
                return Math.abs(area / 2);
            }

            facilitymaps.zoneAt = function (lat, lng) {
                if (!shown || !shown.zones) {
                    return null;
                }
                var best = null;
                (shown.zones.features || []).forEach(function (feature) {
                    var props = feature.properties || {};
                    if (props.isDispatchable === false) {
                        return;
                    }
                    polygons(feature.geometry).forEach(function (poly) {
                        var outer = poly[0] || [];
                        var holes = poly.slice(1);
                        if (!inRing(lng, lat, outer) || holes.some(function (h) { return inRing(lng, lat, h); })) {
                            return;
                        }
                        // The smallest area wins: a room inside a building outline, a muster point inside a lot.
                        var area = ringArea(outer);
                        if (!best || area < best.area) {
                            best = { area: area, id: props.id || feature.id, text: props.name || '', floorId: shown.layer.IndoorMapFloorId, mapName: shown.map.Name, layerName: shown.layer.Name };
                        }
                    });
                });
                return best;
            };

            function zoneLabel(zone) {
                var parts = [zone.text];
                if (zone.layerName && zone.layerName !== zone.mapName) {
                    parts.push(zone.layerName);
                }
                if (zone.mapName) {
                    parts.push(zone.mapName);
                }
                return parts.filter(function (p) { return p; }).join(', ');
            }

            facilitymaps.selectZone = function (zone, lat, lng) {
                if (!zone || !zone.id) {
                    return;
                }
                $('#IndoorMapZoneId').val(zone.id);
                $('#IndoorMapFloorId').val(zone.floorId || '');
                var search = $('#indoorZoneSearch');
                if (search.length) {
                    search.find('option').remove();
                    search.append(new Option(zone.text, zone.id, true, true)).trigger('change');
                }
                if (isFinite(lat) && isFinite(lng) && typeof options.setLocation === 'function') {
                    options.setLocation(lat, lng);
                }
                // The room's name fills a blank address, or replaces the name an earlier room put there; an address the
                // dispatcher typed stays.
                var address = $('#Call_Address');
                var current = $.trim(address.val() || '');
                if (address.length && (current.length === 0 || current === roomAddress)) {
                    roomAddress = zoneLabel(zone);
                    address.val(roomAddress);
                }
            };

            // A pick in the Indoor Location search: move the pin to the room and show its floor.
            facilitymaps.zonePicked = function (result) {
                if (!result) {
                    return;
                }
                var lat = Number(result.lat), lng = Number(result.lng);
                if (isFinite(lat) && isFinite(lng) && !(lat === 0 && lng === 0) && typeof options.setLocation === 'function') {
                    options.setLocation(lat, lng);
                }
                if (result.floorId) {
                    facilitymaps.showFloor(result.floorId, false);
                }
            };

            facilitymaps.showFloor = function (floorId, fit) {
                if (!map || !floorId || !maps.length) {
                    return;
                }
                getJson('GetCustomMapLayer/' + encodeURIComponent(floorId)).then(function (result) {
                    var mapId = result && result.Data && (result.Data.IndoorMapId || (result.Data.Layer && result.Data.Layer.IndoorMapId));
                    if (mapId) {
                        showMap(mapId, floorId, fit);
                    }
                }).catch(function (error) {
                    console.error('Unable to show the floor of the indoor location', error);
                });
            };

            facilitymaps.mapReady = function (createdMap, readyOptions) {
                map = createdMap;
                options = readyOptions || {};
                getJson('GetCustomMaps').then(function (result) {
                    maps = ((result && result.Data) || []).filter(function (m) { return m && m.IndoorMapId; });
                    if (!maps.length) {
                        return;
                    }
                    buildControl();
                    // Update Call: a call that already has an indoor location opens with that floor shown.
                    var floorId = $('#IndoorMapFloorId').val();
                    if (floorId) {
                        facilitymaps.showFloor(floorId, false);
                    }
                }).catch(function (error) {
                    console.error('Unable to load the facility maps', error);
                });
            };
        })(facilitymaps = dispatch.facilitymaps || (dispatch.facilitymaps = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
