var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var recurringcallalerts;
        (function (recurringcallalerts) {
            // Recurring Calls → Needs attention: send, create or skip an occurrence that has no call, open the call an alert is
            // about, acknowledge or dismiss it. Also the Pause dialog (reason, resume date, delete the calls already created).
            var text = window.resgridRecurringAlertsText || {};

            function token() {
                return $('#recurringAlertsForm input[name="__RequestVerificationToken"]').val();
            }

            function post(action, data, button, onSuccess) {
                $('#recurringAlertsError').hide();
                var row = $(button).closest('tr');
                row.find('button').prop('disabled', true);

                data.__RequestVerificationToken = token();

                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/RecurringCalls/' + action,
                    type: 'POST',
                    data: data
                }).done(function (result) {
                    if (result && result.success) {
                        onSuccess(result, row);
                    } else {
                        $('#recurringAlertsError').text(text.failed || '').show();
                        row.find('button').prop('disabled', false);
                    }
                }).fail(function () {
                    $('#recurringAlertsError').text(text.failed || '').show();
                    row.find('button').prop('disabled', false);
                });
            }

            function removeRow(row) {
                row.fadeOut(200, function () {
                    row.remove();
                    var left = $('#recurringAlertsList tbody tr').length;
                    $('#recurringAlertCount').text(left);
                    if (left === 0) {
                        window.location.reload();
                    }
                });
            }

            $(document).ready(function () {
                $(document).on('click', '.rg-alert-send', function () {
                    var button = this;
                    post('DispatchOccurrenceNow', { id: $(button).data('id'), occurrence: $(button).data('occurrence') }, button, function (result) {
                        if (result.url) {
                            window.location.href = result.url;
                        } else {
                            window.location.reload();
                        }
                    });
                });

                $(document).on('click', '.rg-alert-create', function () {
                    var button = this;
                    post('CreateOccurrenceNow', { id: $(button).data('id'), occurrence: $(button).data('occurrence') }, button, function (result) {
                        if (result.url) {
                            window.location.href = result.url;
                        } else {
                            window.location.reload();
                        }
                    });
                });

                $(document).on('click', '.rg-alert-skip', function () {
                    var button = this;
                    // Skipping closes every open alert about that occurrence (a create-failed and a missed one can both be listed), so
                    // the list is drawn again rather than one row removed.
                    post('SkipOccurrence', { id: $(button).data('id'), occurrence: $(button).data('occurrence') }, button, function () {
                        window.location.reload();
                    });
                });

                $(document).on('click', '.rg-alert-ack', function () {
                    var button = this;
                    // Drawn again so the row shows who acknowledged it (it stays open, and counted, until it is resolved).
                    post('AcknowledgeAlert', { alertId: $(button).data('alert') }, button, function () {
                        window.location.reload();
                    });
                });

                $(document).on('click', '.rg-alert-dismiss', function () {
                    var button = this;
                    var note = window.prompt(text.dismissPrompt || '', '');
                    if (note === null) return;

                    post('DismissAlert', { alertId: $(button).data('alert'), note: note }, button, function (result, row) {
                        removeRow(row);
                    });
                });

                $(document).on('click', '.rg-pause', function () {
                    $('#pauseRecurringCallId').val($(this).data('id'));
                    $('#pauseRecurringCallName').text($(this).data('name') || '');
                    $('#pauseReason').val('');
                    $('#pauseResumeOn').val('');
                    $('#pauseDeleteUpcoming').prop('checked', false);
                    $('#pauseRecurringCallModal').modal('show');
                });
            });
        })(recurringcallalerts = dispatch.recurringcallalerts || (dispatch.recurringcallalerts = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
