var resgrid;
(function (resgrid) {
    var lmsclasses;
    (function (lmsclasses) {
        // Trainings -> Classes (T4, LMS): the kiosk's rotating QR (polled every 10 seconds; the code itself changes every 30), the member's
        // QR sign-in with the browser position when the session requires GPS, and "use my location" on the session form.
        $(document).ready(function () {
            var kiosk = document.getElementById('lms-class-kiosk');
            if (kiosk) {
                var url = kiosk.getAttribute('data-code-url');
                var image = document.getElementById('lms-class-qr');
                var list = document.getElementById('lms-class-present');
                var count = document.getElementById('lms-class-count');
                var poll = function () {
                    $.ajax({ url: url, cache: false, dataType: 'json' }).done(function (data) {
                        if (image && data && data.qr) {
                            image.setAttribute('src', data.qr);
                        }
                        if (list && data && data.present) {
                            while (list.firstChild) {
                                list.removeChild(list.firstChild);
                            }
                            for (var i = 0; i < data.present.length; i++) {
                                var item = document.createElement('li');
                                item.textContent = data.present[i];
                                list.appendChild(item);
                            }
                            if (count) {
                                count.textContent = String(data.present.length);
                            }
                        }
                    });
                };
                window.setInterval(poll, 10000);
            }

            var signin = document.getElementById('lms-class-signin');
            if (signin && signin.getAttribute('data-gps') === 'true' && navigator.geolocation) {
                var status = document.getElementById('lms-class-gps-status');
                navigator.geolocation.getCurrentPosition(function (position) {
                    document.getElementById('lms-class-lat').value = String(position.coords.latitude);
                    document.getElementById('lms-class-lon').value = String(position.coords.longitude);
                    if (status) {
                        status.textContent = status.textContent + ' ' + String.fromCharCode(0x2713);
                    }
                }, function () {
                    if (status) {
                        status.className = 'text-danger';
                    }
                }, { enableHighAccuracy: true, timeout: 15000, maximumAge: 60000 });
            }

            var here = document.getElementById('lms-class-here');
            if (here && navigator.geolocation) {
                here.addEventListener('click', function () {
                    navigator.geolocation.getCurrentPosition(function (position) {
                        document.getElementById('c-lat').value = position.coords.latitude.toFixed(6);
                        document.getElementById('c-lon').value = position.coords.longitude.toFixed(6);
                    });
                });
            }
        });
    })(lmsclasses = resgrid.lmsclasses || (resgrid.lmsclasses = {}));
})(resgrid || (resgrid = {}));
