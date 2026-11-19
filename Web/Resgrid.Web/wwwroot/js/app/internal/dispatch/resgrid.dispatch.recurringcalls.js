var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var recurringcalls;
        (function (recurringcalls) {
            // New / Edit Recurring Call: show the part of the schedule the chosen frequency uses, the lead time or the dispatcher
            // alert depending on who sends each call, keep at least one time row, and searchable recipient pickers.
            function showFrequency() {
                var frequency = $('#Frequency').val();

                // A hidden section's fields are disabled too, so the browser neither checks them (a day of the month left at 32 on the
                // monthly part would block saving a daily schedule) nor posts them. Their values are kept for switching back.
                $('.recurring-section').each(function () {
                    var active = $(this).data('frequency').toString() === frequency;
                    $(this).toggle(active);
                    $(this).find('input, select, textarea').prop('disabled', !active);
                });

                $('.recurring-unit').each(function () {
                    $(this).toggle($(this).data('frequency').toString() === frequency);
                });
            }

            function showMode() {
                var mode = $('#recurringCreationMode input[name="CreationMode"]:checked').val() || '0';

                $('.recurring-mode-section').each(function () {
                    $(this).toggle($(this).data('mode').toString() === mode);
                });
            }

            function addTimeRow(value) {
                var template = document.getElementById('recurringTimeTemplate');
                if (!template) return;

                var row = $(template.innerHTML);
                row.find('input[type="time"]').val(value || '');
                $('#recurringTimes').append(row);
                row.find('input[type="time"]').trigger('focus');
            }

            $(document).ready(function () {
                if (!document.getElementById('recurringCallForm')) return;

                resgrid.common.analytics.track('Recurring Call Edit');

                $('#Frequency').on('change', showFrequency);
                showFrequency();

                $('#recurringCreationMode input[name="CreationMode"]').on('change', showMode);
                showMode();

                $('#recurringAddTime').on('click', function () { addTimeRow(''); });

                $('#recurringTimes').on('click', '.recurring-remove-time', function () {
                    var rows = $('#recurringTimes .recurring-time-row');
                    if (rows.length > 1) {
                        $(this).closest('.recurring-time-row').remove();
                    } else {
                        rows.find('input[type="time"]').val('');
                    }
                });

                if ($.fn.select2) {
                    $('.recurring-targets').each(function () {
                        $(this).select2({
                            placeholder: $(this).data('placeholder') || '',
                            width: '100%',
                            closeOnSelect: false
                        });
                    });

                    $('.recurring-destination').select2({ allowClear: false, minimumResultsForSearch: 0, width: '100%' });
                }

                $('#deleteRecurringCallForm').on('submit', function (e) {
                    var message = $(this).data('confirm');
                    if (message && !window.confirm(message)) {
                        e.preventDefault();
                    }
                });
            });
        })(recurringcalls = dispatch.recurringcalls || (dispatch.recurringcalls = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
