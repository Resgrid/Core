$(document).ready(function () {
    // File upload preview
    $('input[name="importFile"]').on('change', function () {
        var file = this.files[0];
        if (file) {
            var ext = file.name.split('.').pop().toLowerCase();
            var supported = ['geojson', 'json', 'kml', 'kmz'];
            if (supported.indexOf(ext) === -1) {
                alert('Unsupported file format. Please use GeoJSON, KML, or KMZ files.');
                $(this).val('');
            }
        }
    });
});
