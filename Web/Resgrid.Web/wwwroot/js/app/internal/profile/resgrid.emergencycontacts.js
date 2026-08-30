// Department-scoped emergency contacts on the profile page. A member manages their own; a
// department admin (or a group admin over that member) manages anyone's — the server enforces
// that on every call, this only shapes the UI.
//
// For a protected department the server returns the REDACTED placeholder instead of the stored
// values when the caller holds no Protected Data Grant. Values are inserted with text() so a
// decrypted value can never execute as markup.
(function (window, $) {
	'use strict';

	// English fallbacks; the host view supplies localized text through settings.messages.
	var DEFAULTS = {
		addTitle: 'Add Emergency Contact', editTitle: 'Edit Emergency Contact',
		none: 'No emergency contacts have been added.', yes: 'Yes', edit: 'Edit', remove: 'Delete',
		nameRequired: 'A name is required.', saveFailed: 'The contact could not be saved.',
		confirmRemove: 'Remove this emergency contact?'
	};

	function text(key) {
		var messages = (settings && settings.messages) || {};
		return messages[key] || DEFAULTS[key];
	}

	var settings = null;
	var contacts = [];
	var loadVersion = 0;

	function cell(value) {
		return $('<td></td>').text(value || '');
	}

	function render() {
		var $body = $('#emergencyContactsBody').empty();

		if (!contacts.length) {
			$body.append($('<tr></tr>').append(
				$('<td colspan="7"></td>').addClass('text-muted').text(text('none'))));
			return;
		}

		$.each(contacts, function (_, contact) {
			var $row = $('<tr></tr>')
				.append(cell(contact.name))
				.append(cell(contact.relationship))
				.append(cell(contact.phoneNumber))
				.append(cell(contact.alternatePhoneNumber))
				.append(cell(contact.email))
				.append(cell(contact.isPrimary ? text('yes') : ''));

			var $actions = $('<td></td>');
			$('<button type="button" class="btn btn-xs btn-default"></button>')
				.text(text('edit'))
				.on('click', function () { openModal(contact); })
				.appendTo($actions);
			$('<button type="button" class="btn btn-xs btn-danger" style="margin-left: 4px;"></button>')
				.text(text('remove'))
				.on('click', function () { remove(contact); })
				.appendTo($actions);

			$body.append($row.append($actions));
		});
	}

	function load() {
		// The list endpoint resolves protected values against whatever grant the request carries,
		// so the SAME call returns placeholders normally and plaintext while a reveal is active.
		// settings.beforeSend is the reveal module's header hook; without one this is unchanged.
		//
		// Reveal and conceal each trigger a load, and the two race: conceal fires its ungranted
		// request while the granted one is still in flight, and if the granted one lands last it
		// renders plaintext into a page the member has already concealed. Only the newest load is
		// allowed to render.
		var version = ++loadVersion;

		$.ajax({
			url: settings.listUrl,
			dataType: 'json',
			data: { userId: settings.userId },
			beforeSend: typeof settings.beforeSend === 'function' ? settings.beforeSend : undefined
		}).done(function (data) {
			if (version !== loadVersion)
				return;

			contacts = data || [];
			render();
		});
	}

	function openModal(contact) {
		$('#emergencyContactError').hide().text('');
		$('#ecId').val(contact ? contact.id : 0);
		$('#ecName').val(contact ? contact.name : '');
		$('#ecRelationship').val(contact ? contact.relationship : '');
		$('#ecPhone').val(contact ? contact.phoneNumber : '');
		$('#ecAltPhone').val(contact ? contact.alternatePhoneNumber : '');
		$('#ecEmail').val(contact ? contact.email : '');
		$('#ecNotes').val(contact ? contact.notes : '');
		$('#ecIsPrimary').prop('checked', contact ? !!contact.isPrimary : false);
		$('#emergencyContactModalTitle').text(contact ? text('editTitle') : text('addTitle'));
		$('#emergencyContactModal').modal('show');
	}

	function save() {
		var name = ($('#ecName').val() || '').trim();
		if (!name) {
			$('#emergencyContactError').text(text('nameRequired')).show();
			return;
		}

		// A field still showing the placeholder was never revealed to this user. The sentinel is
		// posted back AS IS so the server recognises it and keeps the stored value; blanking it
		// here instead would tell the server the member had deliberately cleared their
		// next-of-kin details.
		function submitted(id) {
			return ($(id).val() || '').trim();
		}

		$('#ecSaveBtn').prop('disabled', true);
		$.post(settings.saveUrl, {
			__RequestVerificationToken: settings.antiForgeryToken,
			Id: $('#ecId').val(),
			UserId: settings.userId,
			Name: submitted('#ecName'),
			Relationship: submitted('#ecRelationship'),
			PhoneNumber: submitted('#ecPhone'),
			AlternatePhoneNumber: submitted('#ecAltPhone'),
			Email: submitted('#ecEmail'),
			Notes: submitted('#ecNotes'),
			IsPrimary: $('#ecIsPrimary').is(':checked'),
			SortOrder: 0
		}).done(function (response) {
			$('#ecSaveBtn').prop('disabled', false);
			if (response && response.success) {
				$('#emergencyContactModal').modal('hide');
				load();
				return;
			}

			$('#emergencyContactError')
				.text(response && response.error === 'name_required' ? text('nameRequired') : text('saveFailed'))
				.show();
		}).fail(function () {
			$('#ecSaveBtn').prop('disabled', false);
			$('#emergencyContactError').text(text('saveFailed')).show();
		});
	}

	function remove(contact) {
		if (!window.confirm(text('confirmRemove')))
			return;

		$.post(settings.deleteUrl, {
			__RequestVerificationToken: settings.antiForgeryToken,
			id: contact.id,
			userId: settings.userId
		}).done(load);
	}

	window.resgridEmergencyContacts = {
		// Re-reads the list. The profile page calls this when a reveal starts (to pick up
		// plaintext) and when it ends (to put the placeholders back).
		reload: load,

		init: function (options) {
			settings = options;

			$('#addEmergencyContactBtn').on('click', function () { openModal(null); });
			$('#ecSaveBtn').on('click', save);

			load();
		}
	};
})(window, jQuery);
