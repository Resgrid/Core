var resgrid;
(function (resgrid) {
	var department;
	(function (department) {
		var cadupdates;
		(function (cadupdates) {
			// Department → Call Settings → Updates from your CAD: shows the label or pattern box for the chosen call ID source, and the
			// test box, which posts the page's unsaved settings with a pasted email and shows what Resgrid reads (nothing is created).
			function pick(r, name) {
				if (!r) return null;
				if (r[name] !== undefined) return r[name];
				var pascal = name.charAt(0).toUpperCase() + name.slice(1);
				return r[pascal] !== undefined ? r[pascal] : null;
			}

			function escapeHtml(value) {
				return String(value === null || value === undefined ? '' : value)
					.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
			}

			function toggleSource() {
				var source = $('#IdSource').val();
				$('#idLabelGroup').toggle(source === '1');
				$('#idPatternGroup').toggle(source === '2');
				$('#idSearchGroup').toggle(source !== '0');
				$('#formatHasNoKey').toggle(source === '0');
			}

			function row(label, value) {
				if (value === null || value === undefined || value === '' || (Array.isArray(value) && value.length === 0))
					return '';
				var text = Array.isArray(value) ? value.join(', ') : value;
				return '<dt>' + escapeHtml(label) + '</dt><dd>' + escapeHtml(text) + '</dd>';
			}

			function render(r) {
				var s = window.cadUpdatesStrings || {};
				var closeKind = pick(r, 'closeKind');
				var readsAs = closeKind === 1 ? s.closeClose : closeKind === 2 ? s.closeCancel : s.closeNone;
				var html = row(s.callId, pick(r, 'cadCallId') || s.noCallId) + row(s.readsAs, readsAs);

				if (!pick(r, 'formatFoundCall'))
					html += '<dt></dt><dd class="text-warning">' + escapeHtml(s.noCall) + '</dd>';
				else
					html += row(s.name, pick(r, 'name')) + row(s.address, pick(r, 'address')) + row(s.coordinates, pick(r, 'coordinates')) +
						row(s.type, pick(r, 'type')) + row(s.priority, pick(r, 'priority')) + row(s.units, pick(r, 'units')) +
						row(s.nature, pick(r, 'nature')) + row(s.notes, pick(r, 'notes'));

				$('#cadUpdatesTestResult').html(html).show();
			}

			function test() {
				var s = window.cadUpdatesStrings || {};
				var data = $('#cadUpdatesForm').serializeArray();
				data.push({ name: 'TestSubject', value: $('#TestSubject').val() });
				data.push({ name: 'TestBody', value: $('#TestBody').val() });

				$.ajax({ url: s.previewUrl, type: 'POST', data: $.param(data), dataType: 'json' })
					.done(render)
					.fail(function () {
						$('#cadUpdatesTestResult').html('<dt></dt><dd class="text-danger">' + escapeHtml(s.failed) + '</dd>').show();
					});
			}

			$(document).ready(function () {
				$('#IdSource').on('change', toggleSource);
				$('#cadUpdatesTest').on('click', test);
				toggleSource();
			});
		})(cadupdates = department.cadupdates || (department.cadupdates = {}));
	})(department = resgrid.department || (resgrid.department = {}));
})(resgrid || (resgrid = {}));
