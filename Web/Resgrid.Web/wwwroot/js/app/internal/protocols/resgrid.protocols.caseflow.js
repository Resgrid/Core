
var resgrid;
(function (resgrid) {
    var protocols;
    (function (protocols) {
        var caseflow;
        (function (caseflow) {
            // Protocols P4: the case flow builder in the protocol editor. The graph (questions, instructions, outcomes; children by
            // step key) lives in #DecisionGraphJson and is posted with the editor form; the server renders it into the version's
            // text and validates it on submit. The checks shown here are a convenience: the server's are the rule.
            var graph = { nodes: [] };
            var data = {};

            function t(key) { return (data.text && data.text[key]) || key; }
            function esc(value) { return resgrid.escapeHtml(value === null || value === undefined ? '' : String(value)); }
            function newKey() { return Math.random().toString(36).replace(/[^a-z0-9]/g, '').substring(0, 8) || ('n' + Date.now()); }
            function find(key) { for (var i = 0; i < graph.nodes.length; i++) { if (graph.nodes[i].key === key) return graph.nodes[i]; } return null; }

            function save() {
                // An empty graph is sent as one (a blank field would bind as "no builder" and keep the stored graph).
                $('#DecisionGraphJson').val(JSON.stringify(graph.nodes.length ? graph : { nodes: [] }));
                $(document).trigger('resgrid:graph-changed');
                showProblems();
            }

            function stepOptions(node, selected) {
                var html = '<option value="">' + esc(t('choose')) + '</option>';
                graph.nodes.forEach(function (other, index) {
                    if (other.key === node.key) return;
                    var kind = other.kind === 3 ? t('outcome') : other.kind === 2 ? t('instruction') : t('question');
                    html += '<option value="' + esc(other.key) + '"' + (other.key === selected ? ' selected' : '') + '>' +
                        esc((index + 1) + '. ' + kind + (other.label ? ': ' + other.label : (other.determinant ? ': ' + other.determinant : ''))) + '</option>';
                });
                return html;
            }

            function field(id, label, control) {
                return '<div class="form-group"><label class="control-label" for="' + id + '">' + esc(label) + '</label>' + control + '</div>';
            }

            function answersHtml(node) {
                var html = '';
                (node.answers || []).forEach(function (answer, i) {
                    var id = 'cf-' + node.key + '-a' + i;
                    html += '<div class="rgw-grid caseflow-answer" data-index="' + i + '">';
                    if (node.questionType === 1) {
                        html += '<div class="form-group"><span class="control-label">' + esc(t('answer')) + '</span><div><strong>' + esc(answer.key === 'yes' ? t('yes') : t('no')) + '</strong></div></div>';
                    } else {
                        html += field(id + '-label', t('answer'), '<input class="form-control" id="' + id + '-label" data-answer-field="label" maxlength="200" value="' + esc(answer.label) + '" />');
                    }
                    if (node.questionType === 3) {
                        html += field(id + '-min', t('min'), '<input class="form-control" type="number" step="any" id="' + id + '-min" data-answer-field="min" value="' + esc(answer.min) + '" />');
                        html += field(id + '-max', t('max'), '<input class="form-control" type="number" step="any" id="' + id + '-max" data-answer-field="max" value="' + esc(answer.max) + '" />');
                    }
                    html += field(id + '-next', t('next'), '<select class="form-control" id="' + id + '-next" data-answer-field="next">' + stepOptions(node, answer.next) + '</select>');
                    if (node.questionType !== 1) {
                        html += '<div class="form-group"><span class="control-label">&nbsp;</span><div><button type="button" class="btn btn-xs btn-danger caseflow-remove-answer"><i class="fa fa-minus" aria-hidden="true"></i> ' + esc(t('remove')) + '</button></div></div>';
                    }
                    html += '</div>';
                });
                if (node.questionType === 2 || node.questionType === 3) {
                    html += '<button type="button" class="btn btn-xs btn-success caseflow-add-answer"><i class="fa fa-plus" aria-hidden="true"></i> ' + esc(t('addAnswer')) + '</button>';
                }
                return html;
            }

            function nodeHtml(node, index) {
                var id = 'cf-' + node.key;
                var kind = node.kind === 3 ? t('outcome') : node.kind === 2 ? t('instruction') : (node.stage === 0 ? t('entryQuestion') : t('question'));
                var html = '<div class="panel panel-default rgw-builder-item caseflow-step" data-key="' + esc(node.key) + '">' +
                    '<div class="panel-heading rgw-builder-heading"><span class="rgw-builder-title">' + esc((index + 1) + '. ' + kind) +
                    (node.root ? ' <span class="label label-primary">' + esc(t('first')) + '</span>' : '') + '</span>' +
                    '<span class="rgw-builder-actions">' +
                    (node.root ? '' : '<button type="button" class="btn btn-xs btn-default caseflow-make-root"><i class="fa fa-play" aria-hidden="true"></i> ' + esc(t('makeFirst')) + '</button> ') +
                    '<button type="button" class="btn btn-xs btn-danger caseflow-remove"><i class="fa fa-trash" aria-hidden="true"></i> ' + esc(t('remove')) + '</button></span></div>' +
                    '<div class="panel-body"><div class="rgw-grid">' +
                    field(id + '-label', t('label'), '<input class="form-control" id="' + id + '-label" data-field="label" maxlength="200" value="' + esc(node.label) + '" />');

                if (node.kind === 1) {
                    html += field(id + '-stage', t('stage'), '<select class="form-control" id="' + id + '-stage" data-field="stage"><option value="0"' + (node.stage === 0 ? ' selected' : '') + '>' + esc(t('stageEntry')) + '</option><option value="1"' + (node.stage !== 0 ? ' selected' : '') + '>' + esc(t('stageKey')) + '</option></select>');
                    html += field(id + '-type', t('answerType'), '<select class="form-control" id="' + id + '-type" data-field="questionType">' +
                        [[1, 'yesNo'], [2, 'choice'], [3, 'number'], [4, 'freeText']].map(function (o) { return '<option value="' + o[0] + '"' + (node.questionType === o[0] ? ' selected' : '') + '>' + esc(t(o[1])) + '</option>'; }).join('') + '</select>');
                    html += '</div>' + field(id + '-text', t('text'), '<textarea class="form-control" rows="2" id="' + id + '-text" data-field="text" maxlength="2000">' + esc(node.text) + '</textarea>');
                    if (node.questionType === 4) {
                        html += field(id + '-next', t('next'), '<select class="form-control" id="' + id + '-next" data-field="next">' + stepOptions(node, node.next) + '</select>');
                    } else {
                        html += '<div class="caseflow-answers">' + answersHtml(node) + '</div>';
                    }
                } else if (node.kind === 2) {
                    html += field(id + '-next', t('next'), '<select class="form-control" id="' + id + '-next" data-field="next">' + stepOptions(node, node.next) + '</select>') + '</div>';
                    html += field(id + '-text', t('instructionText'), '<textarea class="form-control" rows="3" id="' + id + '-text" data-field="text" maxlength="2000">' + esc(node.text) + '</textarea>');
                    html += '<div class="checkbox"><label><input type="checkbox" data-field="speak"' + (node.speak ? ' checked' : '') + ' /> ' + esc(t('speak')) + '</label></div>';
                    html += '<div class="checkbox"><label><input type="checkbox" data-field="caller"' + (node.caller ? ' checked' : '') + ' /> ' + esc(t('caller')) + '</label></div>';
                } else {
                    var priorities = '<option value="">' + esc(t('noPriority')) + '</option>' + (data.priorities || []).map(function (p) {
                        return '<option value="' + p.id + '"' + (node.priority === p.id ? ' selected' : '') + '>' + esc(p.name) + '</option>';
                    }).join('');
                    html += field(id + '-det', t('determinant'), '<input class="form-control rgw-mono" id="' + id + '-det" data-field="determinant" maxlength="32" value="' + esc(node.determinant) + '" />') +
                        field(id + '-prio', t('priority'), '<select class="form-control" id="' + id + '-prio" data-field="priority">' + priorities + '</select>') +
                        field(id + '-resp', t('response'), '<input class="form-control" id="' + id + '-resp" data-field="response" maxlength="200" value="' + esc(node.response) + '" />') + '</div>';
                    html += field(id + '-post', t('postDispatch'), '<textarea class="form-control" rows="3" id="' + id + '-post" data-field="postDispatch" maxlength="4000">' + esc(node.postDispatch) + '</textarea>');
                    html += '<div class="checkbox"><label><input type="checkbox" data-field="caller"' + (node.caller ? ' checked' : '') + ' /> ' + esc(t('caller')) + '</label></div>';
                }

                return html + '</div></div>';
            }

            function render() {
                var $steps = $('#caseFlowSteps').empty();
                graph.nodes.forEach(function (node, index) { $steps.append(nodeHtml(node, index)); });
                $('#caseFlowEmpty').prop('hidden', graph.nodes.length > 0);
                showProblems();
            }

            function children(node) {
                var keys = [];
                (node.answers || []).forEach(function (a) { if (a.next) keys.push(a.next); });
                if (node.next) keys.push(node.next);
                return node.kind === 3 ? [] : keys;
            }

            // The same structural rules as the server (ProtocolDecisionGraphs.Validate), in short.
            function problems() {
                var list = [];
                if (!graph.nodes.length) return list;
                var roots = graph.nodes.filter(function (n) { return n.root; });
                if (roots.length !== 1) { list.push(t('noRoot')); return list; }
                graph.nodes.forEach(function (n, i) {
                    if (n.kind === 3 && !(n.determinant || '').trim()) list.push((i + 1) + '. ' + t('noDeterminant'));
                    if (n.kind !== 3) {
                        var missing = n.kind === 2 || n.questionType === 4 ? !find(n.next) : !(n.answers || []).length || (n.answers || []).some(function (a) { return !find(a.next); });
                        if (missing) list.push((i + 1) + '. ' + t('noNext'));
                    }
                });
                var seen = {}, onPath = {}, loop = false;
                (function visit(node) {
                    if (loop || !node) return;
                    if (onPath[node.key]) { loop = true; return; }
                    if (seen[node.key]) return;
                    seen[node.key] = onPath[node.key] = true;
                    children(node).forEach(function (k) { visit(find(k)); });
                    onPath[node.key] = false;
                })(roots[0]);
                if (loop) list.push(t('loop'));
                graph.nodes.forEach(function (n, i) { if (!seen[n.key]) list.push((i + 1) + '. ' + t('unreachable')); });
                return list;
            }

            function showProblems() {
                var list = problems();
                var $box = $('#caseFlowProblems');
                $box.prop('hidden', list.length === 0);
                $box.html(list.length ? '<strong>' + esc(t('problems')) + '</strong><ul>' + list.map(function (p) { return '<li>' + esc(p) + '</li>'; }).join('') + '</ul>' : '');
            }

            function answersFor(type) {
                if (type === 1) return [{ key: 'yes', label: '', next: null }, { key: 'no', label: '', next: null }];
                if (type === 2 || type === 3) return [{ key: 'a1', label: '', next: null }, { key: 'a2', label: '', next: null }];
                return null;
            }

            function init() {
                var $data = $('#caseFlowData');
                if (!$data.length) return;
                try { data = JSON.parse($data.text()) || {}; } catch (e) { data = {}; }
                try { graph = JSON.parse($('#DecisionGraphJson').val() || '{"nodes":[]}') || { nodes: [] }; } catch (e) { graph = { nodes: [] }; }
                graph.nodes = graph.nodes || [];

                $(document).on('click', '[data-caseflow-add]', function () {
                    var kind = Number($(this).data('caseflow-add'));
                    var node = { key: newKey(), kind: kind, root: graph.nodes.length === 0 };
                    if (kind === 1) { node.stage = 1; node.questionType = 1; node.answers = answersFor(1); }
                    graph.nodes.push(node);
                    render();
                    save();
                    $('#caseFlowSteps .caseflow-step').last().find('input,textarea,select').first().trigger('focus');
                });

                $('#caseFlowSteps').on('input change', '[data-field]', function () {
                    var node = find($(this).closest('.caseflow-step').data('key'));
                    if (!node) return;
                    var fieldName = $(this).data('field');
                    var value = $(this).is(':checkbox') ? $(this).is(':checked') : $(this).val();
                    if (fieldName === 'stage' || fieldName === 'questionType' || fieldName === 'priority') value = value === '' ? null : Number(value);
                    node[fieldName] = value === '' ? null : value;
                    if (fieldName === 'priority') {
                        // The name is rendered into the version's text with the graph.
                        var picked = (data.priorities || []).filter(function (p) { return p.id === value; })[0];
                        node.priorityName = picked ? picked.name : null;
                    }
                    if (fieldName === 'questionType') {
                        node.answers = answersFor(value);
                        node.next = null;
                        render();
                    }
                    save();
                });

                $('#caseFlowSteps').on('input change', '[data-answer-field]', function () {
                    var node = find($(this).closest('.caseflow-step').data('key'));
                    var index = Number($(this).closest('.caseflow-answer').data('index'));
                    if (!node || !node.answers || !node.answers[index]) return;
                    var fieldName = $(this).data('answer-field');
                    var value = $(this).val();
                    node.answers[index][fieldName] = (fieldName === 'min' || fieldName === 'max') ? (value === '' ? null : Number(value)) : (value === '' ? null : value);
                    save();
                });

                $('#caseFlowSteps').on('click', '.caseflow-add-answer', function () {
                    var node = find($(this).closest('.caseflow-step').data('key'));
                    if (!node) return;
                    node.answers = node.answers || [];
                    node.answers.push({ key: 'a' + newKey(), label: '', next: null });
                    render();
                    save();
                });

                $('#caseFlowSteps').on('click', '.caseflow-remove-answer', function () {
                    var node = find($(this).closest('.caseflow-step').data('key'));
                    node.answers.splice(Number($(this).closest('.caseflow-answer').data('index')), 1);
                    render();
                    save();
                });

                $('#caseFlowSteps').on('click', '.caseflow-make-root', function () {
                    var key = $(this).closest('.caseflow-step').data('key');
                    graph.nodes.forEach(function (n) { n.root = n.key === key; });
                    render();
                    save();
                });

                $('#caseFlowSteps').on('click', '.caseflow-remove', function () {
                    var key = $(this).closest('.caseflow-step').data('key');
                    graph.nodes = graph.nodes.filter(function (n) { return n.key !== key; });
                    // Pointers to the removed step are cleared, not left dangling.
                    graph.nodes.forEach(function (n) {
                        if (n.next === key) n.next = null;
                        (n.answers || []).forEach(function (a) { if (a.next === key) a.next = null; });
                    });
                    if (graph.nodes.length && !graph.nodes.some(function (n) { return n.root; })) graph.nodes[0].root = true;
                    render();
                    save();
                });

                render();
            }

            $(function () { init(); });
        })(caseflow = protocols.caseflow || (protocols.caseflow = {}));
    })(protocols = resgrid.protocols || (resgrid.protocols = {}));
})(resgrid || (resgrid = {}));
