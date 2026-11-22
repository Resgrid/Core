var resgrid;
(function (resgrid) {
    var dispatcherdesk;
    (function (dispatcherdesk) {
        var breaks;
        (function (breaks) {
            // Shifts -> Dispatcher Breaks: a Gantt-like row per dispatcher (duty window, planned breaks, breaks taken or in
            // progress, missed ones), a coverage lane per slot (red below the minimum, hatched where breaks overlap), the
            // warnings, and the break form. Every position is a percentage of the window the server sent; every time shown
            // is a label the server formatted in department time.
            var options = null;
            var data = null;
            var skew = 0;
            var loading = false;
            var reloadQueued = false;
            var pending = null;
            var editing = null;

            var BREAK_KEY = { 1: 'Break', 2: 'Meal', 3: 'Personal', 4: 'Other' };
            var STATE_KEY = { 0: 'Scheduled', 1: 'Active', 2: 'Completed', 3: 'Missed' };

            function t(key) {
                return dispatcherdesk.common.text(options.strings, key);
            }

            function esc(value) {
                return dispatcherdesk.common.escape(value);
            }

            function pct(ms) {
                return dispatcherdesk.common.percent(ms, data.FromMs, data.ToMs);
            }

            function bar(css, startMs, endMs, text, title, attrs, extraStyle) {
                if (startMs == null || endMs == null || endMs <= data.FromMs || startMs >= data.ToMs) {
                    return '';
                }
                var left = pct(startMs);
                var width = Math.max(0.3, pct(endMs) - left);
                return '<div class="desk-bar ' + css + '" style="left:' + left.toFixed(3) + '%;width:' + width.toFixed(3) + '%;' + (extraStyle || '') + '"' +
                    ' title="' + esc(title || text || '') + '"' + (attrs || '') + '>' + esc(text || '') + '</div>';
            }

            function breakBars(row, b) {
                var current = Date.now() + skew;
                var typeName = t('BreakType.' + (BREAK_KEY[b.BreakType] || 'Break'));
                var stateName = t('BreakState.' + (STATE_KEY[b.State] || 'Scheduled'));
                var title = dispatcherdesk.common.format(t('BreakTooltipFormat'), typeName, stateName, b.Label);
                if (b.Note) { title += ' — ' + b.Note; }
                var attrs = ' data-break="' + esc(b.BreakId) + '" data-user="' + esc(row.UserId) + '"';
                var html = '';

                if (b.State === 0) {
                    html += bar('desk-bar-break desk-bar-planned' + (b.CanEdit ? ' desk-editable' : ''), b.PlannedStartMs, b.PlannedEndMs, typeName, title,
                        attrs + (b.CanEdit ? ' role="button" tabindex="0"' : ''));
                } else if (b.State === 1) {
                    var end = Math.max(b.ExpectedReturnMs || 0, current);
                    html += bar('desk-bar-break ' + (b.Overdue ? 'desk-bar-overdue' : 'desk-bar-active'), b.StartedMs, end,
                        b.Overdue ? t('OverdueBadge') : typeName, title, attrs);
                } else if (b.State === 2) {
                    if (b.PlannedStartMs != null && b.StartedMs != null && Math.abs(b.PlannedStartMs - b.StartedMs) > 60000) {
                        html += bar('desk-bar-break desk-bar-ghost', b.PlannedStartMs, b.PlannedEndMs, '', title, '');
                    }
                    html += bar('desk-bar-break desk-bar-taken', b.StartedMs, b.EndedMs || b.ExpectedReturnMs, typeName, title, attrs);
                } else if (b.State === 3) {
                    html += bar('desk-bar-break desk-bar-missed', b.PlannedStartMs, b.PlannedEndMs, typeName, title, attrs);
                }

                return html;
            }

            function gridlines() {
                return $.map(data.Ticks || [], function (tick) {
                    return '<div class="desk-timeline-gridline" style="left:' + pct(tick.Ms).toFixed(3) + '%;"></div>';
                }).join('');
            }

            function nowLine() {
                var current = Date.now() + skew;
                if (current < data.FromMs || current > data.ToMs) {
                    return '';
                }
                return '<div class="desk-timeline-now" style="left:' + pct(current).toFixed(3) + '%;"></div>';
            }

            function rowHtml(row) {
                var name = esc(row.Name);
                if (row.IsMe) { name += ' <span class="label label-info">' + esc(t('YouBadge')) + '</span>'; }
                if (row.IsSupervisor) { name += ' <span class="label label-primary">' + esc(t('DeskSupervisorBadge')) + '</span>'; }

                var track = gridlines();
                $.each(row.Duty || [], function (i, d) {
                    var css = 'desk-bar-duty' + (d.IsPending ? ' desk-bar-pending' : '');
                    var text = d.IsPending ? d.ShiftName + ' (' + t('PendingApprovalBadge') + ')' : d.ShiftName;
                    // The server only sends a #rgb / #rrggbb shift colour.
                    var color = d.Color && /^#[0-9a-fA-F]{3,8}$/.test(d.Color) ? 'border-left:4px solid ' + d.Color + ';' : '';
                    track += bar(css, d.StartMs, d.EndMs, text, d.Label, '', color);
                });
                $.each(row.Breaks || [], function (i, b) {
                    track += breakBars(row, b);
                });
                track += nowLine();

                return '<div class="desk-timeline-row"><div class="desk-timeline-name" title="' + esc(row.Name) + '">' + name + '</div>' +
                    '<div class="desk-timeline-track">' + track + '</div></div>';
            }

            function coverageHtml() {
                var cells = $.map(data.Slots || [], function (slot) {
                    var css = 'desk-coverage-cell';
                    if (slot.OnDuty > 0) {
                        css += slot.BelowMinimum || slot.Available <= 0 ? ' desk-below' : ' desk-ok';
                    }
                    if (slot.BreaksOverlap) { css += ' desk-overlap'; }
                    if (slot.IsNow) { css += ' desk-now'; }
                    var title = dispatcherdesk.common.format(t('CoverageSlotFormat'), slot.Label, slot.Available, slot.OnBreak, slot.OnDuty);
                    var count = slot.OnDuty > 0 ? '<span class="desk-coverage-count">' + slot.Available + '</span>' : '';
                    return '<div class="' + css + '" title="' + esc(title) + '">' + count + '</div>';
                }).join('');

                return '<div class="desk-timeline-row desk-timeline-coverage"><div class="desk-timeline-name"><strong>' + esc(t('CoverageLane')) + '</strong></div>' +
                    '<div class="desk-timeline-track">' + cells + '</div></div>';
            }

            function render() {
                if (!data) {
                    return;
                }

                $('#breaksTitle').text(data.Title || '');

                var select = $('#breaksShiftDay');
                var selected = String(select.attr('data-selected') || '');
                select.find('option:not(:first)').remove();
                $.each(data.ShiftDays || [], function (i, day) {
                    var option = $('<option></option>').val(String(day.ShiftDayId)).text(day.Label);
                    if (String(day.ShiftDayId) === selected) {
                        option.prop('selected', true);
                    }
                    select.append(option);
                });

                var axis = $.map(data.Ticks || [], function (tick) {
                    return '<div class="desk-timeline-tick" style="left:' + pct(tick.Ms).toFixed(3) + '%;">' + esc(tick.Label) + '</div>';
                }).join('');

                var html = '<div class="desk-timeline-inner">' +
                    '<div class="desk-timeline-row desk-timeline-axis"><div class="desk-timeline-name"></div><div class="desk-timeline-track">' + axis + '</div></div>';

                if (!(data.Rows || []).length) {
                    html += '<p class="text-muted m-t-sm">' + esc(t('NoDispatchersInWindow')) + '</p>';
                } else {
                    html += $.map(data.Rows, rowHtml).join('');
                }

                html += coverageHtml() + '</div>';
                $('#breaksTimeline').html(html);

                var warnings = data.Warnings || [];
                $('#breaksWarnings').html(warnings.length
                    ? $.map(warnings, function (w) { return '<li class="text-warning">' + esc(w) + '</li>'; }).join('')
                    : '<li class="text-muted" style="list-style:none;">' + esc(t('NoWarnings')) + '</li>');

                var editable = $.grep(data.Rows || [], function (r) { return r.CanEdit; });
                $('#breaksAdd').toggle(editable.length > 0).prop('disabled', editable.length === 0);
                $('#breaksPlanGroup').toggle(!!data.CanPlan);
                $('#breaksPlan').prop('disabled', !data.CanPlan);
            }

            function load() {
                // One GET at a time; a refresh asked for meanwhile (a save, a cancel, a realtime change) runs once it finishes, so the
                // page never keeps what an earlier GET read before the change.
                if (loading) {
                    reloadQueued = true;
                    return;
                }

                loading = true;
                reloadQueued = false;
                var query = { date: options.date };
                if (options.shiftDayId) {
                    query.shiftDayId = options.shiftDayId;
                }

                $.ajax({ url: options.dataUrl, type: 'GET', data: query, dataType: 'json', cache: false }).done(function (result) {
                    data = result;
                    skew = (result && result.ServerNowMs ? result.ServerNowMs : Date.now()) - Date.now();
                    render();
                }).fail(function () {
                    $('#breaksAlert').removeClass('alert-success alert-warning').addClass('alert-danger').text(t('PlannerLoadFailed')).show();
                }).always(function () {
                    loading = false;
                    if (reloadQueued) {
                        load();
                    }
                });
            }

            function soon() {
                if (pending) {
                    return;
                }
                pending = setTimeout(function () {
                    pending = null;
                    load();
                }, 1000);
            }

            function findRow(userId) {
                return $.grep(data.Rows || [], function (r) { return r.UserId === userId; })[0] || null;
            }

            function findBreak(row, breakId) {
                return row ? ($.grep(row.Breaks || [], function (b) { return b.BreakId === breakId; })[0] || null) : null;
            }

            function defaultStart(row) {
                var duty = row && row.Duty && row.Duty.length ? row.Duty[0] : null;
                return duty && duty.StartInput ? duty.StartInput : (options.date + 'T12:00');
            }

            function shiftDayFor(row) {
                if (options.shiftDayId) {
                    return options.shiftDayId;
                }
                return row && row.Duty && row.Duty.length === 1 ? row.Duty[0].ShiftDayId : '';
            }

            function openForm(row, b) {
                editing = b ? { breakId: b.BreakId, userId: row.UserId, shiftDayId: b.ShiftDayId } : null;

                var users = $('#breakUser').empty();
                $.each($.grep(data.Rows || [], function (r) { return r.CanEdit; }), function (i, r) {
                    users.append($('<option></option>').val(r.UserId).text(r.Name));
                });
                users.val(row ? row.UserId : users.find('option:first').val()).prop('disabled', !!b);

                var selectedRow = findRow(users.val());
                $('#breakId').val(b ? b.BreakId : '');
                $('#breakType').val(String(b ? b.BreakType : 1));
                $('#breakMinutes').val(b ? b.DurationMinutes : 15);
                $('#breakStart').val(b && b.StartInput ? b.StartInput : defaultStart(selectedRow));
                $('#breakNote').val(b && b.Note ? b.Note : '');
                $('#breakCancel').toggle(!!b);
                $('#breakModalTitle').text(t(b ? 'EditBreakTitle' : 'AddBreakTitle'));
                $('#breakModalAlert').hide();
                $('#breakModal').modal('show');
            }

            function save() {
                var userId = $('#breakUser').val();
                var payload = {
                    DispatcherBreakId: $('#breakId').val(),
                    UserId: userId,
                    ShiftDayId: editing ? (editing.shiftDayId || '') : shiftDayFor(findRow(userId)),
                    BreakType: $('#breakType').val(),
                    DurationMinutes: $('#breakMinutes').val(),
                    Start: $('#breakStart').val(),
                    Note: $('#breakNote').val()
                };

                $('#breakSave').prop('disabled', true);
                dispatcherdesk.common.post(options.saveUrl, payload).done(function (result) {
                    if (result && result.Success) {
                        $('#breakModal').modal('hide');
                        dispatcherdesk.common.showResult('#breaksAlert', result, t('ActionFailed'));
                        load();
                    } else {
                        dispatcherdesk.common.showResult('#breakModalAlert', result, t('ActionFailed'));
                    }
                }).fail(function () {
                    dispatcherdesk.common.showResult('#breakModalAlert', null, t('ActionFailed'));
                }).always(function () {
                    $('#breakSave').prop('disabled', false);
                });
            }

            function cancelBreak() {
                var breakId = $('#breakId').val();
                if (!breakId || !window.confirm(t('CancelBreakConfirm'))) {
                    return;
                }

                dispatcherdesk.common.post(options.cancelUrl, { dispatcherBreakId: breakId }).done(function (result) {
                    if (result && result.Success) {
                        $('#breakModal').modal('hide');
                        $('#breaksAlert').hide();
                        load();
                    } else {
                        dispatcherdesk.common.showResult('#breakModalAlert', result, t('ActionFailed'));
                    }
                }).fail(function () {
                    dispatcherdesk.common.showResult('#breakModalAlert', null, t('ActionFailed'));
                });
            }

            function plan() {
                var replace = $('#breaksPlanReplace').is(':checked');
                if (!data || !data.ShiftDayId || !window.confirm(t(replace ? 'PlanBreaksReplaceConfirm' : 'PlanBreaksConfirm'))) {
                    return;
                }

                $('#breaksPlan').prop('disabled', true);
                dispatcherdesk.common.post(options.planUrl, { shiftDayId: data.ShiftDayId, replace: replace }).done(function (result) {
                    var box = $('#breaksAlert');
                    if (result && result.Success && !(result.Warnings || []).length) {
                        box.removeClass('alert-danger alert-warning').addClass('alert-success').text(result.Message || '').show();
                    } else {
                        dispatcherdesk.common.showResult('#breaksAlert', result, t('ActionFailed'));
                    }
                    load();
                }).fail(function () {
                    dispatcherdesk.common.showResult('#breaksAlert', null, t('ActionFailed'));
                }).always(function () {
                    $('#breaksPlan').prop('disabled', !(data && data.CanPlan));
                });
            }

            function bind() {
                var page = $('#dispatcherBreaks');

                page.on('click keydown', '.desk-bar-break.desk-editable', function (e) {
                    if (e.type === 'keydown' && e.which !== 13 && e.which !== 32) {
                        return;
                    }
                    e.preventDefault();
                    var row = findRow($(this).attr('data-user'));
                    var b = findBreak(row, $(this).attr('data-break'));
                    if (row && b && b.CanEdit) {
                        openForm(row, b);
                    }
                });

                $('#breaksAdd').on('click', function () {
                    var mine = $.grep(data.Rows || [], function (r) { return r.CanEdit && r.IsMe; })[0];
                    openForm(mine || $.grep(data.Rows || [], function (r) { return r.CanEdit; })[0] || null, null);
                });
                $('#breakUser').on('change', function () {
                    if (!editing) {
                        $('#breakStart').val(defaultStart(findRow($(this).val())));
                    }
                });
                $('#breakSave').on('click', save);
                $('#breakCancel').on('click', cancelBreak);
                $('#breaksPlan').on('click', plan);

                // Picking a shift day (or a new date) reloads the page for that window.
                $('#breaksShiftDay').on('change', function () {
                    $('#breaksFilter').submit();
                });
                $('#breaksDate').on('change', function () {
                    $('#breaksShiftDay').val('');
                });
            }

            function init(settings) {
                options = settings;
                bind();
                load();
                setInterval(load, 60000);
                dispatcherdesk.common.subscribe(soon);

                if (resgrid.common && resgrid.common.analytics) {
                    resgrid.common.analytics.track('Dispatcher Desk - Breaks');
                }
            }
            breaks.init = init;
        })(breaks = dispatcherdesk.breaks || (dispatcherdesk.breaks = {}));
    })(dispatcherdesk = resgrid.dispatcherdesk || (resgrid.dispatcherdesk = {}));
})(resgrid || (resgrid = {}));
