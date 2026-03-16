$(document).ready(function () {
    if (document.getElementById('routeMap')) {
        var tiles = L.tileLayer(
            osmTileUrl,
            {
                maxZoom: 19,
                attribution: osmTileAttribution
            }
        );

        var map = L.map('routeMap', {
            scrollWheelZoom: false
        }).setView([39.8283, -98.5795], 4).addLayer(tiles);
    }
});
