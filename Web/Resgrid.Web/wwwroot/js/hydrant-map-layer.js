(function (window) {
    'use strict';
    var L = window.L, config = window.rgHydrantMap;
    if (!L || !config || L.Map.prototype._rgHydrantHook) return;
    L.Map.prototype._rgHydrantHook = true;
    var request;
    function load() {
        if (!request) {
            request = fetch(config.url, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } })
                .then(function (response) {
                    if (response.status === 403 || response.status === 404) return null;
                    if (!response.ok || response.redirected) throw new Error('Hydrant layer unavailable');
                    return response.json();
                }).catch(function (error) { request = null; throw error; });
        }
        return request;
    }
    function field(point, name) { return point[name] !== undefined ? point[name] : point[name.charAt(0).toLowerCase() + name.slice(1)]; }
    L.Map.addInitHook(function () {
        var map = this;
        // Indoor floor plans use pixel coordinates. React maps render their own API-backed hydrant layer.
        if (map.options.crs === L.CRS.Simple || map.getContainer().closest('rg-map')) return;
        var removed = false, control, layer, errorControl;
        map.on('unload', function () { removed = true; });
        function render(points) {
            if (removed || points === null) return;
            if (!Array.isArray(points)) throw new Error('Invalid hydrant layer');
            if (errorControl) { errorControl.remove(); errorControl = null; }
            if (layer) { layer.clearLayers(); } else { layer = L.layerGroup(); }
            points.forEach(function (point) {
                var latitude = Number(field(point, 'Latitude')), longitude = Number(field(point, 'Longitude'));
                if (!Number.isFinite(latitude) || !Number.isFinite(longitude) || Math.abs(latitude) > 90 || Math.abs(longitude) > 180) return;
                var popup = document.createElement('div');
                var link = document.createElement('a');
                link.href = config.detailsUrl + '?id=' + encodeURIComponent(field(point, 'HydrantId'));
                link.textContent = field(point, 'HydrantNumber') || config.label;
                popup.appendChild(link);
                var flow = field(point, 'FlowGpm');
                if (flow !== null && flow !== undefined) { popup.appendChild(document.createElement('br')); popup.appendChild(document.createTextNode(flow + ' gpm')); }
                popup.appendChild(document.createElement('br'));
                popup.appendChild(document.createTextNode(field(point, 'InService') ? config.inService : config.outOfService));
                var color = field(point, 'Color') || '#999999';
                L.circleMarker([latitude, longitude], { radius: 7, color: color, fillColor: color, fillOpacity: 0.85, pmIgnore: true })
                    .bindPopup(popup).addTo(layer);
            });
            if (!control) {
                var overlays = {};
                overlays[config.label] = layer;
                control = L.control.layers(null, overlays, { collapsed: true }).addTo(map);
                layer.addTo(map);
            }
        }
        function refresh() {
            load().then(render).catch(function () {
                if (removed || errorControl) return;
                errorControl = L.control({ position: 'bottomleft' });
                errorControl.onAdd = function () {
                    var box = L.DomUtil.create('div', 'leaflet-bar');
                    box.style.cssText = 'background:white;padding:8px;max-width:240px;';
                    box.setAttribute('role', 'alert');
                    var retry = document.createElement('button');
                    retry.type = 'button';
                    retry.textContent = config.retry;
                    retry.className = 'btn btn-xs btn-default';
                    box.appendChild(document.createTextNode(config.failure + ' '));
                    box.appendChild(retry);
                    L.DomEvent.disableClickPropagation(box);
                    retry.onclick = function () { errorControl.remove(); errorControl = null; refresh(); };
                    return box;
                };
                errorControl.addTo(map);
            });
        }
        map.whenReady(refresh);
    });
})(window);
