
var resgrid;
(function (resgrid) {
    var dataprotection;
    (function (dataprotection) {
        var wizard;
        (function (wizard) {
            // Every command is a state-changing POST carrying the page's antiforgery token; the
            // server re-verifies managing member, addon, gate, MFA recency and state on each one.
            function antiForgeryToken() {
                return $('input[name="__RequestVerificationToken"]').first().val();
            }

            function post(url, data) {
                return $.ajax({
                    url: resgrid.absoluteBaseUrl + url,
                    type: 'POST',
                    data: data,
                    headers: { 'RequestVerificationToken': antiForgeryToken() }
                });
            }

            var errorText = {
                // Localized by the host view (adpWizardMessages), keyed by the value-free codes
                // the server returns. English fallbacks keep the UI readable if a key is missing.
                'acknowledgements_incomplete': 'Every acknowledgement must be checked before enrollment can be queued.',
                'lock_consent_required': 'The overnight operation pause must be consented to before enrollment can be queued.',
                'protected_access_denied': 'Only the department's managing member may run this command.',
                'addon_required': 'An active Advanced Data Protection addon is required.',
                'plan_required': 'Advanced Data Protection requires a paid plan.',
                'feature_not_available': 'Advanced Data Protection enrollment is temporarily unavailable.',
                'invalid_state': 'The department's protection state does not permit this command. Reload the page for current status.',
                'invalid_window': 'A valid migration window time zone is required.',
                'command_failed': 'The command could not be completed; it may be retried.'
            };

            // The host view supplies localized text in adpWizardMessages, keyed by the same
            // value-free codes the server returns; errorText above is the English fallback.
            function localized(code) {
                var messages = window.adpWizardMessages || {};
                return messages[code] || null;
            }

            function showError(container, code) {
                var text = localized(code) || errorText[code]
                    || localized('command_failed') || errorText['command_failed'];
                $(container).text('');
                $('<div class="alert alert-danger"></div>').text(text).appendTo($(container));
            }

            $(document).ready(function () {
                // ── Wizard step navigation ──────────────────────────────────────
                function goTo(step) {
                    $('.adp-step').hide();
                    $('.adp-step[data-step="' + step + '"]').show();
                }

                $('.adp-next').click(function () {
                    if ($(this).is(':disabled'))
                        return;
                    var current = parseInt($(this).closest('.adp-step').data('step'), 10);
                    goTo(current + 1);
                });
                $('.adp-prev').click(function () {
                    var current = parseInt($(this).closest('.adp-step').data('step'), 10);
                    goTo(current - 1);
                });

                // Step 2: continue only when every acknowledgement is checked.
                $('.adp-ack').change(function () {
                    var allChecked = $('.adp-ack').length === $('.adp-ack:checked').length;
                    $('#adpAckNext').prop('disabled', !allChecked);
                });

                // Step 5: continue only with explicit lock consent.
                $('#adpLockConsent').change(function () {
                    $('#adpLockNext').prop('disabled', !this.checked);
                });

                // Step 4: read-only sizing scan.
                $('#adpRunSizing').click(function () {
                    var btn = $(this);
                    btn.prop('disabled', true);
                    $('#adpSizingResult').html('<em>Scanning… this reads row counts only and changes nothing.</em>');

                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/DataProtection/SizingScan?windowMinutes=480',
                        type: 'GET'
                    }).done(function (result) {
                        var rows = '';
                        if (result.TableRowCounts) {
                            Object.keys(result.TableRowCounts).forEach(function (table) {
                                rows += '<tr><td>' + table + '</td><td>' + result.TableRowCounts[table].toLocaleString() + '</td></tr>';
                            });
                        }
                        $('#adpSizingResult').html(
                            '<table class="table table-condensed" style="max-width:480px"><thead><tr><th>Table</th><th>Rows</th></tr></thead><tbody>' + rows + '</tbody></table>' +
                            '<p><strong>' + Number(result.TotalRows).toLocaleString() + '</strong> rows total. Estimated migration time: ' +
                            '<strong>' + result.EstimatedP50Minutes + '&ndash;' + result.EstimatedP90Minutes + ' minutes</strong>, projected across ' +
                            '<strong>' + result.ProjectedNights + '</strong> overnight window(s). The estimate is a range, not a promise; the migration checkpoints every night and your department is in full service between windows.</p>');
                    }).fail(function () {
                        $('#adpSizingResult').html('<div class="alert alert-danger">The sizing scan could not run. You can retry, or continue — the migration worker re-checks sizing on execution night.</div>');
                    }).always(function () {
                        btn.prop('disabled', false);
                    });
                });

                // Step 6: final queue. The server rebuilds the acknowledgement record and re-runs
                // every gate; this just reports the outcome.
                $('#adpQueueEnrollment').click(function () {
                    var btn = $(this);
                    btn.prop('disabled', true);
                    $('#adpQueueError').empty();

                    var data = {
                        WindowStartLocal: $('#adpWindowStart').val(),
                        WindowEndLocal: $('#adpWindowEnd').val(),
                        WindowTimeZone: $('#adpWindowTimeZone').val(),
                        LockConsent: $('#adpLockConsent').is(':checked'),
                        AcknowledgedItems: $('.adp-ack:checked').map(function () { return this.value; }).get()
                    };

                    post('/User/DataProtection/QueueEnrollment', data).done(function (result) {
                        if (result.success) {
                            window.location.reload();
                        } else {
                            showError('#adpQueueError', result.error);
                            btn.prop('disabled', false);
                        }
                    }).fail(function () {
                        showError('#adpQueueError', 'command_failed');
                        btn.prop('disabled', false);
                    });
                });

                // ── Status-panel commands ───────────────────────────────────────
                $('#btnCancelQueued').click(function () {
                    if (!window.confirm(localized('confirm_cancel_queued') || 'Cancel the queued enrollment? Nothing has been migrated yet; you can enroll again later while the addon is active.'))
                        return;
                    var btn = $(this).prop('disabled', true);
                    post('/User/DataProtection/CancelQueuedEnrollment').done(function (result) {
                        if (result.success) { window.location.reload(); }
                        else { showError('#commandResult', result.error); btn.prop('disabled', false); }
                    }).fail(function () { showError('#commandResult', 'command_failed'); btn.prop('disabled', false); });
                });

                $('#btnRevokeOffboarding').click(function () {
                    if (!window.confirm(localized('confirm_revoke_offboarding') || 'Keep Advanced Data Protection active? The scheduled offboarding will be cancelled.'))
                        return;
                    var btn = $(this).prop('disabled', true);
                    post('/User/DataProtection/RevokeOffboarding').done(function (result) {
                        if (result.success) { window.location.reload(); }
                        else { showError('#commandResult', result.error); btn.prop('disabled', false); }
                    }).fail(function () { showError('#commandResult', 'command_failed'); btn.prop('disabled', false); });
                });
            });
        })(wizard = dataprotection.wizard || (dataprotection.wizard = {}));
    })(dataprotection = resgrid.dataprotection || (resgrid.dataprotection = {}));
})(resgrid || (resgrid = {}));
