using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Code-reviewed table bindings for catalog v1 (P0 families), shared by the migration engine and
	/// the sizing scan. FieldIds MUST match ProtectedFieldCatalog exactly — they are AAD components
	/// and stable forever. Child tables derive department ownership through their verified parent
	/// (plan section 6). Add bindings only together with their catalog entries and, where a typed
	/// column is involved, the companion-column migration.
	/// </summary>
	public static class AdpTableBindings
	{
		public static readonly IReadOnlyList<AdpTableBinding> V1 = Build();

		/// <summary>
		/// The bindings restricted to the columns whose catalog fields were added in
		/// (fromCatalogVersion, toCatalogVersion] — the exact work list for a catalog-upgrade sweep.
		/// Tables left with no in-range column are dropped entirely, so an upgrade never re-reads a
		/// table it has nothing to do in. A range covering everything returns the full bindings.
		/// </summary>
		public static IReadOnlyList<AdpTableBinding> ForVersionRange(IProtectedFieldCatalog catalog,
			int fromCatalogVersion, int toCatalogVersion)
		{
			if (catalog == null || toCatalogVersion <= fromCatalogVersion)
				return new List<AdpTableBinding>();

			var inRange = new HashSet<string>(
				catalog.GetAddedBetween(fromCatalogVersion, toCatalogVersion).Select(e => e.FieldId),
				System.StringComparer.OrdinalIgnoreCase);

			var scoped = new List<AdpTableBinding>();
			foreach (var binding in V1)
			{
				var columns = binding.Columns.Where(c => inRange.Contains(c.FieldId)).ToList();
				if (columns.Count == 0)
					continue;

				// Columns is constructor-only, so rebuild the binding with the in-range subset and
				// carry the init-only marker column across.
				scoped.Add(new AdpTableBinding(binding.TableName, binding.PkColumn, binding.PkIsNumeric,
					binding.DepartmentColumn, binding.ParentFkColumn, binding.ParentTable, binding.ParentPkColumn,
					columns) with { ProtectedMarkerColumn = binding.ProtectedMarkerColumn });
			}

			return scoped;
		}

		private static IReadOnlyList<AdpTableBinding> Build()
		{
			AdpColumnSpec Text(string table, string column) =>
				new AdpColumnSpec(column, $"{table.ToLowerInvariant()}.{column.ToLowerInvariant()}", ProtectedFieldStorageKind.Text);
			AdpColumnSpec Binary(string table, string column) =>
				new AdpColumnSpec(column, $"{table.ToLowerInvariant()}.{column.ToLowerInvariant()}", ProtectedFieldStorageKind.Binary);
			AdpColumnSpec Companion(string table, string column) =>
				new AdpColumnSpec(column, $"{table.ToLowerInvariant()}.{column.ToLowerInvariant()}",
					ProtectedFieldStorageKind.CompanionColumn, $"Protected{column}Envelope");

			return new List<AdpTableBinding>
			{
				AdpTableBinding.Direct("Calls", "CallId", pkIsNumeric: true, "DepartmentId", new[]
				{
					Text("Calls", "Name"), Text("Calls", "Type"), Text("Calls", "NatureOfCall"),
					Text("Calls", "Notes"), Text("Calls", "CompletedNotes"), Text("Calls", "Address"),
					Text("Calls", "GeoLocationData"), Text("Calls", "W3W"), Text("Calls", "ContactName"),
					Text("Calls", "ContactNumber"), Text("Calls", "SourceIdentifier"), Text("Calls", "IncidentNumber"),
					Text("Calls", "ExternalIdentifier"), Text("Calls", "ReferenceNumber"), Text("Calls", "CallFormData"),
					Text("Calls", "DeletedReason")
				}),

				AdpTableBinding.ViaParent("CallNotes", "CallNoteId", pkIsNumeric: true, "CallId", "Calls", "CallId", new[]
				{
					Text("CallNotes", "Note"), Text("CallNotes", "FlaggedReason"),
					Companion("CallNotes", "Latitude"), Companion("CallNotes", "Longitude")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.ViaParent("CallAttachments", "CallAttachmentId", pkIsNumeric: true, "CallId", "Calls", "CallId", new[]
				{
					Text("CallAttachments", "Name"), Text("CallAttachments", "FileName"),
					Text("CallAttachments", "FlaggedReason"), Binary("CallAttachments", "Data"),
					Companion("CallAttachments", "Latitude"), Companion("CallAttachments", "Longitude")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("CallLogs", "CallLogId", pkIsNumeric: true, "DepartmentId", new[]
				{
					Text("CallLogs", "Narrative")
				}),

				AdpTableBinding.ViaParent("CallReferences", "CallReferenceId", pkIsNumeric: false, "SourceCallId", "Calls", "CallId", new[]
				{
					Text("CallReferences", "Note")
				}),

				AdpTableBinding.Direct("Contacts", "ContactId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("Contacts", "FirstName"), Text("Contacts", "MiddleName"), Text("Contacts", "LastName"),
					Text("Contacts", "OtherName"), Text("Contacts", "CompanyName"), Text("Contacts", "Email"),
					Text("Contacts", "CountryIssuedIdNumber"), Text("Contacts", "CountryIdName"),
					Text("Contacts", "StateIdNumber"), Text("Contacts", "StateIdName"), Text("Contacts", "StateIdCountryName"),
					Text("Contacts", "HomePhoneNumber"), Text("Contacts", "CellPhoneNumber"), Text("Contacts", "FaxPhoneNumber"),
					Text("Contacts", "OfficePhoneNumber"), Text("Contacts", "Description"), Text("Contacts", "OtherInfo"),
					Binary("Contacts", "Image"), Text("Contacts", "LocationGpsCoordinates"),
					Text("Contacts", "EntranceGpsCoordinates"), Text("Contacts", "ExitGpsCoordinates"),
					Text("Contacts", "LocationGeofence")
				}),

				AdpTableBinding.ViaParent("ContactNotes", "ContactNoteId", pkIsNumeric: false, "ContactId", "Contacts", "ContactId", new[]
				{
					Text("ContactNotes", "Note")
				}),

				// Catalog v2 (section 5.2). Neither table carries its own DepartmentId, so ownership
				// derives from a verified parent: UDF values through their definition, unit states
				// through the unit. MessageRecipients is deliberately ABSENT — Messages has no
				// DepartmentId either, so it needs the section 5.1 child-table ownership migration
				// before it can be bound at all.
				// Catalog v3: the incident log carries its own DepartmentId.
				AdpTableBinding.Direct("Logs", "LogId", pkIsNumeric: true, "DepartmentId", new[]
				{
					Text("Logs", "Narrative"),
					Text("Logs", "InitialReport"),
					Text("Logs", "Cause"),
					Text("Logs", "ContactName"),
					Text("Logs", "ContactNumber"),
					Text("Logs", "OtherPersonnel"),
					Text("Logs", "Location"),
					Text("Logs", "BodyLocation"),
					Text("Logs", "PronouncedDeceasedBy")
				}),

				AdpTableBinding.ViaParent("UdfFieldValues", "UdfFieldValueId", pkIsNumeric: false, "UdfDefinitionId", "UdfDefinitions", "UdfDefinitionId", new[]
				{
					Text("UdfFieldValues", "Value")
				}),

				AdpTableBinding.ViaParent("UnitStates", "UnitStateId", pkIsNumeric: true, "UnitId", "Units", "UnitId", new[]
				{
					Text("UnitStates", "Note"),
					Text("UnitStates", "GeoLocationData"),
					Companion("UnitStates", "Latitude"),
					Companion("UnitStates", "Longitude")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				// Catalog v4: a member's department-scoped emergency contacts (several per member).
				AdpTableBinding.Direct("DepartmentMemberEmergencyContacts", "DepartmentMemberEmergencyContactId", pkIsNumeric: true, "DepartmentId", new[]
				{
					Text("DepartmentMemberEmergencyContacts", "Name"),
					Text("DepartmentMemberEmergencyContacts", "Relationship"),
					Text("DepartmentMemberEmergencyContacts", "PhoneNumber"),
					Text("DepartmentMemberEmergencyContacts", "AlternatePhoneNumber"),
					Text("DepartmentMemberEmergencyContacts", "Email"),
					Text("DepartmentMemberEmergencyContacts", "Notes")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("DepartmentMemberSensitiveData", "DepartmentMemberSensitiveDataId", pkIsNumeric: true, "DepartmentId", new[]
				{
					Text("DepartmentMemberSensitiveData", "IdentificationNumber"),
					Text("DepartmentMemberSensitiveData", "Notes"),
					Text("DepartmentMemberSensitiveData", "HomeAddress1"),
					Text("DepartmentMemberSensitiveData", "HomeCity"),
					Text("DepartmentMemberSensitiveData", "HomeState"),
					Text("DepartmentMemberSensitiveData", "HomePostalCode"),
					Text("DepartmentMemberSensitiveData", "HomeCountry"),
					Text("DepartmentMemberSensitiveData", "MailingAddress1"),
					Text("DepartmentMemberSensitiveData", "MailingCity"),
					Text("DepartmentMemberSensitiveData", "MailingState"),
					Text("DepartmentMemberSensitiveData", "MailingPostalCode"),
					Text("DepartmentMemberSensitiveData", "MailingCountry")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("PersonnelCertifications", "PersonnelCertificationId", pkIsNumeric: true, "DepartmentId", new[]
				{
					Text("PersonnelCertifications", "Name"),
					Text("PersonnelCertifications", "Number"),
					Text("PersonnelCertifications", "Type"),
					Text("PersonnelCertifications", "Area"),
					Text("PersonnelCertifications", "IssuedBy"),
					Text("PersonnelCertifications", "Filename"),
					Binary("PersonnelCertifications", "Data")
				}) with { ProtectedMarkerColumn = "IsProtected" }
			};
		}
	}
}
