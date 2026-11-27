/*
 * Training T2 / A1 course player (Courses/Play, Attempt, CrewViewing).
 * - Player: saves progress on every lesson change and a heartbeat every 15 seconds while the page is visible (the server caps seat
 *   time by its own clock), resumes the video where it was left, records the furthest point watched, and sets the playback speed.
 * - Attempt: counts down a timed attempt and submits it when the time runs out (the server enforces the deadline anyway).
 * - Crew viewing: refreshes the rotating QR code and the list of members present every 10 seconds.
 */
var resgrid;
(function (resgrid) {
    var courses;
    (function (courses) {
        var player;
        (function (player) {
            var HEARTBEAT_SECONDS = 15;

            function token(root) {
                var input = root.querySelector('input[name="__RequestVerificationToken"]');
                return input ? input.value : '';
            }

            function post(url, data, root) {
                var body = new URLSearchParams();
                Object.keys(data).forEach(function (key) {
                    if (data[key] !== null && data[key] !== undefined) {
                        body.append(key, String(data[key]));
                    }
                });
                body.append('__RequestVerificationToken', token(root));
                return fetch(url, {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                    body: body.toString(),
                    keepalive: true
                }).catch(function () { return null; });
            }

            function initPlayer() {
                var root = document.getElementById('lms-player');
                if (!root || root.getAttribute('data-running') !== 'true') {
                    return;
                }
                var url = root.getAttribute('data-progress-url');
                var enrollment = root.getAttribute('data-enrollment');
                var lesson = root.getAttribute('data-lesson');
                var video = document.getElementById('lms-video');
                var watched = 0;
                var last = Date.now();

                function snapshot(seconds) {
                    var data = { id: enrollment, lesson: lesson, seconds: seconds };
                    if (video && video.duration && isFinite(video.duration) && video.duration > 0) {
                        data.position = Math.floor(video.currentTime || 0);
                        data.watched = Math.min(100, Math.floor(watched * 100 / video.duration));
                    }
                    return data;
                }

                function beat() {
                    if (document.visibilityState !== 'visible') {
                        last = Date.now();
                        return;
                    }
                    var now = Date.now();
                    var seconds = Math.min(120, Math.round((now - last) / 1000));
                    last = now;
                    post(url, snapshot(seconds), root);
                }

                // The lesson change itself is saved straight away (resume lands here next time).
                post(url, snapshot(0), root);
                window.setInterval(beat, HEARTBEAT_SECONDS * 1000);
                document.addEventListener('visibilitychange', function () {
                    if (document.visibilityState === 'hidden') {
                        beat();
                    } else {
                        last = Date.now();
                    }
                });

                if (video) {
                    var resumeAt = parseInt(root.getAttribute('data-position') || '0', 10);
                    video.addEventListener('loadedmetadata', function () {
                        if (resumeAt > 0 && video.duration && resumeAt < video.duration - 5) {
                            video.currentTime = resumeAt;
                        }
                    });
                    video.addEventListener('timeupdate', function () {
                        // Only continuous playback moves the watched mark: seeking ahead does not count as watching.
                        if (!video.seeking && video.currentTime > watched && video.currentTime - watched < 3) {
                            watched = video.currentTime;
                        }
                    });
                    video.addEventListener('ended', function () {
                        post(url, snapshot(0), root);
                    });
                    var speed = document.getElementById('lms-speed');
                    if (speed) {
                        speed.addEventListener('change', function () {
                            var rate = parseFloat(speed.value);
                            if (rate > 0) {
                                video.playbackRate = rate;
                            }
                        });
                    }
                }
            }

            function initTimer() {
                var timer = document.getElementById('lms-timer');
                var form = document.getElementById('lms-attempt');
                if (!timer || !form) {
                    return;
                }
                var remaining = parseInt(timer.getAttribute('data-seconds') || '0', 10);
                var format = (window.resgridCoursesI18n && window.resgridCoursesI18n.minutesLeft) || '{0}';
                var submitted = false;
                window.setInterval(function () {
                    remaining -= 1;
                    if (remaining <= 0 && !submitted) {
                        submitted = true;
                        form.submit();
                        return;
                    }
                    var minutes = Math.max(0, Math.ceil(remaining / 60));
                    timer.lastChild.nodeValue = ' ' + format.replace('{0}', String(minutes));
                }, 1000);
            }

            function initCrew() {
                var crew = document.getElementById('lms-crew');
                if (!crew) {
                    return;
                }
                var url = crew.getAttribute('data-code-url');
                var image = document.getElementById('lms-crew-qr');
                var list = document.getElementById('lms-crew-list');
                var count = document.getElementById('lms-crew-count');
                window.setInterval(function () {
                    fetch(url, { credentials: 'same-origin', cache: 'no-store' })
                        .then(function (response) { return response.ok ? response.json() : null; })
                        .then(function (data) {
                            if (!data) {
                                return;
                            }
                            if (image && data.qr) {
                                image.src = data.qr;
                            }
                            if (list && data.attendees) {
                                while (list.firstChild) {
                                    list.removeChild(list.firstChild);
                                }
                                data.attendees.forEach(function (name) {
                                    var item = document.createElement('li');
                                    item.textContent = name;
                                    list.appendChild(item);
                                });
                                if (count) {
                                    count.textContent = String(data.attendees.length);
                                }
                            }
                        })
                        .catch(function () { });
                }, 10000);
            }

            function init() {
                initPlayer();
                initTimer();
                initCrew();
            }

            if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', init);
            } else {
                init();
            }
        })(player = courses.player || (courses.player = {}));
    })(courses = resgrid.courses || (resgrid.courses = {}));
})(resgrid || (resgrid = {}));
