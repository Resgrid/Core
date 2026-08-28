using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Catalog v1 (draft until the Phase 0 catalog freeze): the P0 families from ADP plan section 5.1
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

		private static readonly IReadOnlyList<ProtectedFieldDefinition> Entries = BuildV1();
		private static readonly Dictionary<string, ProtectedFieldDefinition> ById =
			Entries.ToDictionary(e => e.FieldId, StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<string, IReadOnlyList<ProtectedFieldDefinition>> ByTable =
			Entries.GroupBy(e => e.TableName, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => (IReadOnlyList<ProtectedFieldDefinition>)g.ToList(), StringComparer.OrdinalIgnoreCase);

		public int Version => 1;

		public IReadOnlyList<ProtectedFieldDefinition> GetAll() => Entries;

		public IReadOnlyList<ProtectedFieldDefinition> GetForTable(string tableName)
		{
			if (string.IsNullOrWhiteSpace(tableName))
				return Array.Empty<ProtectedFieldDefinition>();

			return ByTable.TryGetValue(tableName, out var entries) ? entries : Array.Empty<ProtectedFieldDefinition>();
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
			Member("EmergencyContactName", ProtectedFieldClassification.Pii);
			Member("EmergencyContactPhone", ProtectedFieldClassification.Pii);
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

			list.Add(new ProtectedFieldDefinition("contactnotes.note", ContactsFamily, "ContactNotes", "Note",
				ProtectedFieldStorageKind.Text, ProtectedFieldClassification.Sensitive, PermissionTypes.ViewProtectedContactData));

			return list;
		}
	}
}
