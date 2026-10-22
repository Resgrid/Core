var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        // Conditional run cards: renders a recommendation's decision (path, outcome, advice, fallbacks, first-arriving
        // rows, pin) and per-unit response codes. Shared by the run card editor's Test tab, New Call and Update Call.
        var runcarddecision;
        (function (runcarddecision) {
            function getText(key, fallback) {
                return (resgrid.dispatch && typeof resgrid.dispatch.getText === 'function')
                    ? resgrid.dispatch.getText(key, fallback)
                    : fallback;
            }

            // Also used inside attribute values (title, style), so quotes are escaped too.
            function escapeHtml(value) {
                return $('<div/>').text(value == null ? '' : String(value)).html().replace(/"/g, '&quot;').replace(/'/g, '&#39;');
            }

            // MVC JSON is camelCase and v4 is PascalCase; read either.
            function prop(obj, name) {
                if (!obj) return undefined;
                if (obj[name] !== undefined) return obj[name];
                var pascal = name.charAt(0).toUpperCase() + name.slice(1);
                return obj[pascal];
            }
            runcarddecision.prop = prop;

            function formatSeconds(seconds) {
                if (seconds === null || seconds === undefined || isNaN(seconds)) return '?';
                var total = Math.max(0, Math.round(seconds));
                var rest = total % 60;
                return Math.floor(total / 60) + ':' + (rest < 10 ? '0' : '') + rest;
            }
            runcarddecision.formatSeconds = formatSeconds;

            function codeChip(code, name, color) {
                if (!code) return '';
                var style = color ? ' style="background-color:' + escapeHtml(color) + ';color:#fff"' : '';
                return ' <span class="label label-default"' + style + ' title="' + escapeHtml(name || '') + '">' + escapeHtml(code) + '</span>';
            }
            runcarddecision.codeChip = codeChip;

            function operandText(operand) {
                if (!operand) return '';
                var name = prop(operand, 'resourceName');
                if (!name) return escapeHtml(getText('rcNoneAvailable', 'none available') + ' (' + (prop(operand, 'targetsLabel') || '') + ')');
                return escapeHtml(name + ' ' + formatSeconds(prop(operand, 'responseSeconds')));
            }

            // The decision section only; null when the result has no decision (a fixed run card level).
            function renderDecision(decision) {
                if (!decision) return '';

                var html = '<div class="rc-decision" style="margin-top:6px">';

                if (prop(decision, 'pinned')) {
                    var pinnedOn = prop(decision, 'pinnedOn');
                    html += '<div class="text-muted"><i class="fa fa-thumb-tack"></i> ' + escapeHtml(getText('rcPinned', 'Decision pinned when the call was saved'))
                        + (pinnedOn ? ' (' + escapeHtml(typeof moment !== 'undefined' ? moment.utc(pinnedOn).local().format('L LT') : pinnedOn) + ')' : '') + '</div>';
                }

                var outcomeLabel = prop(decision, 'outcomeLabel');
                var packageName = prop(decision, 'packageName');
                if (outcomeLabel || packageName) {
                    html += '<div><b>' + escapeHtml(getText('rcOutcome', 'Outcome')) + ':</b> ' + escapeHtml(outcomeLabel || packageName)
                        + (packageName && outcomeLabel && packageName !== outcomeLabel ? ' <span class="text-muted">(' + escapeHtml(packageName) + ')</span>' : '') + '</div>';
                }

                var advice = prop(decision, 'adviceText');
                if (advice) {
                    html += '<div class="alert alert-info" style="padding:6px;margin:4px 0"><i class="fa fa-info-circle"></i> ' + escapeHtml(advice) + '</div>';
                }

                var path = prop(decision, 'path') || [];
                if (path.length) {
                    html += '<ol style="padding-left:18px;margin:4px 0">';
                    path.forEach(function (step) {
                        var yes = prop(step, 'result') === 1;
                        var routed = prop(step, 'timeSource') === 1;
                        html += '<li>' + escapeHtml(prop(step, 'label') || '') + ': <span class="label ' + (yes ? 'label-primary' : 'label-default') + '">'
                            + escapeHtml(yes ? getText('rcYes', 'Yes') : getText('rcNo', 'No')) + '</span> <span class="text-muted">';
                        var subject = prop(step, 'subject');
                        var reference = prop(step, 'reference');
                        if (subject) html += operandText(subject);
                        if (prop(step, 'conditionType') === 1 && reference) html += ' ' + escapeHtml(getText('rcVersus', 'vs')) + ' ' + operandText(reference);
                        if (prop(step, 'conditionType') === 2 && prop(step, 'thresholdSeconds') !== null) html += ' / ' + escapeHtml(formatSeconds(prop(step, 'thresholdSeconds')));
                        html += ' (' + escapeHtml(routed ? getText('rcRouted', 'routed') : getText('rcEstimated', 'estimated')) + ')</span>';
                        if (prop(step, 'uncertain')) {
                            html += ' <span class="label label-warning" title="' + escapeHtml(getText('rcUncertainHelp', 'A resource left out for its position could change this answer.')) + '">'
                                + escapeHtml(getText('rcUncertain', 'Uncertain')) + '</span>';
                        }
                        if (prop(step, 'note')) html += ' <span class="text-muted">' + escapeHtml(prop(step, 'note')) + '</span>';
                        html += '</li>';
                    });
                    html += '</ol>';
                }

                var fallbacks = prop(decision, 'fallbacks') || [];
                if (prop(decision, 'idealConfigurationUnavailable') || fallbacks.length) {
                    html += '<div class="alert alert-warning" style="padding:6px;margin:4px 0"><b>' + escapeHtml(getText('rcIdealUnavailable', 'Ideal configuration not available')) + '</b><ul style="margin:0;padding-left:18px">';
                    fallbacks.forEach(function (f) {
                        var step = prop(f, 'step');
                        var text = prop(f, 'rowLabel') + ': ';
                        if (step === 1) text += getText('rcRelaxed', 'status filter relaxed');
                        else if (step === 2) text += getText('rcSubstituted', 'substituted') + (prop(f, 'substitutePackageName') ? ' (' + prop(f, 'substitutePackageName') + ')' : '')
                            + (prop(f, 'sentInstead') ? ', ' + getText('rcSentInstead', 'sent') + ' ' + prop(f, 'sentInstead') : '');
                        else text += getText('rcShort', 'short') + ' ' + prop(f, 'filledCount') + '/' + prop(f, 'requiredCount');
                        html += '<li>' + escapeHtml(text) + (prop(f, 'outstanding') ? ' <span class="label label-danger">' + escapeHtml(getText('rcAwaiting', 'call will keep awaiting it')) + '</span>' : '') + '</li>';
                    });
                    html += '</ul></div>';
                }

                (prop(decision, 'firstArriving') || []).forEach(function (f) {
                    var text = prop(f, 'rowLabel') + ': ';
                    if (prop(f, 'alreadyIncluded')) {
                        text += prop(f, 'resourceName')
                            ? getText('rcFirstIncluded', 'first-arriving {0} ({1}) already included').replace('{0}', prop(f, 'resourceName')).replace('{1}', formatSeconds(prop(f, 'responseSeconds')))
                            : getText('rcFirstOnCall', 'a matching resource is already on the call');
                    } else {
                        text += getText('rcFirstAdded', 'added {0} ({1}), arrives first').replace('{0}', prop(f, 'resourceName') || '').replace('{1}', formatSeconds(prop(f, 'responseSeconds')));
                    }
                    html += '<div><i class="fa fa-bolt"></i> ' + escapeHtml(text) + '</div>';
                });

                if (prop(decision, 'uncertain')) {
                    html += '<div class="text-warning">' + escapeHtml(getText('rcDecisionUncertain', 'This decision is uncertain: some resources had no recent position.')) + '</div>';
                }

                return html + '</div>';
            }
            runcarddecision.renderDecision = renderDecision;

            // The whole result for the editor's Test tab: units, personnel, shortfalls, decision and notes.
            function renderResult(result) {
                if (!result || !prop(result, 'matchedRunCardId')) {
                    return '<div class="alert alert-info">' + escapeHtml((prop(result, 'notes') || [getText('rcNoCard', 'No run card matches.')]).join(' ')) + '</div>';
                }

                var html = '<h4>' + escapeHtml(prop(result, 'matchedRunCardName')) + ' <small>' + escapeHtml(getText('rcAlarmLevel', 'Alarm level')) + ' ' + escapeHtml(prop(result, 'alarmLevel')) + '</small></h4>';
                html += renderDecision(prop(result, 'decision'));

                var units = prop(result, 'units') || [];
                if (units.length) {
                    html += '<table class="table table-condensed"><thead><tr><th>' + escapeHtml(getText('rcUnit', 'Unit')) + '</th><th>' + escapeHtml(getText('rcRow', 'Row')) + '</th><th>'
                        + escapeHtml(getText('rcResponse', 'Response')) + '</th><th>' + escapeHtml(getText('rcStatus', 'Status')) + '</th></tr></thead><tbody>';
                    units.forEach(function (u) {
                        html += '<tr><td>' + escapeHtml(prop(u, 'unitName')) + ' <span class="text-muted">' + escapeHtml(prop(u, 'unitTypeName') || '') + '</span>'
                            + codeChip(prop(u, 'responseCode'), prop(u, 'responseCodeName'), prop(u, 'responseCodeColor')) + '</td><td>' + escapeHtml(prop(u, 'rowLabel') || '') + '</td><td>'
                            + escapeHtml(prop(u, 'responseSeconds') !== null && prop(u, 'responseSeconds') !== undefined ? formatSeconds(prop(u, 'responseSeconds')) : '') + '</td><td>'
                            + escapeHtml(prop(u, 'currentStatusText') || '') + '</td></tr>';
                    });
                    html += '</tbody></table>';
                }

                var personnel = prop(result, 'personnel') || [];
                if (personnel.length) {
                    html += '<div><b>' + escapeHtml(getText('rcPersonnel', 'Personnel')) + ':</b> ' + personnel.map(function (p) {
                        return escapeHtml((prop(p, 'name') || prop(p, 'userId')) + (prop(p, 'roleName') ? ' (' + prop(p, 'roleName') + ')' : ''));
                    }).join(', ') + '</div>';
                }

                var shortfalls = prop(result, 'shortfalls') || [];
                if (shortfalls.length) {
                    html += '<div class="text-danger"><b>' + escapeHtml(getText('rcShortfalls', 'Shortfalls')) + ':</b> ' + shortfalls.map(function (s) {
                        return escapeHtml((prop(s, 'typeOrRoleName') || ('#' + prop(s, 'typeOrRoleId'))) + ' ' + prop(s, 'filledCount') + '/' + prop(s, 'requiredCount'))
                            + (prop(s, 'outstanding') ? ' <span class="label label-danger">' + escapeHtml(getText('rcAwaitingShort', 'awaiting')) + '</span>' : '');
                    }).join(', ') + '</div>';
                }

                var notes = prop(result, 'notes') || [];
                if (notes.length) {
                    html += '<div class="text-muted" style="font-size:11px;margin-top:6px">' + notes.map(escapeHtml).join('<br/>') + '</div>';
                }

                return html;
            }
            runcarddecision.renderResult = renderResult;

            // Per-unit response code pickers for a form (unitResponseCode_{unitId}), defaulting to the proposal's code.
            // Empty when the department has no codes, so nothing about codes shows.
            function renderCodePickers(units, codes) {
                var active = (codes || []).filter(function (c) { return !prop(c, 'retired'); });
                if (!active.length || !units || !units.length) return '';

                var html = '<div class="rc-codes form-inline" style="margin-top:4px"><b>' + escapeHtml(getText('rcResponseCodes', 'Response codes')) + ':</b> ';
                units.forEach(function (u) {
                    var unitId = prop(u, 'unitId');
                    var selected = prop(u, 'responseCodeId');
                    html += '<label style="margin-right:10px;font-weight:normal">' + escapeHtml(prop(u, 'unitName')) + ' <select class="form-control input-sm" name="unitResponseCode_' + unitId + '">'
                        + '<option value="">' + escapeHtml(getText('rcNoCode', 'No code')) + '</option>';
                    active.forEach(function (c) {
                        html += '<option value="' + prop(c, 'id') + '"' + (selected === prop(c, 'id') ? ' selected' : '') + '>' + escapeHtml(prop(c, 'code') + ' ' + (prop(c, 'name') || '')) + '</option>';
                    });
                    html += '</select></label>';
                });
                return html + '</div>';
            }
            runcarddecision.renderCodePickers = renderCodePickers;
        })(runcarddecision = dispatch.runcarddecision || (dispatch.runcarddecision = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
