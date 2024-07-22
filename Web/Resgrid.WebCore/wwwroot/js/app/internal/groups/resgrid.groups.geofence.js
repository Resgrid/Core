
var resgrid;
(function (resgrid) {
    var groups;
    (function (groups) {
        var geofence;
        (function (geofence) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Group Geofence');
                boundaryColor = '#f70c16'; // initialize color of polyline
                count = 0;
                paths = [];

                $("#colorPicker").minicolors({
                    animationSpeed: 50,
                    animationEasing: 'swing',
                    changeDelay: 0,
                    control: 'hue',
                    defaultValue: '#0080ff',
                    format: 'hex',
                    showSpeed: 100,
                    hideSpeed: 100,
                    inline: false,
                    theme: 'bootstrap'
                });

                // Initializing a map
                var latlng = new google.maps.LatLng(latitude, longitude);
                var myOptions = {
                    zoom: 9,
                    center: latlng,
                    mapTypeId: google.maps.MapTypeId.ROADMAP
                };
                // Draw a map on DIV "map_canvas"
                map = new google.maps.Map(document.getElementById("map_canvas"), myOptions);
                // Listen Click Event to draw Polygon
                google.maps.event.addListener(map, 'click', function (event) {
                    //polyCoordinates[count] = event.latLng;
                    polyCoordinates[count] = event.latLng; //new google.maps.LatLng(event.da.x, event.da.y)
                    createPolyline(polyCoordinates);
                    count++;
                });
                if (polyCoordinates == null)
                    polyCoordinates = [];
                else {
                    for (var i = 0; i < polyCoordinates.length; i++) {
                        polyCoordinates[i] = new google.maps.LatLng(polyCoordinates[i].k, polyCoordinates[i].A);
                    }
                    count = polyCoordinates.length - 1;
                    createPolyline(polyCoordinates);
                }
            });
            function createPolyline(polyC) {
                path = new google.maps.Polyline({
                    path: polyC,
                    strokeColor: boundaryColor,
                    strokeOpacity: 1.0,
                    strokeWeight: 2
                });
                path.setMap(map);
                paths.push(path);
            }
            geofence.createPolyline = createPolyline;
            function connectPoints() {
                //var point_add = []; // initialize an array
                var start = polyCoordinates[0]; // storing start point
                var end = polyCoordinates[(polyCoordinates.length - 1)]; // storing end point
                // pushing start and end point to an array
                //point_add.push(start);
                //point_add.push(end);
                polyCoordinates.push(start);
                polyCoordinates.push(end);
                //createPolyline(point_add); // function to join points
                createPolyline(polyCoordinates);
            }
            geofence.connectPoints = connectPoints;
            function saveGeofence() {
                connectPoints();
                $.ajax({
                    type: "POST",
                    async: true,
                    url: resgrid.absoluteBaseUrl + '/User/Groups/SaveGeofence',
                    contentType: 'application/json',
                    cache: false,
                    processData: false,
                    data: kendo.stringify({
                        DepartmentGroupId: $('#Group_DepartmentGroupId').val(),
                        Color: $('#colorPicker').val(),
                        GeoFence: JSON.stringify(polyCoordinates)
                    })
                }).done(function (data) {
                    $('#successArea').html('<p>Your geofence has been saved.</p>');
                    $('#successArea').show();
                    $('#alertArea').hide();
                }).fail(function (error) {
                    $('#alertArea').html('<p>Your geofence could not be saved. Please correct the errors and try again.</p>');
                    $('#successArea').hide();
                    $('#alertArea').show();
                });
            }
            geofence.saveGeofence = saveGeofence;
            function resetGeofence() {
                for (var i = 0; i < paths.length; i++) {
                    if (paths[i]) {
                        paths[i].setMap(null);
                        paths[i] = null;
                    }
                }
                paths = [];
                path = null;
                polyCoordinates = [];
                count = 0;
            }
            geofence.resetGeofence = resetGeofence;
        })(geofence = groups.geofence || (groups.geofence = {}));
    })(groups = resgrid.groups || (resgrid.groups = {}));
})(resgrid || (resgrid = {}));
