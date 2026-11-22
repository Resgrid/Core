var resgrid;
(function (resgrid) {
    var dispatcherdesk;
    (function (dispatcherdesk) {
        var common;
        (function (common) {
            // Shared by the desk board and the break planner: posts with the antiforgery token, text formatting and the
            // result of a desk action (refused with a reason, or done, with warnings either way).
            function token() {
                var input = $('#deskActionToken input[name="__RequestVerificationToken"]');
                if (input.length) {
                    return input.val();
                }

                var meta = document.querySelector('meta[name="request-verification-token"]');
                return meta && meta.content ? meta.content : '';
            }
            common.token = token;

            function post(url, data) {
                var payload = $.extend({}, data || {});
                payload.__RequestVerificationToken = token();
                return $.ajax({ url: url, type: 'POST', data: payload, dataType: 'json' });
            }
            common.post = post;

            function format(text) {
                var args = Array.prototype.slice.call(arguments, 1);
                return String(text == null ? '' : text).replace(/\{(\d+)\}/g, function (match, index) {
                    var value = args[parseInt(index, 10)];
                    return value === undefined || value === null ? '' : String(value);
                });
            }
            common.format = format;

            function text(strings, key) {
                return strings && Object.prototype.hasOwnProperty.call(strings, key) ? strings[key] : key;
            }
            common.text = text;

            // 75 -> "1:15", 3725 -> "1:02:05".
            function duration(seconds) {
                var total = Math.max(0, Math.floor(seconds));
                var h = Math.floor(total / 3600);
                var m = Math.floor((total % 3600) / 60);
                var s = total % 60;
                var mm = h > 0 && m < 10 ? '0' + m : String(m);
                var ss = s < 10 ? '0' + s : String(s);
                return h > 0 ? h + ':' + mm + ':' + ss : mm + ':' + ss;
            }
            common.duration = duration;

            function escape(value) {
                return resgrid.escapeHtml(value);
            }
            common.escape = escape;

            // A desk action's answer as an alert above the page (warnings stay until the next action).
            function showResult(target, result, fallback) {
                var box = $(target);
                if (!result) {
                    box.removeClass('alert-success alert-warning').addClass('alert-danger').text(fallback || '').show();
                    return false;
                }

                var warnings = result.Warnings || [];
                if (!result.Success) {
                    box.removeClass('alert-success alert-warning').addClass('alert-danger');
                } else if (warnings.length) {
                    box.removeClass('alert-success alert-danger').addClass('alert-warning');
                } else {
                    box.hide();
                    return true;
                }

                var html = '<strong>' + escape(result.Message || fallback || '') + '</strong>';
                if (warnings.length) {
                    html += '<ul class="m-b-none">';
                    for (var i = 0; i < warnings.length; i++) {
                        html += '<li>' + escape(warnings[i]) + '</li>';
                    }
                    html += '</ul>';
                }

                box.html(html).show();
                return !!result.Success;
            }
            common.showResult = showResult;

            function percent(ms, fromMs, toMs) {
                if (toMs <= fromMs) {
                    return 0;
                }
                var value = (ms - fromMs) / (toMs - fromMs) * 100;
                return Math.max(0, Math.min(100, value));
            }
            common.percent = percent;

            // The realtime desk events (EventingTypes 19 / 20), when the eventing hub is reachable; polling covers the rest.
            function subscribe(callback) {
                document.addEventListener('resgrid:dispatcher-desk-updated', callback);
                document.addEventListener('resgrid:dispatcher-queue-updated', callback);

                if (resgrid.common && resgrid.common.signalr && resgrid.common.signalr.init) {
                    try {
                        // The hub client reads its four callbacks unguarded when their events arrive, so give it no-ops.
                        var none = function () { };
                        resgrid.common.signalr.init(none, none, none, none);
                    } catch (e) {
                        // The board still refreshes on its timer.
                    }
                }
            }
            common.subscribe = subscribe;
        })(common = dispatcherdesk.common || (dispatcherdesk.common = {}));
    })(dispatcherdesk = resgrid.dispatcherdesk || (resgrid.dispatcherdesk = {}));
})(resgrid || (resgrid = {}));
