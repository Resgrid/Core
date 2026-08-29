using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Attended protected-read pipeline for calls and their children. See
	/// <see cref="IProtectedReadService"/> for the contract. The field sets mirror the
	/// catalog-v1 bindings (AdpTableBindings) — the same lists the migration engine envelopes —
	/// through static accessor maps so a binding change without a matching accessor fails loudly in
	/// the parity test, not silently in redaction. Attachment binary payloads (rgdpb) ride the
	/// broker as base64 and only when a file-serving endpoint opts in. Registered ONLY in web-host
	/// composition roots (it depends on the broker client).
	/// </summary>
	public class ProtectedReadService : IProtectedReadService, IProtectedWriteService
	{
		/// <summary>Catalog field id -> (getter, setter) for every cataloged Calls text column.
		/// Public so the parity test can pin it against AdpTableBindings.V1.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<Call, string> Get, Action<Call, string> Set)> CallFieldAccessors =
			new Dictionary<string, (Func<Call, string>, Action<Call, string>)>
			{
				["calls.name"] = (c => c.Name, (c, v) => c.Name = v),
				["calls.type"] = (c => c.Type, (c, v) => c.Type = v),
				["calls.natureofcall"] = (c => c.NatureOfCall, (c, v) => c.NatureOfCall = v),
				["calls.notes"] = (c => c.Notes, (c, v) => c.Notes = v),
				["calls.completednotes"] = (c => c.CompletedNotes, (c, v) => c.CompletedNotes = v),
				["calls.address"] = (c => c.Address, (c, v) => c.Address = v),
				["calls.geolocationdata"] = (c => c.GeoLocationData, (c, v) => c.GeoLocationData = v),
				["calls.w3w"] = (c => c.W3W, (c, v) => c.W3W = v),
				["calls.contactname"] = (c => c.ContactName, (c, v) => c.ContactName = v),
				["calls.contactnumber"] = (c => c.ContactNumber, (c, v) => c.ContactNumber = v),
				["calls.sourceidentifier"] = (c => c.SourceIdentifier, (c, v) => c.SourceIdentifier = v),
				["calls.incidentnumber"] = (c => c.IncidentNumber, (c, v) => c.IncidentNumber = v),
				["calls.externalidentifier"] = (c => c.ExternalIdentifier, (c, v) => c.ExternalIdentifier = v),
				["calls.referencenumber"] = (c => c.ReferenceNumber, (c, v) => c.ReferenceNumber = v),
				["calls.callformdata"] = (c => c.CallFormData, (c, v) => c.CallFormData = v),
				["calls.deletedreason"] = (c => c.DeletedReason, (c, v) => c.DeletedReason = v)
			};

		/// <summary>CallNotes text columns (parity-pinned).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<CallNote, string> Get, Action<CallNote, string> Set)> NoteFieldAccessors =
			new Dictionary<string, (Func<CallNote, string>, Action<CallNote, string>)>
			{
				["callnotes.note"] = (n => n.Note, (n, v) => n.Note = v),
				["callnotes.flaggedreason"] = (n => n.FlaggedReason, (n, v) => n.FlaggedReason = v)
			};

		/// <summary>CallNotes companion columns: envelope property + typed setter (parity-pinned).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<CallNote, string> GetEnvelope, Action<CallNote, decimal?> SetTyped)> NoteCompanionAccessors =
			new Dictionary<string, (Func<CallNote, string>, Action<CallNote, decimal?>)>
			{
				["callnotes.latitude"] = (n => n.ProtectedLatitudeEnvelope, (n, v) => n.Latitude = v),
				["callnotes.longitude"] = (n => n.ProtectedLongitudeEnvelope, (n, v) => n.Longitude = v)
			};

		/// <summary>CallAttachments text columns (parity-pinned; Data is the separate binary field).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<CallAttachment, string> Get, Action<CallAttachment, string> Set)> AttachmentFieldAccessors =
			new Dictionary<string, (Func<CallAttachment, string>, Action<CallAttachment, string>)>
			{
				["callattachments.name"] = (a => a.Name, (a, v) => a.Name = v),
				["callattachments.filename"] = (a => a.FileName, (a, v) => a.FileName = v),
				["callattachments.flaggedreason"] = (a => a.FlaggedReason, (a, v) => a.FlaggedReason = v)
			};

		/// <summary>CallAttachments companion columns (parity-pinned).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<CallAttachment, string> GetEnvelope, Action<CallAttachment, decimal?> SetTyped)> AttachmentCompanionAccessors =
			new Dictionary<string, (Func<CallAttachment, string>, Action<CallAttachment, decimal?>)>
			{
				["callattachments.latitude"] = (a => a.ProtectedLatitudeEnvelope, (a, v) => a.Latitude = v),
				["callattachments.longitude"] = (a => a.ProtectedLongitudeEnvelope, (a, v) => a.Longitude = v)
			};

		/// <summary>The rgdpb binary attachment payload field id.</summary>
		public const string AttachmentDataFieldId = "callattachments.data";

		/// <summary>
		/// CallLogs text columns (parity-pinned). CallLogs is the per-call running log, a different
		/// table and entity from the Log family's incident work logs — both happen to call their
		/// text column Narrative.
		/// </summary>
		public static readonly IReadOnlyDictionary<string, (Func<CallLog, string> Get, Action<CallLog, string> Set)> CallLogFieldAccessors =
			new Dictionary<string, (Func<CallLog, string>, Action<CallLog, string>)>
			{
				["calllogs.narrative"] = (l => l.Narrative, (l, v) => l.Narrative = v)
			};

		/// <summary>PersonnelCertifications text columns (parity-pinned; Data is the binary field).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<PersonnelCertification, string> Get, Action<PersonnelCertification, string> Set)> CertificationFieldAccessors =
			new Dictionary<string, (Func<PersonnelCertification, string>, Action<PersonnelCertification, string>)>
			{
				["personnelcertifications.name"] = (c => c.Name, (c, v) => c.Name = v),
				["personnelcertifications.number"] = (c => c.Number, (c, v) => c.Number = v),
				["personnelcertifications.type"] = (c => c.Type, (c, v) => c.Type = v),
				["personnelcertifications.area"] = (c => c.Area, (c, v) => c.Area = v),
				["personnelcertifications.issuedby"] = (c => c.IssuedBy, (c, v) => c.IssuedBy = v),
				["personnelcertifications.filename"] = (c => c.Filename, (c, v) => c.Filename = v)
			};

		/// <summary>The rgdpb binary certification document field id.</summary>
		public const string CertificationDataFieldId = "personnelcertifications.data";

		/// <summary>Contacts text columns (parity-pinned; Image is the separate binary field).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<Contact, string> Get, Action<Contact, string> Set)> ContactFieldAccessors =
			new Dictionary<string, (Func<Contact, string>, Action<Contact, string>)>
			{
				["contacts.firstname"] = (c => c.FirstName, (c, v) => c.FirstName = v),
				["contacts.middlename"] = (c => c.MiddleName, (c, v) => c.MiddleName = v),
				["contacts.lastname"] = (c => c.LastName, (c, v) => c.LastName = v),
				["contacts.othername"] = (c => c.OtherName, (c, v) => c.OtherName = v),
				["contacts.companyname"] = (c => c.CompanyName, (c, v) => c.CompanyName = v),
				["contacts.email"] = (c => c.Email, (c, v) => c.Email = v),
				["contacts.countryissuedidnumber"] = (c => c.CountryIssuedIdNumber, (c, v) => c.CountryIssuedIdNumber = v),
				["contacts.countryidname"] = (c => c.CountryIdName, (c, v) => c.CountryIdName = v),
				["contacts.stateidnumber"] = (c => c.StateIdNumber, (c, v) => c.StateIdNumber = v),
				["contacts.stateidname"] = (c => c.StateIdName, (c, v) => c.StateIdName = v),
				["contacts.stateidcountryname"] = (c => c.StateIdCountryName, (c, v) => c.StateIdCountryName = v),
				["contacts.homephonenumber"] = (c => c.HomePhoneNumber, (c, v) => c.HomePhoneNumber = v),
				["contacts.cellphonenumber"] = (c => c.CellPhoneNumber, (c, v) => c.CellPhoneNumber = v),
				["contacts.faxphonenumber"] = (c => c.FaxPhoneNumber, (c, v) => c.FaxPhoneNumber = v),
				["contacts.officephonenumber"] = (c => c.OfficePhoneNumber, (c, v) => c.OfficePhoneNumber = v),
				["contacts.description"] = (c => c.Description, (c, v) => c.Description = v),
				["contacts.otherinfo"] = (c => c.OtherInfo, (c, v) => c.OtherInfo = v),
				["contacts.locationgpscoordinates"] = (c => c.LocationGpsCoordinates, (c, v) => c.LocationGpsCoordinates = v),
				["contacts.entrancegpscoordinates"] = (c => c.EntranceGpsCoordinates, (c, v) => c.EntranceGpsCoordinates = v),
				["contacts.exitgpscoordinates"] = (c => c.ExitGpsCoordinates, (c, v) => c.ExitGpsCoordinates = v),
				["contacts.locationgeofence"] = (c => c.LocationGeofence, (c, v) => c.LocationGeofence = v)
			};

		/// <summary>The rgdpb binary contact image field id (stripped on reads, never served via v4).</summary>
		public const string ContactImageFieldId = "contacts.image";

		/// <summary>ContactNotes text columns (parity-pinned).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<ContactNote, string> Get, Action<ContactNote, string> Set)> ContactNoteFieldAccessors =
			new Dictionary<string, (Func<ContactNote, string>, Action<ContactNote, string>)>
			{
				["contactnotes.note"] = (n => n.Note, (n, v) => n.Note = v)
			};

		/// <summary>UnitStates text columns (catalog v2 operational family; parity-pinned).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<UnitState, string> Get, Action<UnitState, string> Set)> UnitStateFieldAccessors =
			new Dictionary<string, (Func<UnitState, string>, Action<UnitState, string>)>
			{
				["unitstates.note"] = (s => s.Note, (s, v) => s.Note = v),
				["unitstates.geolocationdata"] = (s => s.GeoLocationData, (s, v) => s.GeoLocationData = v)
			};

		/// <summary>UnitStates companion columns (parity-pinned).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<UnitState, string> GetEnvelope, Action<UnitState, decimal?> SetTyped)> UnitStateCompanionAccessors =
			new Dictionary<string, (Func<UnitState, string>, Action<UnitState, decimal?>)>
			{
				["unitstates.latitude"] = (s => s.ProtectedLatitudeEnvelope, (s, v) => s.Latitude = v),
				["unitstates.longitude"] = (s => s.ProtectedLongitudeEnvelope, (s, v) => s.Longitude = v)
			};

		/// <summary>DepartmentMemberSensitiveData text columns (catalog v1 personnel family; parity-pinned).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<DepartmentMemberSensitiveData, string> Get, Action<DepartmentMemberSensitiveData, string> Set)> MemberSensitiveDataAccessors =
			new Dictionary<string, (Func<DepartmentMemberSensitiveData, string>, Action<DepartmentMemberSensitiveData, string>)>
			{
				["departmentmembersensitivedata.identificationnumber"] = (d => d.IdentificationNumber, (d, v) => d.IdentificationNumber = v),
				["departmentmembersensitivedata.notes"] = (d => d.Notes, (d, v) => d.Notes = v),
				["departmentmembersensitivedata.homeaddress1"] = (d => d.HomeAddress1, (d, v) => d.HomeAddress1 = v),
				["departmentmembersensitivedata.homecity"] = (d => d.HomeCity, (d, v) => d.HomeCity = v),
				["departmentmembersensitivedata.homestate"] = (d => d.HomeState, (d, v) => d.HomeState = v),
				["departmentmembersensitivedata.homepostalcode"] = (d => d.HomePostalCode, (d, v) => d.HomePostalCode = v),
				["departmentmembersensitivedata.homecountry"] = (d => d.HomeCountry, (d, v) => d.HomeCountry = v),
				["departmentmembersensitivedata.mailingaddress1"] = (d => d.MailingAddress1, (d, v) => d.MailingAddress1 = v),
				["departmentmembersensitivedata.mailingcity"] = (d => d.MailingCity, (d, v) => d.MailingCity = v),
				["departmentmembersensitivedata.mailingstate"] = (d => d.MailingState, (d, v) => d.MailingState = v),
				["departmentmembersensitivedata.mailingpostalcode"] = (d => d.MailingPostalCode, (d, v) => d.MailingPostalCode = v),
				["departmentmembersensitivedata.mailingcountry"] = (d => d.MailingCountry, (d, v) => d.MailingCountry = v)
			};

		/// <summary>DepartmentMemberEmergencyContacts text columns (catalog v4; parity-pinned).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<DepartmentMemberEmergencyContact, string> Get, Action<DepartmentMemberEmergencyContact, string> Set)> MemberEmergencyContactAccessors =
			new Dictionary<string, (Func<DepartmentMemberEmergencyContact, string>, Action<DepartmentMemberEmergencyContact, string>)>
			{
				["departmentmemberemergencycontacts.name"] = (c => c.Name, (c, v) => c.Name = v),
				["departmentmemberemergencycontacts.relationship"] = (c => c.Relationship, (c, v) => c.Relationship = v),
				["departmentmemberemergencycontacts.phonenumber"] = (c => c.PhoneNumber, (c, v) => c.PhoneNumber = v),
				["departmentmemberemergencycontacts.alternatephonenumber"] = (c => c.AlternatePhoneNumber, (c, v) => c.AlternatePhoneNumber = v),
				["departmentmemberemergencycontacts.email"] = (c => c.Email, (c, v) => c.Email = v),
				["departmentmemberemergencycontacts.notes"] = (c => c.Notes, (c, v) => c.Notes = v)
			};

		/// <summary>Logs text columns (catalog v3; parity-pinned).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<Log, string> Get, Action<Log, string> Set)> LogFieldAccessors =
			new Dictionary<string, (Func<Log, string>, Action<Log, string>)>
			{
				["logs.narrative"] = (l => l.Narrative, (l, v) => l.Narrative = v),
				["logs.initialreport"] = (l => l.InitialReport, (l, v) => l.InitialReport = v),
				["logs.cause"] = (l => l.Cause, (l, v) => l.Cause = v),
				["logs.contactname"] = (l => l.ContactName, (l, v) => l.ContactName = v),
				["logs.contactnumber"] = (l => l.ContactNumber, (l, v) => l.ContactNumber = v),
				["logs.otherpersonnel"] = (l => l.OtherPersonnel, (l, v) => l.OtherPersonnel = v),
				["logs.location"] = (l => l.Location, (l, v) => l.Location = v),
				["logs.bodylocation"] = (l => l.BodyLocation, (l, v) => l.BodyLocation = v),
				["logs.pronounceddeceasedby"] = (l => l.PronouncedDeceasedBy, (l, v) => l.PronouncedDeceasedBy = v)
			};

		/// <summary>UdfFieldValues text column (catalog v2 operational family; parity-pinned).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<UdfFieldValue, string> Get, Action<UdfFieldValue, string> Set)> UdfFieldValueAccessors =
			new Dictionary<string, (Func<UdfFieldValue, string>, Action<UdfFieldValue, string>)>
			{
				["udffieldvalues.value"] = (v => v.Value, (v, x) => v.Value = x)
			};

		private static readonly byte[] BinaryPrefixBytes = Encoding.ASCII.GetBytes(ProtectedDataEnvelope.BinaryPrefix);

		/// <summary>One protected value wired to its reveal/redact actions on the owning entity.</summary>
		private sealed class Slot
		{
			public string FieldId;
			public string RowKey;
			public bool IsBinary;
			public string WireValue;
			public ProtectedReadResult Owner;
			public Action<string> Reveal;
			public Action Redact;
		}

		private readonly IDepartmentDataProtectionService _dataProtectionService;
		private readonly IProtectedDataGrantService _grantService;
		private readonly IProtectedDataBrokerClient _brokerClient;
		private readonly IProtectedFieldCatalog _fieldCatalog;

		public ProtectedReadService(IDepartmentDataProtectionService dataProtectionService,
			IProtectedDataGrantService grantService, IProtectedDataBrokerClient brokerClient,
			IProtectedFieldCatalog fieldCatalog)
		{
			_dataProtectionService = dataProtectionService;
			_grantService = grantService;
			_brokerClient = brokerClient;
			_fieldCatalog = fieldCatalog;
		}

		public async Task<ProtectedReadResult> ResolveForReadAsync(int departmentId, Call call,
			string grantToken, string userId, CancellationToken cancellationToken = default)
		{
			var results = await ResolveForReadAsync(departmentId,
				call == null ? Array.Empty<Call>() : new[] { call }, grantToken, userId, cancellationToken);
			return results.Count > 0 ? results[0] : new ProtectedReadResult { Call = null };
		}

		public async Task<IReadOnlyList<ProtectedReadResult>> ResolveForReadAsync(int departmentId,
			IReadOnlyList<Call> calls, string grantToken, string userId, CancellationToken cancellationToken = default)
		{
			calls ??= Array.Empty<Call>();
			var results = calls.Select(c => new ProtectedReadResult { Call = c }).ToList();
			if (results.Count == 0)
				return results;

			var slots = new List<Slot>();
			foreach (var result in results)
			{
				CollectCallSlots(result, slots);

				// Children ride the same batch when the controller populated them. The binary
				// attachment payload is deliberately excluded here — only file-serving endpoints
				// opt into it via ResolveAttachmentsForReadAsync(includeData: true).
				if (result.Call.CallNotes != null)
					foreach (var note in result.Call.CallNotes.Where(n => n != null))
						CollectNoteSlots(result, note, slots);

				if (result.Call.Attachments != null)
					foreach (var attachment in result.Call.Attachments.Where(a => a != null))
						CollectAttachmentSlots(result, attachment, slots, includeData: false);
			}

			await ResolveSlotsAsync(departmentId, grantToken, userId, results, slots, cancellationToken);
			return results;
		}

		public async Task<ProtectedReadResult> ResolveNotesForReadAsync(int departmentId,
			IReadOnlyList<CallNote> notes, string grantToken, string userId, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			var slots = new List<Slot>();
			foreach (var note in (notes ?? Array.Empty<CallNote>()).Where(n => n != null))
				CollectNoteSlots(result, note, slots);

			await ResolveSlotsAsync(departmentId, grantToken, userId, new List<ProtectedReadResult> { result }, slots, cancellationToken);
			return result;
		}

		public async Task<ProtectedReadResult> ResolveAttachmentsForReadAsync(int departmentId,
			IReadOnlyList<CallAttachment> attachments, string grantToken, string userId,
			bool includeData = false, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			var slots = new List<Slot>();
			foreach (var attachment in (attachments ?? Array.Empty<CallAttachment>()).Where(a => a != null))
				CollectAttachmentSlots(result, attachment, slots, includeData);

			await ResolveSlotsAsync(departmentId, grantToken, userId, new List<ProtectedReadResult> { result }, slots, cancellationToken);
			return result;
		}

		public async Task<ProtectedReadResult> ResolveCertificationsForReadAsync(int departmentId,
			IReadOnlyList<PersonnelCertification> certifications, string grantToken, string userId,
			bool includeData = false, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			var slots = new List<Slot>();
			foreach (var certification in (certifications ?? Array.Empty<PersonnelCertification>()).Where(c => c != null))
				CollectCertificationSlots(result, certification, slots, includeData);

			await ResolveSlotsAsync(departmentId, grantToken, userId, new List<ProtectedReadResult> { result }, slots, cancellationToken);
			return result;
		}

		public async Task<ProtectedReadResult> ResolveContactsForReadAsync(int departmentId,
			IReadOnlyList<Contact> contacts, string grantToken, string userId, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			var slots = new List<Slot>();
			foreach (var contact in (contacts ?? Array.Empty<Contact>()).Where(c => c != null))
				CollectContactSlots(result, contact, slots);

			await ResolveSlotsAsync(departmentId, grantToken, userId, new List<ProtectedReadResult> { result }, slots, cancellationToken);
			return result;
		}

		public async Task<ProtectedReadResult> ResolveMemberEmergencyContactsForReadAsync(int departmentId,
			IReadOnlyList<DepartmentMemberEmergencyContact> contacts, string grantToken, string userId,
			CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			var slots = new List<Slot>();
			foreach (var contact in (contacts ?? Array.Empty<DepartmentMemberEmergencyContact>()).Where(c => c != null))
			{
				var rowKey = contact.DepartmentMemberEmergencyContactId.ToString(CultureInfo.InvariantCulture);
				foreach (var accessor in MemberEmergencyContactAccessors)
				{
					var value = accessor.Value.Get(contact);
					if (!ProtectedDataEnvelope.HasEnvelopePrefix(value))
						continue;

					var set = accessor.Value.Set;
					slots.Add(new Slot
					{
						FieldId = accessor.Key,
						RowKey = rowKey,
						WireValue = value,
						Owner = result,
						Reveal = plaintext => set(contact, plaintext),
						Redact = () => set(contact, ProtectedDataEnvelope.RedactionValue)
					});
				}
			}

			await ResolveSlotsAsync(departmentId, grantToken, userId, new List<ProtectedReadResult> { result }, slots, cancellationToken);
			return result;
		}

		public async Task<ProtectedWriteResult> PrepareMemberEmergencyContactWriteAsync(int departmentId,
			DepartmentMemberEmergencyContact contact, string grantToken, string userId, bool workloadCaller,
			CancellationToken cancellationToken = default)
		{
			if (contact == null)
				return ProtectedWriteResult.Allowed();

			var slots = new List<WriteSlot>();
			var rowKey = contact.DepartmentMemberEmergencyContactId.ToString(CultureInfo.InvariantCulture);
			foreach (var accessor in MemberEmergencyContactAccessors)
			{
				var value = accessor.Value.Get(contact);
				if (string.IsNullOrEmpty(value) || ProtectedDataEnvelope.HasEnvelopePrefix(value) ||
					value == ProtectedDataEnvelope.RedactionValue)
					continue;

				var set = accessor.Value.Set;
				slots.Add(new WriteSlot { FieldId = accessor.Key, RowKey = rowKey, WireValue = value, Apply = envelope => set(contact, envelope) });
			}

			return await EncryptSlotsAsync(departmentId, grantToken, userId, workloadCaller, slots,
				() => contact.IsProtected = true, cancellationToken);
		}

		public async Task<ProtectedReadResult> ResolveMemberSensitiveDataForReadAsync(int departmentId,
			IReadOnlyList<DepartmentMemberSensitiveData> rows, string grantToken, string userId, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			var slots = new List<Slot>();
			foreach (var row in (rows ?? Array.Empty<DepartmentMemberSensitiveData>()).Where(r => r != null))
			{
				var rowKey = row.DepartmentMemberSensitiveDataId.ToString(CultureInfo.InvariantCulture);
				foreach (var accessor in MemberSensitiveDataAccessors)
				{
					var value = accessor.Value.Get(row);
					if (!ProtectedDataEnvelope.HasEnvelopePrefix(value))
						continue;

					var set = accessor.Value.Set;
					slots.Add(new Slot
					{
						FieldId = accessor.Key,
						RowKey = rowKey,
						WireValue = value,
						Owner = result,
						Reveal = plaintext => set(row, plaintext),
						Redact = () => set(row, ProtectedDataEnvelope.RedactionValue)
					});
				}
			}

			await ResolveSlotsAsync(departmentId, grantToken, userId, new List<ProtectedReadResult> { result }, slots, cancellationToken);
			return result;
		}

		public async Task<ProtectedWriteResult> PrepareMemberSensitiveDataWriteAsync(int departmentId,
			DepartmentMemberSensitiveData data, string grantToken, string userId, bool workloadCaller,
			CancellationToken cancellationToken = default)
		{
			if (data == null)
				return ProtectedWriteResult.Allowed();

			var slots = new List<WriteSlot>();
			var rowKey = data.DepartmentMemberSensitiveDataId.ToString(CultureInfo.InvariantCulture);
			foreach (var accessor in MemberSensitiveDataAccessors)
			{
				var value = accessor.Value.Get(data);
				if (string.IsNullOrEmpty(value) || ProtectedDataEnvelope.HasEnvelopePrefix(value) ||
					value == ProtectedDataEnvelope.RedactionValue)
					continue;

				var set = accessor.Value.Set;
				slots.Add(new WriteSlot { FieldId = accessor.Key, RowKey = rowKey, WireValue = value, Apply = envelope => set(data, envelope) });
			}

			return await EncryptSlotsAsync(departmentId, grantToken, userId, workloadCaller, slots,
				() => data.IsProtected = true, cancellationToken);
		}

		public async Task<ProtectedReadResult> ResolveCallLogsForReadAsync(int departmentId,
			IReadOnlyList<CallLog> logs, string grantToken, string userId, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			var slots = new List<Slot>();
			foreach (var log in (logs ?? Array.Empty<CallLog>()).Where(l => l != null))
			{
				var rowKey = log.CallLogId.ToString(CultureInfo.InvariantCulture);
				foreach (var accessor in CallLogFieldAccessors)
				{
					var value = accessor.Value.Get(log);
					if (!ProtectedDataEnvelope.HasEnvelopePrefix(value))
						continue;

					var set = accessor.Value.Set;
					slots.Add(new Slot
					{
						FieldId = accessor.Key,
						RowKey = rowKey,
						WireValue = value,
						Owner = result,
						Reveal = plaintext => set(log, plaintext),
						Redact = () => set(log, ProtectedDataEnvelope.RedactionValue)
					});
				}
			}

			await ResolveSlotsAsync(departmentId, grantToken, userId, new List<ProtectedReadResult> { result }, slots, cancellationToken);
			return result;
		}

		public async Task<ProtectedReadResult> ResolveLogsForReadAsync(int departmentId,
			IReadOnlyList<Log> logs, string grantToken, string userId, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			var slots = new List<Slot>();
			foreach (var log in (logs ?? Array.Empty<Log>()).Where(l => l != null))
			{
				var rowKey = log.LogId.ToString(CultureInfo.InvariantCulture);
				foreach (var accessor in LogFieldAccessors)
				{
					var value = accessor.Value.Get(log);
					if (!ProtectedDataEnvelope.HasEnvelopePrefix(value))
						continue;

					var set = accessor.Value.Set;
					slots.Add(new Slot
					{
						FieldId = accessor.Key,
						RowKey = rowKey,
						WireValue = value,
						Owner = result,
						Reveal = plaintext => set(log, plaintext),
						Redact = () => set(log, ProtectedDataEnvelope.RedactionValue)
					});
				}
			}

			await ResolveSlotsAsync(departmentId, grantToken, userId, new List<ProtectedReadResult> { result }, slots, cancellationToken);
			return result;
		}

		public async Task<ProtectedWriteResult> PrepareCallLogWriteAsync(int departmentId, CallLog log,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default)
		{
			if (log == null)
				return ProtectedWriteResult.Allowed();

			var slots = new List<WriteSlot>();
			var rowKey = log.CallLogId.ToString(CultureInfo.InvariantCulture);
			foreach (var accessor in CallLogFieldAccessors)
			{
				var value = accessor.Value.Get(log);
				if (string.IsNullOrEmpty(value) || ProtectedDataEnvelope.HasEnvelopePrefix(value) ||
					value == ProtectedDataEnvelope.RedactionValue)
					continue;

				var set = accessor.Value.Set;
				slots.Add(new WriteSlot { FieldId = accessor.Key, RowKey = rowKey, WireValue = value, Apply = envelope => set(log, envelope) });
			}

			// No marker column on CallLogs, so nothing to flag — the sweep and the residue counts
			// work off envelope detection on the column itself.
			return await EncryptSlotsAsync(departmentId, grantToken, userId, workloadCaller, slots, null, cancellationToken);
		}

		public async Task<ProtectedWriteResult> PrepareLogWriteAsync(int departmentId, Log log,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default)
		{
			if (log == null)
				return ProtectedWriteResult.Allowed();

			var slots = new List<WriteSlot>();
			var rowKey = log.LogId.ToString(CultureInfo.InvariantCulture);
			foreach (var accessor in LogFieldAccessors)
			{
				var value = accessor.Value.Get(log);
				if (string.IsNullOrEmpty(value) || ProtectedDataEnvelope.HasEnvelopePrefix(value) ||
					value == ProtectedDataEnvelope.RedactionValue)
					continue;

				var set = accessor.Value.Set;
				slots.Add(new WriteSlot { FieldId = accessor.Key, RowKey = rowKey, WireValue = value, Apply = envelope => set(log, envelope) });
			}

			return await EncryptSlotsAsync(departmentId, grantToken, userId, workloadCaller, slots, null, cancellationToken);
		}

		public async Task<ProtectedReadResult> ResolveUnitStatesForReadAsync(int departmentId,
			IReadOnlyList<UnitState> states, string grantToken, string userId, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			var slots = new List<Slot>();
			foreach (var state in (states ?? Array.Empty<UnitState>()).Where(s => s != null))
				CollectUnitStateSlots(result, state, slots);

			await ResolveSlotsAsync(departmentId, grantToken, userId, new List<ProtectedReadResult> { result }, slots, cancellationToken);
			return result;
		}

		public async Task<ProtectedReadResult> ResolveUdfFieldValuesForReadAsync(int departmentId,
			IReadOnlyList<UdfFieldValue> values, string grantToken, string userId, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			var slots = new List<Slot>();
			foreach (var value in (values ?? Array.Empty<UdfFieldValue>()).Where(v => v != null))
				CollectUdfFieldValueSlots(result, value, slots);

			await ResolveSlotsAsync(departmentId, grantToken, userId, new List<ProtectedReadResult> { result }, slots, cancellationToken);
			return result;
		}

		public async Task<ProtectedReadResult> ResolveContactNotesForReadAsync(int departmentId,
			IReadOnlyList<ContactNote> notes, string grantToken, string userId, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			var slots = new List<Slot>();
			foreach (var note in (notes ?? Array.Empty<ContactNote>()).Where(n => n != null))
			{
				var rowKey = note.ContactNoteId;
				foreach (var accessor in ContactNoteFieldAccessors)
				{
					var value = accessor.Value.Get(note);
					if (!ProtectedDataEnvelope.HasEnvelopePrefix(value))
						continue;

					var set = accessor.Value.Set;
					var target = note;
					slots.Add(new Slot
					{
						FieldId = accessor.Key,
						RowKey = rowKey,
						WireValue = value,
						Owner = result,
						Reveal = plaintext => set(target, plaintext),
						Redact = () => set(target, ProtectedDataEnvelope.RedactionValue)
					});
				}
			}

			await ResolveSlotsAsync(departmentId, grantToken, userId, new List<ProtectedReadResult> { result }, slots, cancellationToken);
			return result;
		}

		private static void CollectContactSlots(ProtectedReadResult owner, Contact contact, List<Slot> slots)
		{
			var rowKey = contact.ContactId;
			foreach (var accessor in ContactFieldAccessors)
			{
				var value = accessor.Value.Get(contact);
				if (!ProtectedDataEnvelope.HasEnvelopePrefix(value))
					continue;

				var set = accessor.Value.Set;
				var target = contact;
				slots.Add(new Slot
				{
					FieldId = accessor.Key,
					RowKey = rowKey,
					WireValue = value,
					Owner = owner,
					Reveal = plaintext => set(target, plaintext),
					Redact = () => set(target, ProtectedDataEnvelope.RedactionValue)
				});
			}

			// The enveloped image blob is stripped unconditionally: no v4 endpoint serves it, and
			// ciphertext bytes must never ride out through a serializer.
			if (IsBinaryEnveloped(contact.Image))
				contact.Image = null;
		}

		/// <summary>
		/// Clone of ONLY the cataloged call fields, taken before an edit overwrites them — the
		/// REDACTED-sentinel restore source for PrepareCallWriteAsync.
		/// </summary>
		public static Call SnapshotCatalogedCallFields(Call call)
		{
			var snapshot = new Call();
			foreach (var accessor in CallFieldAccessors)
				accessor.Value.Set(snapshot, accessor.Value.Get(call));
			return snapshot;
		}

		// ── protected writes (IProtectedWriteService) ────────────────────────────

		/// <summary>One plaintext value queued for broker encryption, with its apply-back action.</summary>
		private sealed class WriteSlot
		{
			public string FieldId;
			public string RowKey;
			public bool IsBinary;
			public string WireValue;
			public Action<string> Apply;
		}

		public async Task<ProtectedWriteResult> PreflightWriteAsync(int departmentId, string grantToken, string userId,
			bool workloadCaller, CancellationToken cancellationToken = default)
		{
			bool shouldEncrypt;
			try
			{
				shouldEncrypt = await _dataProtectionService.ShouldEncryptNewWritesAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Protection-state lookup failed for department {departmentId}; blocking the protected write defensively.");
				return ProtectedWriteResult.Blocked("broker_unavailable");
			}

			if (!shouldEncrypt)
				return ProtectedWriteResult.Allowed();

			if (workloadCaller)
				return ProtectedWriteResult.Allowed(isProtected: true);

			if (string.IsNullOrWhiteSpace(grantToken))
				return ProtectedWriteResult.Blocked("step_up_required");

			var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(departmentId);
			var outcome = _grantService.ValidateGrant(grantToken, departmentId, policy?.PolicyEpoch ?? 0,
				ProtectedDataGrantScopes.Write, out var grant);
			if (outcome != ProtectedDataGrantValidationOutcome.Valid)
				return ProtectedWriteResult.Blocked(outcome switch
				{
					ProtectedDataGrantValidationOutcome.Expired => "grant_expired",
					ProtectedDataGrantValidationOutcome.EpochRevoked => "grant_revoked",
					_ => "step_up_required"
				});
			if (!string.Equals(grant.UserId, userId, StringComparison.OrdinalIgnoreCase))
				return ProtectedWriteResult.Blocked("protected_access_denied");

			return ProtectedWriteResult.Allowed(isProtected: true);
		}

		public async Task<ProtectedWriteResult> PrepareCallWriteAsync(int departmentId, Call call, Call existingCall,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default)
		{
			if (call == null)
				return ProtectedWriteResult.Allowed();

			var slots = new List<WriteSlot>();
			var rowKey = call.CallId.ToString(CultureInfo.InvariantCulture);
			foreach (var accessor in CallFieldAccessors)
			{
				var value = accessor.Value.Get(call);

				// Round-tripped REDACTED sentinel on an edit means "unchanged": restore the stored
				// value (usually an envelope) instead of persisting the literal placeholder. Without
				// a stored row to restore from the sentinel is still never encrypted — enveloping the
				// placeholder would silently destroy the original.
				if (value == ProtectedDataEnvelope.RedactionValue)
				{
					if (existingCall != null)
						accessor.Value.Set(call, accessor.Value.Get(existingCall));
					continue;
				}

				if (string.IsNullOrEmpty(value) || ProtectedDataEnvelope.HasEnvelopePrefix(value))
					continue;

				var set = accessor.Value.Set;
				slots.Add(new WriteSlot
				{
					FieldId = accessor.Key,
					RowKey = rowKey,
					WireValue = value,
					Apply = envelope => set(call, envelope)
				});
			}

			return await EncryptSlotsAsync(departmentId, grantToken, userId, workloadCaller, slots, null, cancellationToken);
		}

		public async Task<ProtectedWriteResult> PrepareCallNoteWriteAsync(int departmentId, CallNote note,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default)
		{
			if (note == null)
				return ProtectedWriteResult.Allowed();

			var slots = new List<WriteSlot>();
			var rowKey = note.CallNoteId.ToString(CultureInfo.InvariantCulture);
			foreach (var accessor in NoteFieldAccessors)
			{
				var value = accessor.Value.Get(note);
				if (string.IsNullOrEmpty(value) || ProtectedDataEnvelope.HasEnvelopePrefix(value) ||
					value == ProtectedDataEnvelope.RedactionValue)
					continue;

				var set = accessor.Value.Set;
				slots.Add(new WriteSlot { FieldId = accessor.Key, RowKey = rowKey, WireValue = value, Apply = envelope => set(note, envelope) });
			}

			// Companion columns: the typed coordinate moves into its envelope column and the typed
			// column is nulled — the migration engine's exact write shape (plan 22.3).
			if (note.Latitude.HasValue)
				slots.Add(new WriteSlot
				{
					FieldId = "callnotes.latitude",
					RowKey = rowKey,
					WireValue = note.Latitude.Value.ToString(CultureInfo.InvariantCulture),
					Apply = envelope => { note.ProtectedLatitudeEnvelope = envelope; note.Latitude = null; }
				});
			if (note.Longitude.HasValue)
				slots.Add(new WriteSlot
				{
					FieldId = "callnotes.longitude",
					RowKey = rowKey,
					WireValue = note.Longitude.Value.ToString(CultureInfo.InvariantCulture),
					Apply = envelope => { note.ProtectedLongitudeEnvelope = envelope; note.Longitude = null; }
				});

			return await EncryptSlotsAsync(departmentId, grantToken, userId, workloadCaller, slots,
				() => note.IsProtected = true, cancellationToken);
		}

		public async Task<ProtectedWriteResult> PrepareCallAttachmentWriteAsync(int departmentId, CallAttachment attachment,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default)
		{
			if (attachment == null)
				return ProtectedWriteResult.Allowed();

			var slots = new List<WriteSlot>();
			var rowKey = attachment.CallAttachmentId.ToString(CultureInfo.InvariantCulture);
			foreach (var accessor in AttachmentFieldAccessors)
			{
				var value = accessor.Value.Get(attachment);
				if (string.IsNullOrEmpty(value) || ProtectedDataEnvelope.HasEnvelopePrefix(value) ||
					value == ProtectedDataEnvelope.RedactionValue)
					continue;

				var set = accessor.Value.Set;
				slots.Add(new WriteSlot { FieldId = accessor.Key, RowKey = rowKey, WireValue = value, Apply = envelope => set(attachment, envelope) });
			}

			if (attachment.Latitude.HasValue)
				slots.Add(new WriteSlot
				{
					FieldId = "callattachments.latitude",
					RowKey = rowKey,
					WireValue = attachment.Latitude.Value.ToString(CultureInfo.InvariantCulture),
					Apply = envelope => { attachment.ProtectedLatitudeEnvelope = envelope; attachment.Latitude = null; }
				});
			if (attachment.Longitude.HasValue)
				slots.Add(new WriteSlot
				{
					FieldId = "callattachments.longitude",
					RowKey = rowKey,
					WireValue = attachment.Longitude.Value.ToString(CultureInfo.InvariantCulture),
					Apply = envelope => { attachment.ProtectedLongitudeEnvelope = envelope; attachment.Longitude = null; }
				});

			if (attachment.Data != null && attachment.Data.Length > 0 && !IsBinaryEnveloped(attachment.Data))
				slots.Add(new WriteSlot
				{
					FieldId = AttachmentDataFieldId,
					RowKey = rowKey,
					IsBinary = true,
					WireValue = Convert.ToBase64String(attachment.Data),
					Apply = envelopeBase64 => attachment.Data = Convert.FromBase64String(envelopeBase64)
				});

			return await EncryptSlotsAsync(departmentId, grantToken, userId, workloadCaller, slots,
				() => attachment.IsProtected = true, cancellationToken);
		}

		public async Task<ProtectedWriteResult> PrepareCertificationWriteAsync(int departmentId,
			PersonnelCertification certification, PersonnelCertification existingCertification,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default)
		{
			if (certification == null)
				return ProtectedWriteResult.Allowed();

			var slots = new List<WriteSlot>();
			var restored = false;
			var rowKey = certification.PersonnelCertificationId.ToString(CultureInfo.InvariantCulture);
			foreach (var accessor in CertificationFieldAccessors)
			{
				var value = accessor.Value.Get(certification);

				// The sentinel is what an unrevealed form posts back. Restore the stored value rather
				// than encrypting the literal "REDACTED" over the member's real certification data.
				if (value == ProtectedDataEnvelope.RedactionValue)
				{
					if (existingCertification != null)
					{
						accessor.Value.Set(certification, accessor.Value.Get(existingCertification));
						restored = true;
					}

					continue;
				}

				if (string.IsNullOrEmpty(value) || ProtectedDataEnvelope.HasEnvelopePrefix(value))
					continue;

				var set = accessor.Value.Set;
				slots.Add(new WriteSlot { FieldId = accessor.Key, RowKey = rowKey, WireValue = value, Apply = envelope => set(certification, envelope) });
			}

			// A null document on an edit means "no new file was uploaded", not "delete the file" —
			// keep whatever is stored, which for a protected department is already an envelope.
			if ((certification.Data == null || certification.Data.Length == 0) && existingCertification != null)
				certification.Data = existingCertification.Data;

			if (certification.Data != null && certification.Data.Length > 0 && !IsBinaryEnveloped(certification.Data))
				slots.Add(new WriteSlot
				{
					FieldId = CertificationDataFieldId,
					RowKey = rowKey,
					IsBinary = true,
					WireValue = Convert.ToBase64String(certification.Data),
					Apply = envelopeBase64 => certification.Data = Convert.FromBase64String(envelopeBase64)
				});

			var result = await EncryptSlotsAsync(departmentId, grantToken, userId, workloadCaller, slots,
				() => certification.IsProtected = true, cancellationToken);

			// A restore mutates the entity without producing a broker slot, so EncryptSlotsAsync has
			// nothing to report and Changed stays false. The caller re-persists only on Changed, so
			// without this the transiently-saved row keeps the literal REDACTED placeholder and the
			// member's certification number and file name are gone. Reported through the result
			// rather than left to each caller to special-case, the way calls and contacts do.
			if (restored && result.Success && !result.Changed)
				result.Changed = true;

			return result;
		}

		public async Task<ProtectedWriteResult> PrepareContactWriteAsync(int departmentId, Contact contact, Contact existingContact,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default)
		{
			if (contact == null)
				return ProtectedWriteResult.Allowed();

			var slots = new List<WriteSlot>();
			var rowKey = contact.ContactId;
			foreach (var accessor in ContactFieldAccessors)
			{
				var value = accessor.Value.Get(contact);

				if (value == ProtectedDataEnvelope.RedactionValue)
				{
					if (existingContact != null)
						accessor.Value.Set(contact, accessor.Value.Get(existingContact));
					continue;
				}

				if (string.IsNullOrEmpty(value) || ProtectedDataEnvelope.HasEnvelopePrefix(value))
					continue;

				var set = accessor.Value.Set;
				slots.Add(new WriteSlot { FieldId = accessor.Key, RowKey = rowKey, WireValue = value, Apply = envelope => set(contact, envelope) });
			}

			if (contact.Image != null && contact.Image.Length > 0 && !IsBinaryEnveloped(contact.Image))
				slots.Add(new WriteSlot
				{
					FieldId = ContactImageFieldId,
					RowKey = rowKey,
					IsBinary = true,
					WireValue = Convert.ToBase64String(contact.Image),
					Apply = envelopeBase64 => contact.Image = Convert.FromBase64String(envelopeBase64)
				});

			return await EncryptSlotsAsync(departmentId, grantToken, userId, workloadCaller, slots, null, cancellationToken);
		}

		public async Task<ProtectedWriteResult> PrepareContactNoteWriteAsync(int departmentId, ContactNote note,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default)
		{
			if (note == null)
				return ProtectedWriteResult.Allowed();

			var slots = new List<WriteSlot>();
			var rowKey = note.ContactNoteId;
			foreach (var accessor in ContactNoteFieldAccessors)
			{
				var value = accessor.Value.Get(note);
				if (string.IsNullOrEmpty(value) || ProtectedDataEnvelope.HasEnvelopePrefix(value) ||
					value == ProtectedDataEnvelope.RedactionValue)
					continue;

				var set = accessor.Value.Set;
				slots.Add(new WriteSlot { FieldId = accessor.Key, RowKey = rowKey, WireValue = value, Apply = envelope => set(note, envelope) });
			}

			return await EncryptSlotsAsync(departmentId, grantToken, userId, workloadCaller, slots, null, cancellationToken);
		}

		public async Task<ProtectedWriteResult> PrepareUnitStateWriteAsync(int departmentId, UnitState state,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default)
		{
			if (state == null)
				return ProtectedWriteResult.Allowed();

			var slots = new List<WriteSlot>();
			var rowKey = state.UnitStateId.ToString(CultureInfo.InvariantCulture);
			foreach (var accessor in UnitStateFieldAccessors)
			{
				var value = accessor.Value.Get(state);
				if (string.IsNullOrEmpty(value) || ProtectedDataEnvelope.HasEnvelopePrefix(value) ||
					value == ProtectedDataEnvelope.RedactionValue)
					continue;

				var set = accessor.Value.Set;
				slots.Add(new WriteSlot { FieldId = accessor.Key, RowKey = rowKey, WireValue = value, Apply = envelope => set(state, envelope) });
			}

			// Companion columns: the typed coordinate moves into its envelope column and the typed
			// column is nulled — the migration engine's exact write shape (plan 22.3).
			if (state.Latitude.HasValue)
				slots.Add(new WriteSlot
				{
					FieldId = "unitstates.latitude",
					RowKey = rowKey,
					WireValue = state.Latitude.Value.ToString(CultureInfo.InvariantCulture),
					Apply = envelope => { state.ProtectedLatitudeEnvelope = envelope; state.Latitude = null; }
				});
			if (state.Longitude.HasValue)
				slots.Add(new WriteSlot
				{
					FieldId = "unitstates.longitude",
					RowKey = rowKey,
					WireValue = state.Longitude.Value.ToString(CultureInfo.InvariantCulture),
					Apply = envelope => { state.ProtectedLongitudeEnvelope = envelope; state.Longitude = null; }
				});

			return await EncryptSlotsAsync(departmentId, grantToken, userId, workloadCaller, slots,
				() => state.IsProtected = true, cancellationToken);
		}

		public async Task<ProtectedWriteResult> PrepareUdfFieldValueWriteAsync(int departmentId, UdfFieldValue value,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default)
		{
			if (value == null)
				return ProtectedWriteResult.Allowed();

			var slots = new List<WriteSlot>();
			foreach (var accessor in UdfFieldValueAccessors)
			{
				var stored = accessor.Value.Get(value);
				if (string.IsNullOrEmpty(stored) || ProtectedDataEnvelope.HasEnvelopePrefix(stored) ||
					stored == ProtectedDataEnvelope.RedactionValue)
					continue;

				var set = accessor.Value.Set;
				slots.Add(new WriteSlot
				{
					FieldId = accessor.Key,
					RowKey = value.UdfFieldValueId,
					WireValue = stored,
					Apply = envelope => set(value, envelope)
				});
			}

			return await EncryptSlotsAsync(departmentId, grantToken, userId, workloadCaller, slots, null, cancellationToken);
		}

		/// <summary>
		/// REDACTED-sentinel restore source for PrepareContactWriteAsync (mirrors
		/// SnapshotCatalogedCallFields for the MVC contact edit surface).
		/// </summary>
		public static Contact SnapshotCatalogedContactFields(Contact contact)
		{
			var snapshot = new Contact();
			foreach (var accessor in ContactFieldAccessors)
				accessor.Value.Set(snapshot, accessor.Value.Get(contact));
			return snapshot;
		}

		/// <summary>
		/// Shared write core: enforcement check, attended-grant gate, ONE broker encrypt batch, and
		/// ALL-OR-NOTHING application — any failure blocks the write; plaintext never persists in a
		/// protected department's rows.
		/// </summary>
		private async Task<ProtectedWriteResult> EncryptSlotsAsync(int departmentId, string grantToken, string userId,
			bool workloadCaller, List<WriteSlot> slots, Action markProtected, CancellationToken cancellationToken)
		{
			bool shouldEncrypt;
			try
			{
				shouldEncrypt = await _dataProtectionService.ShouldEncryptNewWritesAsync(departmentId);
			}
			catch (Exception ex)
			{
				// Unknown protection state on a WRITE fails closed: persisting plaintext into a
				// possibly-protected department is the one unrecoverable direction.
				Logging.LogException(ex, $"Protection-state lookup failed for department {departmentId}; blocking the protected write defensively.");
				return ProtectedWriteResult.Blocked("broker_unavailable");
			}

			if (!shouldEncrypt)
				return ProtectedWriteResult.Allowed();

			if (slots.Count == 0)
			{
				markProtected?.Invoke();
				return ProtectedWriteResult.Allowed(isProtected: true);
			}

			// Attended callers need a currently-valid grant (RequireStepUpForProtectedWrites, plan
			// 3.3). Workload callers use the broker's encrypt-only lane — no grant, no disclosure.
			if (!workloadCaller)
			{
				var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(departmentId);
				var outcome = _grantService.ValidateGrant(grantToken, departmentId, policy?.PolicyEpoch ?? 0,
					ProtectedDataGrantScopes.Write, out var grant);
				if (string.IsNullOrWhiteSpace(grantToken))
					return ProtectedWriteResult.Blocked("step_up_required");
				if (outcome != ProtectedDataGrantValidationOutcome.Valid)
					return ProtectedWriteResult.Blocked(outcome switch
					{
						ProtectedDataGrantValidationOutcome.Expired => "grant_expired",
						ProtectedDataGrantValidationOutcome.EpochRevoked => "grant_revoked",
						_ => "step_up_required"
					});
				if (!string.Equals(grant.UserId, userId, StringComparison.OrdinalIgnoreCase))
					return ProtectedWriteResult.Blocked("protected_access_denied");
			}

			var policyRow = await _dataProtectionService.GetPolicyByDepartmentIdAsync(departmentId);
			var catalogVersion = policyRow?.CatalogVersion ?? 0;

			// The department's PINNED catalog version decides what it owns. A field added in a later
			// catalog is not encrypted until a catalog upgrade sweeps that department: encrypting it
			// early would write an envelope no migration ever accounted for, under this department's
			// older AAD. Skipped fields stay plaintext and are picked up by the upgrade.
			var ownedFieldIds = new HashSet<string>(
				_fieldCatalog.GetAllForVersion(catalogVersion).Select(e => e.FieldId),
				StringComparer.OrdinalIgnoreCase);
			var skipped = slots.Where(x => !ownedFieldIds.Contains(x.FieldId)).ToList();
			if (skipped.Count > 0)
			{
				Logging.LogInfo($"ADP: department {departmentId} is pinned at catalog v{catalogVersion}; skipping {skipped.Count} field(s) added in a later catalog until an upgrade runs.");
				slots = slots.Where(x => ownedFieldIds.Contains(x.FieldId)).ToList();

				if (slots.Count == 0)
				{
					markProtected?.Invoke();
					return ProtectedWriteResult.Allowed(isProtected: true);
				}
			}

			var items = slots.Select(s => new ProtectedFieldOperationItem
			{
				FieldId = s.FieldId,
				RowKey = s.RowKey,
				Value = s.WireValue,
				IsBinary = s.IsBinary,
				CatalogVersion = catalogVersion
			}).ToList();

			ProtectedDataBrokerResult brokerResult;
			try
			{
				brokerResult = await _brokerClient.EncryptAsync(departmentId, workloadCaller ? null : grantToken,
					Guid.NewGuid().ToString("N"), items, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Protected write broker call failed for department {departmentId}; blocking the write.");
				brokerResult = null;
			}

			if (brokerResult == null || !brokerResult.Success)
				return ProtectedWriteResult.Blocked(brokerResult?.ErrorCode switch
				{
					"grant_expired" => "grant_expired",
					"grant_revoked" => "grant_revoked",
					"grant_invalid" => "step_up_required",
					_ => "broker_unavailable"
				});

			var encrypted = brokerResult.Items
				.Where(i => i != null && i.FieldId != null && i.RowKey != null)
				.GroupBy(i => (i.RowKey, i.FieldId))
				.ToDictionary(g => g.Key, g => g.First());

			// ALL items must have encrypted cleanly before ANY is applied.
			foreach (var slot in slots)
			{
				if (!encrypted.TryGetValue((slot.RowKey, slot.FieldId), out var item) ||
					item.ErrorCode != null || string.IsNullOrEmpty(item.Value))
					return ProtectedWriteResult.Blocked("broker_unavailable");
			}

			foreach (var slot in slots)
				slot.Apply(encrypted[(slot.RowKey, slot.FieldId)].Value);

			markProtected?.Invoke();
			return ProtectedWriteResult.Allowed(isProtected: true, changed: true);
		}

		/// <summary>True when the blob starts with the rgdpb envelope prefix (format check only).</summary>
		public static bool IsBinaryEnveloped(byte[] value)
		{
			if (value == null || value.Length < BinaryPrefixBytes.Length)
				return false;

			for (var i = 0; i < BinaryPrefixBytes.Length; i++)
			{
				if (value[i] != BinaryPrefixBytes[i])
					return false;
			}

			return true;
		}

		// ── slot collection ──────────────────────────────────────────────────────

		private static void CollectCallSlots(ProtectedReadResult owner, List<Slot> slots)
		{
			var call = owner.Call;
			var rowKey = call.CallId.ToString(CultureInfo.InvariantCulture);
			foreach (var accessor in CallFieldAccessors)
			{
				var value = accessor.Value.Get(call);
				if (!ProtectedDataEnvelope.HasEnvelopePrefix(value))
					continue;

				var set = accessor.Value.Set;
				slots.Add(new Slot
				{
					FieldId = accessor.Key,
					RowKey = rowKey,
					WireValue = value,
					Owner = owner,
					Reveal = plaintext => set(call, plaintext),
					Redact = () => set(call, ProtectedDataEnvelope.RedactionValue)
				});
			}
		}

		private static void CollectUnitStateSlots(ProtectedReadResult owner, UnitState state, List<Slot> slots)
		{
			var rowKey = state.UnitStateId.ToString(CultureInfo.InvariantCulture);
			foreach (var accessor in UnitStateFieldAccessors)
			{
				var value = accessor.Value.Get(state);
				if (!ProtectedDataEnvelope.HasEnvelopePrefix(value))
					continue;

				var set = accessor.Value.Set;
				slots.Add(new Slot
				{
					FieldId = accessor.Key,
					RowKey = rowKey,
					WireValue = value,
					Owner = owner,
					Reveal = plaintext => set(state, plaintext),
					Redact = () => set(state, ProtectedDataEnvelope.RedactionValue)
				});
			}

			foreach (var accessor in UnitStateCompanionAccessors)
			{
				var envelope = accessor.Value.GetEnvelope(state);
				if (!ProtectedDataEnvelope.HasEnvelopePrefix(envelope))
					continue;

				var setTyped = accessor.Value.SetTyped;
				slots.Add(new Slot
				{
					FieldId = accessor.Key,
					RowKey = rowKey,
					WireValue = envelope,
					Owner = owner,
					Reveal = plaintext => setTyped(state,
						decimal.TryParse(plaintext, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null),
					Redact = () => setTyped(state, null)
				});
			}
		}

		private static void CollectUdfFieldValueSlots(ProtectedReadResult owner, UdfFieldValue value, List<Slot> slots)
		{
			var rowKey = value.UdfFieldValueId;
			foreach (var accessor in UdfFieldValueAccessors)
			{
				var stored = accessor.Value.Get(value);
				if (!ProtectedDataEnvelope.HasEnvelopePrefix(stored))
					continue;

				var set = accessor.Value.Set;
				slots.Add(new Slot
				{
					FieldId = accessor.Key,
					RowKey = rowKey,
					WireValue = stored,
					Owner = owner,
					Reveal = plaintext => set(value, plaintext),
					Redact = () => set(value, ProtectedDataEnvelope.RedactionValue)
				});
			}
		}

		private static void CollectNoteSlots(ProtectedReadResult owner, CallNote note, List<Slot> slots)
		{
			var rowKey = note.CallNoteId.ToString(CultureInfo.InvariantCulture);
			foreach (var accessor in NoteFieldAccessors)
			{
				var value = accessor.Value.Get(note);
				if (!ProtectedDataEnvelope.HasEnvelopePrefix(value))
					continue;

				var set = accessor.Value.Set;
				slots.Add(new Slot
				{
					FieldId = accessor.Key,
					RowKey = rowKey,
					WireValue = value,
					Owner = owner,
					Reveal = plaintext => set(note, plaintext),
					Redact = () => set(note, ProtectedDataEnvelope.RedactionValue)
				});
			}

			foreach (var accessor in NoteCompanionAccessors)
			{
				var envelope = accessor.Value.GetEnvelope(note);
				if (!ProtectedDataEnvelope.HasEnvelopePrefix(envelope))
					continue;

				var setTyped = accessor.Value.SetTyped;
				slots.Add(new Slot
				{
					FieldId = accessor.Key,
					RowKey = rowKey,
					WireValue = envelope,
					Owner = owner,
					// Companion reveal: the envelope held the invariant string of the typed value;
					// an unparseable payload stays concealed (typed column remains null).
					Reveal = plaintext => setTyped(note,
						decimal.TryParse(plaintext, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null),
					Redact = () => setTyped(note, null)
				});
			}
		}

		private static void CollectAttachmentSlots(ProtectedReadResult owner, CallAttachment attachment,
			List<Slot> slots, bool includeData)
		{
			var rowKey = attachment.CallAttachmentId.ToString(CultureInfo.InvariantCulture);
			foreach (var accessor in AttachmentFieldAccessors)
			{
				var value = accessor.Value.Get(attachment);
				if (!ProtectedDataEnvelope.HasEnvelopePrefix(value))
					continue;

				var set = accessor.Value.Set;
				slots.Add(new Slot
				{
					FieldId = accessor.Key,
					RowKey = rowKey,
					WireValue = value,
					Owner = owner,
					Reveal = plaintext => set(attachment, plaintext),
					Redact = () => set(attachment, ProtectedDataEnvelope.RedactionValue)
				});
			}

			foreach (var accessor in AttachmentCompanionAccessors)
			{
				var envelope = accessor.Value.GetEnvelope(attachment);
				if (!ProtectedDataEnvelope.HasEnvelopePrefix(envelope))
					continue;

				var setTyped = accessor.Value.SetTyped;
				slots.Add(new Slot
				{
					FieldId = accessor.Key,
					RowKey = rowKey,
					WireValue = envelope,
					Owner = owner,
					Reveal = plaintext => setTyped(attachment,
						decimal.TryParse(plaintext, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null),
					Redact = () => setTyped(attachment, null)
				});
			}

			if (includeData && IsBinaryEnveloped(attachment.Data))
			{
				slots.Add(new Slot
				{
					FieldId = AttachmentDataFieldId,
					RowKey = rowKey,
					IsBinary = true,
					WireValue = Convert.ToBase64String(attachment.Data),
					Owner = owner,
					Reveal = base64 => attachment.Data = Convert.FromBase64String(base64),
					// A concealed binary payload is NULL — ciphertext bytes are never served.
					Redact = () => attachment.Data = null
				});
			}
			else if (!includeData && IsBinaryEnveloped(attachment.Data))
			{
				// Metadata-only resolution: strip the ciphertext bytes so a serializer can never
				// carry them out; the file endpoints re-fetch and opt into decryption.
				attachment.Data = null;
			}
		}

		private static void CollectCertificationSlots(ProtectedReadResult owner, PersonnelCertification certification,
			List<Slot> slots, bool includeData)
		{
			var rowKey = certification.PersonnelCertificationId.ToString(CultureInfo.InvariantCulture);
			foreach (var accessor in CertificationFieldAccessors)
			{
				var value = accessor.Value.Get(certification);
				if (!ProtectedDataEnvelope.HasEnvelopePrefix(value))
					continue;

				var set = accessor.Value.Set;
				slots.Add(new Slot
				{
					FieldId = accessor.Key,
					RowKey = rowKey,
					WireValue = value,
					Owner = owner,
					Reveal = plaintext => set(certification, plaintext),
					Redact = () => set(certification, ProtectedDataEnvelope.RedactionValue)
				});
			}

			if (includeData && IsBinaryEnveloped(certification.Data))
			{
				slots.Add(new Slot
				{
					FieldId = CertificationDataFieldId,
					RowKey = rowKey,
					IsBinary = true,
					WireValue = Convert.ToBase64String(certification.Data),
					Owner = owner,
					Reveal = base64 => certification.Data = Convert.FromBase64String(base64),
					// A concealed document is NULL — ciphertext bytes are never served.
					Redact = () => certification.Data = null
				});
			}
			else if (!includeData && IsBinaryEnveloped(certification.Data))
			{
				// Metadata-only resolution: strip the ciphertext so a serializer cannot carry it out;
				// the download endpoint re-fetches and opts into decryption.
				certification.Data = null;
			}
		}

		// ── shared resolution core ───────────────────────────────────────────────

		private async Task ResolveSlotsAsync(int departmentId, string grantToken, string userId,
			List<ProtectedReadResult> results, List<Slot> slots, CancellationToken cancellationToken)
		{
			bool enforced;
			try
			{
				enforced = await _dataProtectionService.IsProtectionEnforcedAsync(departmentId);
			}
			catch (Exception ex)
			{
				// Unknown protection state must not leak: treat as enforced with no grant.
				Logging.LogException(ex, $"Protection-state lookup failed for department {departmentId}; redacting protected reads defensively.");
				RedactSlots(slots, "protected_access_denied");
				foreach (var result in results)
					result.IsProtected = true;
				return;
			}

			if (!enforced)
				return;

			foreach (var result in results)
				result.IsProtected = true;

			if (slots.Count == 0)
				return;

			var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(departmentId);
			var currentEpoch = policy?.PolicyEpoch ?? 0;
			var catalogVersion = policy?.CatalogVersion ?? 0;

			// One grant validation per batch, bound to this user and department at the current
			// policy epoch. Anything but Valid redacts with a machine-readable reason the clients
			// map onto the step-up flow.
			string redactionReason;
			if (string.IsNullOrWhiteSpace(grantToken))
			{
				redactionReason = "step_up_required";
			}
			else
			{
				var outcome = _grantService.ValidateGrant(grantToken, departmentId, currentEpoch,
					ProtectedDataGrantScopes.Read, out var grant);
				redactionReason = outcome switch
				{
					ProtectedDataGrantValidationOutcome.Valid when
						string.Equals(grant.UserId, userId, StringComparison.OrdinalIgnoreCase) => null,
					ProtectedDataGrantValidationOutcome.Valid => "protected_access_denied",
					ProtectedDataGrantValidationOutcome.Expired => "grant_expired",
					ProtectedDataGrantValidationOutcome.EpochRevoked => "grant_revoked",
					_ => "step_up_required"
				};
			}

			if (redactionReason != null)
			{
				RedactSlots(slots, redactionReason);
				return;
			}

			var items = slots.Select(s => new ProtectedFieldOperationItem
			{
				FieldId = s.FieldId,
				RowKey = s.RowKey,
				Value = s.WireValue,
				IsBinary = s.IsBinary,
				CatalogVersion = catalogVersion
			}).ToList();

			ProtectedDataBrokerResult brokerResult;
			try
			{
				brokerResult = await _brokerClient.DecryptAsync(departmentId, grantToken,
					Guid.NewGuid().ToString("N"), items, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Protected read broker call failed for department {departmentId}; redacting.");
				brokerResult = null;
			}

			if (brokerResult == null || !brokerResult.Success)
			{
				RedactSlots(slots, brokerResult?.ErrorCode switch
				{
					"grant_expired" => "grant_expired",
					"grant_revoked" => "grant_revoked",
					"grant_invalid" => "step_up_required",
					_ => "broker_unavailable"
				});
				return;
			}

			var decrypted = brokerResult.Items
				.Where(i => i != null && i.FieldId != null && i.RowKey != null)
				.GroupBy(i => (i.RowKey, i.FieldId))
				.ToDictionary(g => g.Key, g => g.First());

			foreach (var slot in slots)
			{
				if (decrypted.TryGetValue((slot.RowKey, slot.FieldId), out var item) &&
					item.ErrorCode == null && item.Value != null)
				{
					try
					{
						slot.Reveal(item.Value);
						continue;
					}
					catch (FormatException)
					{
						// Fall through to redaction: a malformed reveal payload stays concealed.
					}
				}

				// Per-item broker fault (corrupt envelope, unknown key version): that one field
				// stays concealed; the rest of the batch reads normally.
				slot.Redact();
				RecordRedaction(slot, "broker_unavailable");
			}
		}

		/// <summary>Redacts every slot and records the reason on each affected result.</summary>
		private static void RedactSlots(List<Slot> slots, string reason)
		{
			foreach (var slot in slots)
			{
				slot.Redact();
				RecordRedaction(slot, reason);
			}
		}

		private static void RecordRedaction(Slot slot, string reason)
		{
			if (!slot.Owner.RedactedFields.Contains(slot.FieldId))
				slot.Owner.RedactedFields.Add(slot.FieldId);
			slot.Owner.ProtectedReason ??= reason;
		}
	}
}
