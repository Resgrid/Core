using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Catalog v2 (draft until the Phase 0 catalog freeze): the P0 families from ADP plan section 5.1
	/// — calls, call children, department-scoped personnel data, and contacts. FieldIds are stable
	/// forever (they are AAD components); entries are only ever ADDED, with the catalog version
	/// incremented. Section 5.2/5.3 operational, moderation, and section 22.1 audit families land in
	/// later versions before the freeze. Linked Address rows are deliberately absent until the
	/// shared-Address ownership migration exists (section 5.1).
	/// </summary>
	public class ProtectedFieldCatalog : IProtectedFieldCatalog
	{
		private const string CallsFamily = "Calls";
		private const string PersonnelFamily = "Personnel";
		private const string ContactsFamily = "Contacts";
		private const string OperationalFamily = "Operational";
		private const string MessagingFamily = "Messaging";
		private const string ModerationFamily = "Moderation";
		private const string DocumentsFamily = "Documents";
		private const string CredentialsFamily = "Credentials";

		/// <summary>Catalog version the section 5.2 operational entries were added in.</summary>
		private const int OperationalCatalogVersion = 2;

		/// <summary>Catalog version the section 5.2 Log (incident report) family was added in.</summary>
		private const int LogCatalogVersion = 3;

		/// <summary>Catalog version the department-scoped emergency-contact family was added in.</summary>
		private const int EmergencyContactCatalogVersion = 4;

		/// <summary>Catalog version the department-scoped member address columns were added in.</summary>
		private const int MemberAddressCatalogVersion = 5;

		/// <summary>Catalog version the personnel certification family was added in.</summary>
		private const int CertificationCatalogVersion = 6;

		/// <summary>Catalog version the member-messaging family was added in.</summary>
		private const int MessagingCatalogVersion = 7;

		/// <summary>Catalog version the moderation family was added in.</summary>
		private const int ModerationCatalogVersion = 8;

		/// <summary>
		/// Catalog version the plan's remaining candidates were added in: unit logs, user state
		/// notes, calendar items, documents and the stored mailbox credentials.
		/// </summary>
		private const int RemainingCandidatesCatalogVersion = 9;

		private static readonly IReadOnlyList<ProtectedFieldDefinition> Entries = BuildV1();
		private static readonly Dictionary<string, ProtectedFieldDefinition> ById =
			Entries.ToDictionary(e => e.FieldId, StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<string, IReadOnlyList<ProtectedFieldDefinition>> ByTable =
			Entries.GroupBy(e => e.TableName, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => (IReadOnlyList<ProtectedFieldDefinition>)g.ToList(), StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Current catalog version. MUST equal the highest AddedInCatalogVersion in the entries —
		/// version-scoped queries and the upgrade work list are meaningless if the constant lags the
		/// data, so it is derived rather than hand-maintained.
		/// </summary>
		public int Version { get; } = Entries.Max(e => e.AddedInCatalogVersion);

		public IReadOnlyList<ProtectedFieldDefinition> GetAll() => Entries;

		public IReadOnlyList<ProtectedFieldDefinition> GetAllForVersion(int catalogVersion)
		{
			if (catalogVersion <= 0)
				return Array.Empty<ProtectedFieldDefinition>();

			return Entries.Where(e => e.AddedInCatalogVersion <= catalogVersion).ToList();
		}

		public IReadOnlyList<ProtectedFieldDefinition> GetForTable(string tableName)
		{
			if (string.IsNullOrWhiteSpace(tableName))
				return Array.Empty<ProtectedFieldDefinition>();

			return ByTable.TryGetValue(tableName, out var entries) ? entries : Array.Empty<ProtectedFieldDefinition>();
		}

		public IReadOnlyList<ProtectedFieldDefinition> GetForTableAndVersion(string tableName, int catalogVersion)
		{
			if (catalogVersion <= 0)
				return Array.Empty<ProtectedFieldDefinition>();

			return GetForTable(tableName).Where(e => e.AddedInCatalogVersion <= catalogVersion).ToList();
		}

		public IReadOnlyList<ProtectedFieldDefinition> GetAddedBetween(int fromCatalogVersion, int toCatalogVersion)
		{
			if (toCatalogVersion <= fromCatalogVersion)
				return Array.Empty<ProtectedFieldDefinition>();

			return Entries
				.Where(e => e.AddedInCatalogVersion > fromCatalogVersion && e.AddedInCatalogVersion <= toCatalogVersion)
				.ToList();
		}

		public ProtectedFieldDefinition GetById(string fieldId)
		{
			if (string.IsNullOrWhiteSpace(fieldId))
				return null;

			return ById.TryGetValue(fieldId, out var entry) ? entry : null;
		}

		public bool IsProtectedField(string tableName, string columnName)
		{
			if (string.IsNullOrWhiteSpace(columnName))
				return false;

			return GetForTable(tableName).Any(e => string.Equals(e.ColumnName, columnName, StringComparison.OrdinalIgnoreCase));
		}

		private static IReadOnlyList<ProtectedFieldDefinition> BuildV1()
		{
			var list = new List<ProtectedFieldDefinition>();

			// ---- Calls (section 5.1). User-authored "number/type/name" values are protected even
			// though their labels look structural; the system-generated Calls.Number stays plaintext.
			void Call(string column, ProtectedFieldClassification classification, ProtectedFieldStorageKind kind = ProtectedFieldStorageKind.Text) =>
				list.Add(new ProtectedFieldDefinition($"calls.{column.ToLowerInvariant()}", CallsFamily, "Calls", column,
					kind, classification, PermissionTypes.ViewProtectedCallData, PermissionTypes.EditProtectedCallData));

			Call("Name", ProtectedFieldClassification.Sensitive);
			Call("Type", ProtectedFieldClassification.Sensitive);
			Call("NatureOfCall", ProtectedFieldClassification.Phi);
			Call("Notes", ProtectedFieldClassification.Phi);
			Call("CompletedNotes", ProtectedFieldClassification.Phi);
			Call("Address", ProtectedFieldClassification.Pii);
			Call("GeoLocationData", ProtectedFieldClassification.Pii);
			Call("W3W", ProtectedFieldClassification.Pii);
			Call("ContactName", ProtectedFieldClassification.Pii);
			Call("ContactNumber", ProtectedFieldClassification.Pii);
			Call("SourceIdentifier", ProtectedFieldClassification.Sensitive);
			Call("IncidentNumber", ProtectedFieldClassification.Sensitive);
			Call("ExternalIdentifier", ProtectedFieldClassification.Sensitive);
			Call("ReferenceNumber", ProtectedFieldClassification.Sensitive);
			Call("CallFormData", ProtectedFieldClassification.Phi);
			Call("DeletedReason", ProtectedFieldClassification.Sensitive);

			// ---- Call children.
			void CallChild(string table, string column, ProtectedFieldClassification classification,
				ProtectedFieldStorageKind kind = ProtectedFieldStorageKind.Text) =>
				list.Add(new ProtectedFieldDefinition($"{table.ToLowerInvariant()}.{column.ToLowerInvariant()}", CallsFamily, table, column,
					kind, classification, PermissionTypes.ViewProtectedCallData, PermissionTypes.EditProtectedCallData));

			CallChild("CallNotes", "Note", ProtectedFieldClassification.Phi);
			CallChild("CallNotes", "FlaggedReason", ProtectedFieldClassification.Sensitive);
			CallChild("CallNotes", "Latitude", ProtectedFieldClassification.Pii, ProtectedFieldStorageKind.CompanionColumn);
			CallChild("CallNotes", "Longitude", ProtectedFieldClassification.Pii, ProtectedFieldStorageKind.CompanionColumn);
			CallChild("CallLogs", "Narrative", ProtectedFieldClassification.Phi);
			CallChild("CallReferences", "Note", ProtectedFieldClassification.Phi);
			CallChild("CallAttachments", "Name", ProtectedFieldClassification.Sensitive);
			CallChild("CallAttachments", "FileName", ProtectedFieldClassification.Sensitive);
			CallChild("CallAttachments", "FlaggedReason", ProtectedFieldClassification.Sensitive);
			CallChild("CallAttachments", "Data", ProtectedFieldClassification.Phi, ProtectedFieldStorageKind.Binary);
			CallChild("CallAttachments", "Latitude", ProtectedFieldClassification.Pii, ProtectedFieldStorageKind.CompanionColumn);
			CallChild("CallAttachments", "Longitude", ProtectedFieldClassification.Pii, ProtectedFieldStorageKind.CompanionColumn);

			// ---- Personnel: department-scoped sensitive attributes live on DepartmentMemberSensitiveData
			// (never the global UserProfile row — a user can belong to several departments).
			void Member(string column, ProtectedFieldClassification classification) =>
				list.Add(new ProtectedFieldDefinition($"departmentmembersensitivedata.{column.ToLowerInvariant()}", PersonnelFamily,
					"DepartmentMemberSensitiveData", column, ProtectedFieldStorageKind.Text, classification,
					PermissionTypes.ViewProtectedPersonnelData));

			Member("IdentificationNumber", ProtectedFieldClassification.Pii);
			Member("Notes", ProtectedFieldClassification.Sensitive);

			// ---- Contacts (section 5.1: all name parts, email, government IDs, phone fields,
			// description/other information, image, GPS/geofence, and ContactNote.Note).
			void Contact(string column, ProtectedFieldClassification classification,
				ProtectedFieldStorageKind kind = ProtectedFieldStorageKind.Text) =>
				list.Add(new ProtectedFieldDefinition($"contacts.{column.ToLowerInvariant()}", ContactsFamily, "Contacts", column,
					kind, classification, PermissionTypes.ViewProtectedContactData));

			Contact("FirstName", ProtectedFieldClassification.Pii);
			Contact("MiddleName", ProtectedFieldClassification.Pii);
			Contact("LastName", ProtectedFieldClassification.Pii);
			Contact("OtherName", ProtectedFieldClassification.Pii);
			Contact("CompanyName", ProtectedFieldClassification.Pii);
			Contact("Email", ProtectedFieldClassification.Pii);
			Contact("CountryIssuedIdNumber", ProtectedFieldClassification.Pii);
			Contact("CountryIdName", ProtectedFieldClassification.Pii);
			Contact("StateIdNumber", ProtectedFieldClassification.Pii);
			Contact("StateIdName", ProtectedFieldClassification.Pii);
			Contact("StateIdCountryName", ProtectedFieldClassification.Pii);
			Contact("HomePhoneNumber", ProtectedFieldClassification.Pii);
			Contact("CellPhoneNumber", ProtectedFieldClassification.Pii);
			Contact("FaxPhoneNumber", ProtectedFieldClassification.Pii);
			Contact("OfficePhoneNumber", ProtectedFieldClassification.Pii);
			Contact("Description", ProtectedFieldClassification.Sensitive);
			Contact("OtherInfo", ProtectedFieldClassification.Sensitive);
			Contact("Image", ProtectedFieldClassification.Pii, ProtectedFieldStorageKind.Binary);
			Contact("LocationGpsCoordinates", ProtectedFieldClassification.Pii);
			Contact("EntranceGpsCoordinates", ProtectedFieldClassification.Pii);
			Contact("ExitGpsCoordinates", ProtectedFieldClassification.Pii);
			Contact("LocationGeofence", ProtectedFieldClassification.Pii);

			// ---- Operational free-form data (section 5.2), catalog v2 -------------------------
			// UdfFieldValues.Value is user-authored free text on any entity; the plan defaults free
			// text to sensitive in a protected department. UnitStates carry the crew's own note and
			// the position it was filed from — protected location data rides the companion columns.
			void Operational(string table, string column, ProtectedFieldClassification classification,
				ProtectedFieldStorageKind kind = ProtectedFieldStorageKind.Text) =>
				list.Add(new ProtectedFieldDefinition($"{table.ToLowerInvariant()}.{column.ToLowerInvariant()}",
					OperationalFamily, table, column, kind, classification,
					PermissionTypes.ViewProtectedOperationalData, PermissionTypes.EditProtectedCallData,
					OperationalCatalogVersion));

			// ---- Personnel certifications (section 5.1: "license/certification numbers and
			// documents"), catalog v6. The document itself is the binary field — a certificate scan
			// carries the member's name, licence number and often their signature, so protecting the
			// metadata while serving the file in the clear would protect nothing.
			void Certification(string column, ProtectedFieldClassification classification,
				ProtectedFieldStorageKind kind = ProtectedFieldStorageKind.Text) =>
				list.Add(new ProtectedFieldDefinition($"personnelcertifications.{column.ToLowerInvariant()}",
					PersonnelFamily, "PersonnelCertifications", column, kind, classification,
					PermissionTypes.ViewProtectedPersonnelData, PermissionTypes.ViewProtectedPersonnelData,
					CertificationCatalogVersion));

			Certification("Name", ProtectedFieldClassification.Pii);
			Certification("Number", ProtectedFieldClassification.Pii);
			Certification("Type", ProtectedFieldClassification.Sensitive);
			Certification("Area", ProtectedFieldClassification.Sensitive);
			Certification("IssuedBy", ProtectedFieldClassification.Sensitive);
			Certification("Filename", ProtectedFieldClassification.Sensitive);
			Certification("Data", ProtectedFieldClassification.Pii, ProtectedFieldStorageKind.Binary);

			// ---- Member addresses (section 5.1), catalog v5 -----------------------------------
			// An address is protected as a UNIT: leaving the city, state or postal code in the clear
			// while encrypting the street line still re-identifies the member in a small department.
			void MemberAddress(string column) =>
				list.Add(new ProtectedFieldDefinition($"departmentmembersensitivedata.{column.ToLowerInvariant()}",
					PersonnelFamily, "DepartmentMemberSensitiveData", column, ProtectedFieldStorageKind.Text,
					ProtectedFieldClassification.Pii, PermissionTypes.ViewProtectedPersonnelData,
					PermissionTypes.ViewProtectedPersonnelData, MemberAddressCatalogVersion));

			MemberAddress("HomeAddress1");
			MemberAddress("HomeCity");
			MemberAddress("HomeState");
			MemberAddress("HomePostalCode");
			MemberAddress("HomeCountry");
			MemberAddress("MailingAddress1");
			MemberAddress("MailingCity");
			MemberAddress("MailingState");
			MemberAddress("MailingPostalCode");
			MemberAddress("MailingCountry");

			// ---- Member emergency contacts (section 5.1), catalog v4 ---------------------------
			// A member may have several per department, and the values may differ per department.
			// These numbers ARE encrypted: they are next-of-kin reference data an authorized human
			// reads, never an outbound channel handed to an SMS or voice provider (the rule that
			// member NOTIFICATION numbers stay plaintext does not reach them).
			void EmergencyContact(string column, ProtectedFieldClassification classification) =>
				list.Add(new ProtectedFieldDefinition($"departmentmemberemergencycontacts.{column.ToLowerInvariant()}",
					PersonnelFamily, "DepartmentMemberEmergencyContacts", column, ProtectedFieldStorageKind.Text,
					classification, PermissionTypes.ViewProtectedPersonnelData, PermissionTypes.ViewProtectedPersonnelData,
					EmergencyContactCatalogVersion));

			EmergencyContact("Name", ProtectedFieldClassification.Pii);
			EmergencyContact("Relationship", ProtectedFieldClassification.Pii);
			EmergencyContact("PhoneNumber", ProtectedFieldClassification.Pii);
			EmergencyContact("AlternatePhoneNumber", ProtectedFieldClassification.Pii);
			EmergencyContact("Email", ProtectedFieldClassification.Pii);
			EmergencyContact("Notes", ProtectedFieldClassification.Sensitive);

			// ---- Logs (section 5.2), catalog v3 -----------------------------------------------
			// The incident/NFIRS-style log. NOTE: the separate CallLogs.Narrative entry above covers a
			// DIFFERENT table (call activity logs); both carry a Narrative column and both are
			// user-authored, so both are cataloged independently.
			void LogField(string column, ProtectedFieldClassification classification) =>
				list.Add(new ProtectedFieldDefinition($"logs.{column.ToLowerInvariant()}", OperationalFamily, "Logs",
					column, ProtectedFieldStorageKind.Text, classification,
					PermissionTypes.ViewProtectedOperationalData, PermissionTypes.EditProtectedCallData,
					LogCatalogVersion));

			LogField("Narrative", ProtectedFieldClassification.Phi);
			LogField("InitialReport", ProtectedFieldClassification.Phi);
			LogField("Cause", ProtectedFieldClassification.Sensitive);
			LogField("ContactName", ProtectedFieldClassification.Sensitive);
			LogField("ContactNumber", ProtectedFieldClassification.Sensitive);
			LogField("OtherPersonnel", ProtectedFieldClassification.Sensitive);
			LogField("Location", ProtectedFieldClassification.Sensitive);
			LogField("BodyLocation", ProtectedFieldClassification.Phi);
			LogField("PronouncedDeceasedBy", ProtectedFieldClassification.Phi);

			Operational("UdfFieldValues", "Value", ProtectedFieldClassification.Sensitive);
			Operational("UnitStates", "Note", ProtectedFieldClassification.Sensitive);
			Operational("UnitStates", "GeoLocationData", ProtectedFieldClassification.Sensitive);
			Operational("UnitStates", "Latitude", ProtectedFieldClassification.Sensitive, ProtectedFieldStorageKind.CompanionColumn);
			Operational("UnitStates", "Longitude", ProtectedFieldClassification.Sensitive, ProtectedFieldStorageKind.CompanionColumn);

			list.Add(new ProtectedFieldDefinition("contactnotes.note", ContactsFamily, "ContactNotes", "Note",
				ProtectedFieldStorageKind.Text, ProtectedFieldClassification.Sensitive, PermissionTypes.ViewProtectedContactData));

			// ---- Member messaging (section 5.2), catalog v7 -----------------------------------
			// Member-to-member messages are free text about incidents, patients and people, and the
			// reply carries whatever the member typed back; the position a reply was filed from is
			// protected location data and rides the companion columns. Both tables became bindable
			// only with M0137, which gave them a DepartmentId of their own — an envelope AAD binds
			// the department, and until then these rows could not be attributed to one.
			//
			// Subject is cataloged with the body deliberately. A subject line like "Overdose at 14
			// Elm - do not tell the family" discloses as much as the message, and leaving it in the
			// clear would leave every inbox listing readable.
			//
			// MessageRecipients.Note needed M0138 before it could be listed here. It used to be
			// dual-purpose - besides a member's typed note it carried the TextResponsePromptMetadata
			// token naming the calendar item or poll a prompt belongs to, parsed by the chatbot
			// inbound resolver, the RSVP prompt service and both message controllers. Those paths
			// hold NO grant and the broker's workload lane is encrypt-only, so encrypting the column
			// would have silently broken calendar RSVP and poll replies for the departments that
			// turned protection on. M0138 moved the token to MessageRecipients.PromptMetadata (which
			// stays plaintext: it is a row pointer and says nothing about a person), leaving Note as
			// ordinary member free text and part of this family.
			void MessageField(string table, string column, ProtectedFieldClassification classification,
				ProtectedFieldStorageKind kind = ProtectedFieldStorageKind.Text) =>
				list.Add(new ProtectedFieldDefinition($"{table.ToLowerInvariant()}.{column.ToLowerInvariant()}",
					MessagingFamily, table, column, kind, classification,
					PermissionTypes.ViewProtectedOperationalData, PermissionTypes.ViewProtectedOperationalData,
					MessagingCatalogVersion));

			MessageField("Messages", "Subject", ProtectedFieldClassification.Sensitive);
			MessageField("Messages", "Body", ProtectedFieldClassification.Sensitive);
			MessageField("MessageRecipients", "Response", ProtectedFieldClassification.Sensitive);
			MessageField("MessageRecipients", "Note", ProtectedFieldClassification.Sensitive);
			MessageField("MessageRecipients", "Latitude", ProtectedFieldClassification.Sensitive,
				ProtectedFieldStorageKind.CompanionColumn);
			MessageField("MessageRecipients", "Longitude", ProtectedFieldClassification.Sensitive,
				ProtectedFieldStorageKind.CompanionColumn);

			// ---- Moderation (section 5.3), catalog v8 ------------------------------------------
			// A moderation record is a VERBATIM COPY of the worst content the department holds: the
			// message or note that was reported, the file that came with it, and the moderator's
			// account of why. Leaving it in the clear would mean a protected department encrypts
			// the original and keeps a plaintext duplicate one table over, reachable by anyone who
			// can read the queue.
			//
			// Moderators need their normal permission AND a current grant (plan 5.3). The queue
			// itself stays usable without one: status, reason CODE and counts are structural and
			// stay plaintext, so a moderator can triage; only the excerpts need the step-up.
			//
			// NOT cataloged, deliberately: ModerationActions.ActorRole / IpAddress / UserAgent /
			// TraceId / ServerName. Those are the security audit trail of who acted and from where
			// (section 5.4), they are not the reported content, and encrypting them would blind the
			// very trail that exists to investigate abuse of the moderation tools themselves.
			void Moderation(string table, string column, ProtectedFieldClassification classification,
				ProtectedFieldStorageKind kind = ProtectedFieldStorageKind.Text) =>
				list.Add(new ProtectedFieldDefinition($"{table.ToLowerInvariant()}.{column.ToLowerInvariant()}",
					ModerationFamily, table, column, kind, classification,
					PermissionTypes.ViewProtectedOperationalData, PermissionTypes.ViewProtectedOperationalData,
					ModerationCatalogVersion));

			Moderation("ModerationRequests", "OriginalSubject", ProtectedFieldClassification.Sensitive);
			Moderation("ModerationRequests", "OriginalText", ProtectedFieldClassification.Sensitive);
			Moderation("ModerationRequests", "OriginalFileName", ProtectedFieldClassification.Sensitive);
			Moderation("ModerationRequests", "OriginalContentType", ProtectedFieldClassification.Sensitive);
			Moderation("ModerationRequests", "OriginalContent", ProtectedFieldClassification.Sensitive,
				ProtectedFieldStorageKind.Binary);
			Moderation("ModerationRequests", "OriginalMetadataJson", ProtectedFieldClassification.Sensitive);
			Moderation("ModerationRequests", "AdminNote", ProtectedFieldClassification.Sensitive);

			Moderation("ModerationReports", "Note", ProtectedFieldClassification.Sensitive);

			Moderation("ModerationActions", "Note", ProtectedFieldClassification.Sensitive);
			Moderation("ModerationActions", "DetailsJson", ProtectedFieldClassification.Sensitive);
			Moderation("ModerationActions", "EvidenceText", ProtectedFieldClassification.Sensitive);
			Moderation("ModerationActions", "EvidenceContent", ProtectedFieldClassification.Sensitive,
				ProtectedFieldStorageKind.Binary);
			Moderation("ModerationActions", "EvidenceMetadataJson", ProtectedFieldClassification.Sensitive);

			// Chat moderation carries the same classification (plan 5.3): flag and action notes,
			// reasons, detail JSON, and the export payload — an export is the whole conversation.
			Moderation("ChatMessageFlags", "Note", ProtectedFieldClassification.Sensitive);
			Moderation("ChatMessageFlags", "ResolutionNote", ProtectedFieldClassification.Sensitive);

			Moderation("ChatModerationActions", "Reason", ProtectedFieldClassification.Sensitive);
			Moderation("ChatModerationActions", "DetailsJson", ProtectedFieldClassification.Sensitive);

			Moderation("ChatExports", "Data", ProtectedFieldClassification.Sensitive,
				ProtectedFieldStorageKind.Binary);
			Moderation("ChatExports", "Error", ProtectedFieldClassification.Sensitive);

			// ---- The plan's remaining candidates (sections 5.2 and 22.1), catalog v9 -----------
			void Remaining(string family, string table, string column, ProtectedFieldClassification classification,
				PermissionTypes permission, ProtectedFieldStorageKind kind = ProtectedFieldStorageKind.Text) =>
				list.Add(new ProtectedFieldDefinition($"{table.ToLowerInvariant()}.{column.ToLowerInvariant()}",
					family, table, column, kind, classification, permission, permission,
					RemainingCandidatesCatalogVersion));

			// A unit log narrative is the crew's own account of a response, and a user state note is
			// why someone is unavailable ("at the hospital with my father") - both free text about
			// people, which the plan defaults to sensitive in a protected department.
			Remaining(OperationalFamily, "UnitLogs", "Narrative", ProtectedFieldClassification.Sensitive,
				PermissionTypes.ViewProtectedOperationalData);
			Remaining(OperationalFamily, "UserStates", "Note", ProtectedFieldClassification.Sensitive,
				PermissionTypes.ViewProtectedOperationalData);

			// Calendar entries name people and places: "Meet family re: incident 4471", a home
			// address as the location. Structural scheduling columns (start/end, timezone,
			// recurrence rule) stay plaintext - a protected department's calendar must still lay out.
			Remaining(OperationalFamily, "CalendarItems", "Title", ProtectedFieldClassification.Sensitive,
				PermissionTypes.ViewProtectedOperationalData);
			Remaining(OperationalFamily, "CalendarItems", "Description", ProtectedFieldClassification.Sensitive,
				PermissionTypes.ViewProtectedOperationalData);
			Remaining(OperationalFamily, "CalendarItems", "Location", ProtectedFieldClassification.Sensitive,
				PermissionTypes.ViewProtectedOperationalData);

			// A department document is whatever they uploaded - protocols, but also incident
			// paperwork and personnel letters. The FILE is the point, so it is cataloged with its
			// name: protecting the metadata while serving the bytes in the clear protects nothing.
			Remaining(DocumentsFamily, "Documents", "Name", ProtectedFieldClassification.Sensitive,
				PermissionTypes.ViewProtectedOperationalData);
			Remaining(DocumentsFamily, "Documents", "Description", ProtectedFieldClassification.Sensitive,
				PermissionTypes.ViewProtectedOperationalData);
			Remaining(DocumentsFamily, "Documents", "Filename", ProtectedFieldClassification.Sensitive,
				PermissionTypes.ViewProtectedOperationalData);
			Remaining(DocumentsFamily, "Documents", "Data", ProtectedFieldClassification.Sensitive,
				PermissionTypes.ViewProtectedOperationalData, ProtectedFieldStorageKind.Binary);

			// Section 22.1 credential hygiene: a stored mailbox username and password sitting in the
			// clear is a standing credential leak. Nothing in the codebase reads these columns today
			// (the import paths take their configuration elsewhere), so binding them costs nothing —
			// but a FUTURE consumer would need an attended path or a workload-decryptable secret
			// store, because the broker's decrypt lane is grant-gated by design.
			Remaining(CredentialsFamily, "DistributionLists", "Username", ProtectedFieldClassification.Sensitive,
				PermissionTypes.ManageDepartmentDataProtection);
			Remaining(CredentialsFamily, "DistributionLists", "Password", ProtectedFieldClassification.Sensitive,
				PermissionTypes.ManageDepartmentDataProtection);

			return list;
		}
	}
}
