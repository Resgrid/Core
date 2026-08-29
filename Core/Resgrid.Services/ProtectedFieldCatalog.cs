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

			return list;
		}
	}
}
