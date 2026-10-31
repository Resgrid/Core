var resgrid;
(function (resgrid) {
    var shifts;
    (function (shifts) {
        var shiftStaffing;
        (function (shiftStaffing) {
            // Picker and status text come from the page (resgridShiftsI18n); the English fallbacks only cover a page that
            // did not set them.
            function text(key, fallback) {
                return (typeof resgridShiftsI18n !== 'undefined' && resgridShiftsI18n[key]) ? resgridShiftsI18n[key] : fallback;
            }
            function esc(value) {
                return $('<div>').text(value === null || value === undefined ? '' : String(value)).html();
            }
            function format(template, values) {
                return String(template).replace(/\{(\d+)\}/g, function (match, index) {
                    return values[index] !== undefined ? values[index] : match;
                });
            }
            function pad(value) {
                return value < 10 ? '0' + value : '' + value;
            }
            // Shift colours are free text; only a hex colour goes into the style attribute.
            function swatchColor(color) {
                return /^#[0-9a-fA-F]{3,8}$/.test(color || '') ? color : '#cccccc';
            }
            function toPickerDate(date) {
                return pad(date.getMonth() + 1) + '/' + pad(date.getDate()) + '/' + date.getFullYear();
            }

            $(document).ready(function () {
                resgrid.common.analytics.track('Shifts - Shift Staffing');
                $('.sl2').select2();
                $("#shiftDayPicker").keypress(function (e) {
                    e.preventDefault();
                });

                // A day handed to the page (link or redirect after saving) opens once its days have loaded.
                shiftStaffing.pendingDay = (typeof resgridShiftStaffingState !== 'undefined' && resgridShiftStaffingState.day) ? resgridShiftStaffingState.day : '';

                $('input[name="StaffingKind"]').on('change', function () {
                    shiftStaffing.kindChanged();
                });
                $('#ShiftId').on('change', function () {
                    shiftStaffing.loadDays();
                });
                $('#WorkshiftId').on('change', function () {
                    shiftStaffing.loadDays();
                });
                $('#clearStaffingButton').on('click', function () {
                    if (window.confirm(text('clearConfirm', 'Remove the approved staffing for this day?'))) {
                        $('#clearStaffingForm').submit();
                    }
                });

                var now = new Date();
                var from = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 7);
                var to = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 7);
                $('#overviewStart').val(toPickerDate(from)).datetimepicker({ timepicker: false, format: 'm/d/Y', scrollMonth: false, scrollInput: false });
                $('#overviewEnd').val(toPickerDate(to)).datetimepicker({ timepicker: false, format: 'm/d/Y', scrollMonth: false, scrollInput: false });
                $('#overviewLoad').on('click', function () {
                    shiftStaffing.loadOverview();
                });
                $('#overviewType').on('change', function () {
                    shiftStaffing.renderOverview();
                });
                $('#staffingOverview').on('click', '.staffing-select', function () {
                    var row = shiftStaffing.overview[parseInt($(this).attr('data-index'), 10)];
                    if (row) {
                        shiftStaffing.selectOccurrence(row);
                    }
                });

                shiftStaffing.kindChanged();
                shiftStaffing.loadOverview();
            });

            function kind() {
                var checked = $('input[name="StaffingKind"]:checked').val();
                if (checked) {
                    return checked;
                }
                return $('input[name="StaffingKind"]').val() || 'shift';
            }
            shiftStaffing.kind = kind;

            function kindChanged() {
                var workshift = kind() === 'workshift';
                $('.staffing-shift-only').toggle(!workshift);
                $('.staffing-workshift-only').toggle(workshift);
                $('#groupStaffing').empty();
                $('#unitsStaffing').empty();
                shiftStaffing.loadDays();
            }
            shiftStaffing.kindChanged = kindChanged;

            function loadDays() {
                var workshift = kind() === 'workshift';
                var id = workshift ? $('#WorkshiftId').val() : $('#ShiftId').val();
                var seq = shiftStaffing.daysSeq = (shiftStaffing.daysSeq || 0) + 1;

                shiftStaffing.shiftDays = [];
                if (!id) {
                    shiftStaffing.initDatePicker();
                    shiftStaffing.loadStaffing();
                    return;
                }

                var url = workshift
                    ? resgrid.absoluteBaseUrl + '/User/Shifts/GetWorkshiftDays?workshiftId=' + encodeURIComponent(id)
                    : resgrid.absoluteBaseUrl + '/User/Shifts/GetShiftDays?shiftId=' + encodeURIComponent(id);

                $.ajax({
                    url: url,
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    if (seq !== shiftStaffing.daysSeq) return;
                    shiftStaffing.shiftDays = data || [];
                    shiftStaffing.initDatePicker();

                    var day = shiftStaffing.pendingDay;
                    shiftStaffing.pendingDay = '';
                    if (day && shiftStaffing.hasDay(day)) {
                        $('#shiftDayPicker').val(day);
                    }
                    shiftStaffing.loadStaffing();
                });
            }
            shiftStaffing.loadDays = loadDays;

            function hasDay(day) {
                var days = shiftStaffing.shiftDays || [];
                for (var i = 0; i < days.length; i++) {
                    if (days[i].Day === day) {
                        return true;
                    }
                }
                return false;
            }
            shiftStaffing.hasDay = hasDay;

            function compareDates(date, dates) {
                for (var i = 0; i < dates.length; i++) {
                    var shiftDay = new Date(dates[i].Day);
                    if (shiftDay.getDate() == date.getDate() &&
                        shiftDay.getMonth() == date.getMonth() &&
                        shiftDay.getFullYear() == date.getFullYear()) {
                        return true;
                    }
                }
                return false;
            }
            shiftStaffing.compareDates = compareDates;

            function initDatePicker() {
                $("#shiftDayPicker").val('');
                if ($("#shiftDayPicker").data('xdsoft_datetimepicker')) {
                    $("#shiftDayPicker").datetimepicker('destroy');
                }
                // Past days are open too: staffing an ended day records who worked it (for the on-shift hours report).
                resgrid.shifts.shiftStaffing.datePicker = $("#shiftDayPicker").datetimepicker({
                    timepicker: false,
                    format: 'm/d/Y',
                    scrollMonth: false,
                    scrollInput: false,
                    onSelectDate: function () {
                        resgrid.shifts.shiftStaffing.loadStaffing();
                    },
                    onGenerate: function () {
                        // Disable days the shift does not run on
                        $(this).find('.xdsoft_date').each(function () {
                            var $td = $(this);
                            var y = $td.data('year');
                            var m = $td.data('month'); // 0-based
                            var d = $td.data('date');
                            if (y !== undefined && m !== undefined && d !== undefined) {
                                var cellDate = new Date(y, m, d);
                                if (!resgrid.shifts.shiftStaffing.compareDates(cellDate, shiftStaffing.shiftDays || [])) {
                                    $td.addClass('xdsoft_disabled');
                                }
                            }
                        });
                    }
                });
            }
            shiftStaffing.initDatePicker = initDatePicker;

            function loadStaffing() {
                var day = $('#shiftDayPicker').val();
                var workshift = kind() === 'workshift';
                // Only the latest shift/day selection may fill the pickers: a slower response for an earlier pick is
                // dropped, so it can never put that day's staffing on the day now being edited.
                var seq = shiftStaffing.inputsSeq = (shiftStaffing.inputsSeq || 0) + 1;

                $('#groupStaffing').empty();
                $('#unitsStaffing').empty();
                $('#clearStaffingButton').hide();
                highlightOverview();

                if (!day) {
                    $('#staffingStatus').text(text('selectDayFirst', 'Pick a shift and a day to see its staffing.'));
                    return;
                }

                $('#staffingStatus').text(text('loading', 'Loading...'));

                var url = workshift
                    ? resgrid.absoluteBaseUrl + '/User/Shifts/GetWorkshiftDayStaffing?workshiftId=' + encodeURIComponent($('#WorkshiftId').val()) + '&day=' + encodeURIComponent(day)
                    : resgrid.absoluteBaseUrl + '/User/Shifts/GetShiftDayStaffing?shiftId=' + encodeURIComponent($('#ShiftId').val()) + '&day=' + encodeURIComponent(day);

                $.ajax({
                    url: url,
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    if (seq !== shiftStaffing.inputsSeq) return;
                    if (!data || !data.Found) {
                        $('#staffingStatus').text(text('notFound', 'You must select a valid shift day'));
                        return;
                    }

                    shiftStaffing.current = data;
                    renderStatus(data, day);
                    if (workshift) {
                        renderUnits(data);
                    } else {
                        renderGroups(data);
                    }
                    highlightOverview();
                });
            }
            shiftStaffing.loadStaffing = loadStaffing;

            function nameList(people) {
                if (!people || !people.length) {
                    return '<em>' + esc(text('nobody', 'Nobody')) + '</em>';
                }
                var html = '<ul>';
                for (var i = 0; i < people.length; i++) {
                    var extra = [];
                    if (people[i].GroupName) extra.push(people[i].GroupName);
                    if (people[i].Source) extra.push(people[i].Source);
                    html += '<li>' + esc(people[i].Name) + (extra.length ? ' <span class="staffing-readonly">(' + esc(extra.join(', ')) + ')</span>' : '') + '</li>';
                }
                return html + '</ul>';
            }

            function automaticPeople(data) {
                if (data.Automatic) {
                    return data.Automatic;
                }
                var people = [];
                for (var i = 0; i < (data.Units || []).length; i++) {
                    for (var j = 0; j < data.Units[i].Automatic.length; j++) {
                        var person = $.extend({}, data.Units[i].Automatic[j]);
                        person.GroupName = data.Units[i].Name;
                        people.push(person);
                    }
                }
                return people;
            }

            function renderStatus(data, day) {
                var summary = data.Summary;
                var approved = summary.Source === 1;
                var html = '<div><span class="label ' + (approved ? 'staffing-approved' : 'staffing-automatic') + '" style="color:#fff;">' +
                    esc(summary.SourceName) + '</span> <strong>' + esc(summary.Name) + '</strong> ' +
                    '<span class="staffing-readonly">' + esc(summary.TypeName) + ' &middot; ' + esc(summary.Start) + ' - ' + esc(summary.End) +
                    ' (' + esc(summary.Hours) + 'h)</span></div>';

                if (summary.IsActive) {
                    html += '<div class="text-info">' + esc(text('inProgress', 'In progress')) + '</div>';
                }
                if (summary.IsOver) {
                    html += '<div class="text-muted">' + esc(text('dayOver', 'This day is over.')) + '</div>';
                }

                if (approved) {
                    html += '<div>' + esc(format(text('approvedByFormat', 'Approved by {0} on {1}'), [summary.SetBy || '', summary.SetOn || ''])) + '</div>';
                    if (data.Note) {
                        html += '<div><strong>' + esc(text('noteLabel', 'Note')) + ':</strong> ' + esc(data.Note) + '</div>';
                    }
                    if (data.Added && data.Added.length) {
                        html += '<div class="staffing-diff-added">' + esc(format(text('addedFormat', 'Added: {0}'), [data.Added.join(', ')])) + '</div>';
                    }
                    if (data.Removed && data.Removed.length) {
                        html += '<div class="staffing-diff-removed">' + esc(format(text('removedFormat', 'Left off: {0}'), [data.Removed.join(', ')])) + '</div>';
                    }
                } else {
                    html += '<div class="staffing-readonly">' + esc(summary.Kind === 'workshift' ? text('automaticStaticHelp', '') : text('automaticHelp', '')) + '</div>';
                }

                html += '<div style="margin-top:6px;"><strong>' + esc(text('automaticHeader', 'From the shift (automatic)')) + '</strong>' + nameList(automaticPeople(data)) + '</div>';

                $('#staffingStatus').html(html);

                if (data.CanClear) {
                    $('#clearStaffingKind').val(kind());
                    $('#clearShiftId').val(kind() === 'workshift' ? 0 : $('#ShiftId').val());
                    $('#clearWorkshiftId').val(kind() === 'workshift' ? $('#WorkshiftId').val() : '');
                    $('#clearDay').val(day);
                    $('#clearStaffingButton').show();
                }
            }

            function personnelSelect(id, placeholder, people) {
                var $select = $('#' + id);
                $select.select2({
                    placeholder: placeholder,
                    allowClear: true,
                    multiple: true,
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter',
                        dataType: 'json',
                        processResults: function (data) { return { results: $.map(data, function (u) { return { id: u.UserId, text: u.Name }; }) }; }
                    }
                });
                for (var i = 0; i < people.length; i++) {
                    $select.append(new Option(people[i].Name, people[i].UserId, true, true));
                }
                $select.trigger('change');
            }

            function renderGroups(data) {
                var personnel = data.Personnel || [];
                var html = '';
                var pickers = [];

                if (data.CanSetNonGroup) {
                    html += '<div class="form-group"><label class="control-label">' + esc(text('nonGroupPersonnel', 'Non - Group Personnel')) +
                        '</label><div class="controls"><div class="col-xs-6"><select id="shiftPersonnel" name="shiftPersonnel" style="width: 100%;"></select></div></div></div>';
                    pickers.push({ id: 'shiftPersonnel', placeholder: text('selectNonGroupPersonnel', 'Select Non-Group Personnel...'), people: personnel.filter(function (x) { return !x.GroupId; }) });
                }

                for (var i = 0; i < (data.Groups || []).length; i++) {
                    var group = data.Groups[i];
                    var people = personnel.filter(function (x) { return x.GroupId === group.Id; });

                    if (isAdmin || group.CanManage) {
                        var id = 'groupPersonnel_' + group.Id;
                        html += '<div class="form-group"><label class="control-label">' + esc(group.Name) + '</label><div class="controls"><div class="col-md-6">' +
                            '<div class="row"><div class="col-sm-10"><select id="' + id + '" name="' + id + '" style="width: 100%;"></select></div></div></div></div></div>';
                        pickers.push({ id: id, placeholder: text('selectPersonnelForGroup', 'Select Personnel for Group...'), people: people });
                    } else {
                        // Another supervisor's group: shown, kept as it is on save.
                        html += '<div class="form-group"><label class="control-label">' + esc(group.Name) + '</label><div class="controls staffing-readonly">' +
                            esc(text('otherSupervisor', '')) + nameList(people) + '</div></div>';
                    }
                }

                $('#groupStaffing').empty().append(html);

                for (var j = 0; j < pickers.length; j++) {
                    personnelSelect(pickers[j].id, pickers[j].placeholder, pickers[j].people);
                }
            }

            function renderUnits(data) {
                var units = data.Units || [];
                var html = '';

                if (!units.length) {
                    $('#unitsStaffing').empty().text(text('noUnits', 'This static shift has no units.'));
                    return;
                }

                for (var i = 0; i < units.length; i++) {
                    var unit = units[i];
                    var crew = $.map(unit.Automatic || [], function (x) { return x.Name; });
                    var id = 'unitPersonnel_' + unit.UnitId;
                    html += '<div class="form-group"><label class="control-label">' + esc(unit.Name) + '</label><div class="controls"><div class="col-md-6">' +
                        '<select id="' + id + '" name="' + id + '" style="width: 100%;"></select>' +
                        '<span class="help-block m-b-none">' + esc(format(text('unitCrewFormat', 'Crew: {0}'), [crew.length ? crew.join(', ') : text('nobody', 'Nobody')])) + '</span>' +
                        '</div></div></div>';
                }

                $('#unitsStaffing').empty().append(html);

                for (var j = 0; j < units.length; j++) {
                    personnelSelect('unitPersonnel_' + units[j].UnitId, text('selectPerson', 'Select Person...'), units[j].Personnel || []);
                }
            }

            function loadOverview() {
                var seq = shiftStaffing.overviewSeq = (shiftStaffing.overviewSeq || 0) + 1;
                $('#staffingOverview tbody').html('<tr><td colspan="9">' + esc(text('loading', 'Loading...')) + '</td></tr>');

                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Shifts/GetStaffingOverview?start=' + encodeURIComponent($('#overviewStart').val()) + '&end=' + encodeURIComponent($('#overviewEnd').val()),
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    if (seq !== shiftStaffing.overviewSeq) return;
                    shiftStaffing.overview = data || [];
                    renderOverview();
                });
            }
            shiftStaffing.loadOverview = loadOverview;

            function renderOverview() {
                var rows = shiftStaffing.overview || [];
                var type = $('#overviewType').val();
                var html = '';

                for (var i = 0; i < rows.length; i++) {
                    var row = rows[i];
                    if (type !== '' && type !== undefined && String(row.ShiftType) !== type) {
                        continue;
                    }

                    var approved = row.Source === 1;
                    var changes = approved && (row.AddedCount || row.RemovedCount)
                        ? ' <span class="staffing-readonly">' + esc(format(text('changesFormat', '+{0} / -{1}'), [row.AddedCount, row.RemovedCount])) + '</span>'
                        : '';

                    html += '<tr data-key="' + esc(row.Key) + '">' +
                        '<td>' + esc(row.Day) + '</td>' +
                        '<td><span class="staffing-swatch" style="background-color:' + swatchColor(row.Color) + ';"></span>' + esc(row.Name) + '</td>' +
                        '<td>' + esc(row.TypeName) + '</td>' +
                        '<td>' + esc(row.Start) + ' - ' + esc(row.End) + '</td>' +
                        '<td>' + esc(row.Hours) + '</td>' +
                        '<td>' + esc(row.People) + '</td>' +
                        '<td><span class="label ' + (approved ? 'staffing-approved' : 'staffing-automatic') + '" style="color:#fff;">' + esc(row.SourceName) + '</span>' + changes + '</td>' +
                        '<td>' + (approved ? esc(row.SetBy) + '<br/><span class="staffing-readonly">' + esc(row.SetOn) + '</span>' : '') + '</td>' +
                        '<td><button type="button" class="btn btn-xs btn-default staffing-select" data-index="' + i + '">' + esc(text('select', 'Select')) + '</button></td>' +
                        '</tr>';
                }

                if (!html) {
                    html = '<tr><td colspan="9">' + esc(text('noDays', 'No shift days in this range.')) + '</td></tr>';
                }

                $('#staffingOverview tbody').html(html);
                highlightOverview();
            }
            shiftStaffing.renderOverview = renderOverview;

            function highlightOverview() {
                var current = shiftStaffing.current && shiftStaffing.current.Summary ? shiftStaffing.current.Summary.Key : null;
                $('#staffingOverview tbody tr').each(function () {
                    $(this).toggleClass('staffing-selected', !!current && $(this).attr('data-key') === current && !!$('#shiftDayPicker').val());
                });
            }

            function selectOccurrence(row) {
                shiftStaffing.pendingDay = row.Day;

                if (row.Kind === 'workshift') {
                    $('input[name="StaffingKind"][value="workshift"]').prop('checked', true);
                    $('.staffing-shift-only').hide();
                    $('.staffing-workshift-only').show();
                    $('#WorkshiftId').val(row.WorkshiftId).trigger('change');
                } else {
                    $('input[name="StaffingKind"][value="shift"]').prop('checked', true);
                    $('.staffing-shift-only').show();
                    $('.staffing-workshift-only').hide();
                    $('#ShiftId').val(row.ShiftId).trigger('change');
                }

                $('html, body').animate({ scrollTop: $('#shiftDayPicker').offset().top - 120 }, 200);
            }
            shiftStaffing.selectOccurrence = selectOccurrence;
        })(shiftStaffing = shifts.shiftStaffing || (shifts.shiftStaffing = {}));
    })(shifts = resgrid.shifts || (resgrid.shifts = {}));
})(resgrid || (resgrid = {}));
