var resgrid;
(function (resgrid) {
    var protocols;
    (function (protocols) {
        var steps;
        (function (steps) {
            // Step-through on a call (Protocols P3). Each step button is a plain form post (the page works without script); with
            // script the post is sent in place with json=true, so the member keeps their place while working through the steps.
            // The server timestamps every step; the page only shows what the server recorded.
            function texts() {
                try {
                    return JSON.parse($('#protocolStepText').text() || '{}');
                } catch (e) {
                    return {};
                }
            }

            function stateName(text, state) {
                switch (state) {
                    case 1: return text.done || '';
                    case 2: return text.skipped || '';
                    case 3: return text.notApplicable || '';
                    default: return text.pending || '';
                }
            }

            $(document).ready(function () {
                var text = texts();
                var busy = false;

                $(document).on('submit', '.rgp-step-form', function (event) {
                    event.preventDefault();
                    if (busy) {
                        return false;
                    }
                    busy = true;
                    var $form = $(this);
                    var $step = $form.closest('.rgp-run-step');
                    var data = $form.serializeArray();
                    data.push({ name: 'json', value: 'true' });
                    $step.attr('aria-busy', 'true');
                    $.ajax({ url: $form.attr('action'), type: 'POST', data: $.param(data) })
                        .done(function (result) {
                            if (!result || !result.success) {
                                $('#stepError').text((result && result.error) || text.failed || '').prop('hidden', false);
                                return;
                            }
                            $('#stepError').prop('hidden', true);
                            $step.removeClass('rgp-state-0 rgp-state-1 rgp-state-2 rgp-state-3').addClass('rgp-state-' + result.state);
                            $step.find('[data-step-state]').text(stateName(text, result.state));
                            $step.find('button[aria-pressed]').attr('aria-pressed', 'false');
                            $form.find('button').attr('aria-pressed', 'true');
                            var progress = (text.progress || '{0}/{1}').replace('{0}', result.done).replace('{1}', result.total);
                            $('#stepProgress').text(progress);
                        })
                        .fail(function () {
                            $('#stepError').text(text.failed || '').prop('hidden', false);
                        })
                        .always(function () {
                            busy = false;
                            $step.attr('aria-busy', 'false');
                        });
                    return false;
                });
            });
        })(steps = protocols.steps || (protocols.steps = {}));
    })(protocols = resgrid.protocols || (resgrid.protocols = {}));
})(resgrid || (resgrid = {}));
