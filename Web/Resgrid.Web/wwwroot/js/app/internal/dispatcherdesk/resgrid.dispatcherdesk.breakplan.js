var resgrid;
(function (resgrid) {
    var dispatcherdesk;
    (function (dispatcherdesk) {
        var breakplan;
        (function (breakplan) {
            // Break plan rows (_DispatcherBreakPlanEditor) and the shift pages' "Dispatchers on duty" section
            // (_ShiftDispatchSection): add / remove plan rows, show the details only while dispatchers are on, keep the
            // standing roster filter and the supervisor box in step. Buttons render disabled and are enabled here, so a
            // click before this script ran does nothing rather than failing.
            function updateEmpty(editor) {
                var rows = editor.find('tbody tr.dispatcher-break-plan-row').length;
                editor.find('.dispatcher-break-plan-empty').toggle(rows === 0);
            }

            function addRow(editor) {
                var template = editor.find('template.dispatcher-break-plan-template')[0];
                if (!template) {
                    return;
                }

                var index = parseInt(editor.attr('data-next-index'), 10);
                if (isNaN(index)) {
                    index = editor.find('tbody tr').length;
                }

                editor.attr('data-next-index', index + 1);
                var html = template.innerHTML.replace(/__index__/g, String(index));
                editor.find('tbody').append(html);
                updateEmpty(editor);
            }

            function initEditor(editor) {
                editor.find('.dispatcher-break-plan-add').prop('disabled', false).on('click', function () {
                    addRow(editor);
                });
                editor.find('.dispatcher-break-plan-remove').prop('disabled', false);
                editor.on('click', '.dispatcher-break-plan-remove', function () {
                    $(this).closest('tr').remove();
                    updateEmpty(editor);
                });
                updateEmpty(editor);
            }

            function initSection(section) {
                var enabled = section.find('.shift-dispatch-enabled');
                var details = section.find('.shift-dispatch-details');

                enabled.on('change', function () {
                    details.toggle($(this).is(':checked'));
                });

                section.find('.shift-dispatch-roster-filter').on('input', function () {
                    var text = ($(this).val() || '').toLowerCase();
                    section.find('tr.shift-dispatch-candidate').each(function () {
                        var name = String($(this).attr('data-name') || '').toLowerCase();
                        $(this).toggle(text.length === 0 || name.indexOf(text) >= 0);
                    });
                });

                // A desk supervisor is on the roster; taking someone off the roster takes the supervisor role too.
                section.on('change', '.shift-dispatch-supervisor', function () {
                    if ($(this).is(':checked')) {
                        $(this).closest('tr').find('.shift-dispatch-onroster').prop('checked', true);
                    }
                });
                section.on('change', '.shift-dispatch-onroster', function () {
                    if (!$(this).is(':checked')) {
                        $(this).closest('tr').find('.shift-dispatch-supervisor').prop('checked', false);
                    }
                });

                // The new shift wizard: only an Assigned shift (0) has a standing roster.
                var assignment = $('#AssignmentType');
                if (assignment.length) {
                    var toggleRoster = function () {
                        section.find('.shift-dispatch-roster').toggle(String(assignment.val()) === '0');
                    };
                    assignment.on('change', toggleRoster);
                    toggleRoster();
                }
            }

            $(document).ready(function () {
                $('.dispatcher-break-plan').each(function () {
                    initEditor($(this));
                });
                $('.shift-dispatch-section').each(function () {
                    initSection($(this));
                });
            });
        })(breakplan = dispatcherdesk.breakplan || (dispatcherdesk.breakplan = {}));
    })(dispatcherdesk = resgrid.dispatcherdesk || (resgrid.dispatcherdesk = {}));
})(resgrid || (resgrid = {}));
