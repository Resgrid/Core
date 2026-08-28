using System.Collections.Generic;
using Resgrid.Model;

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

				AdpTableBinding.Direct("DepartmentMemberSensitiveData", "DepartmentMemberSensitiveDataId", pkIsNumeric: true, "DepartmentId", new[]
				{
					Text("DepartmentMemberSensitiveData", "IdentificationNumber"),
					Text("DepartmentMemberSensitiveData", "EmergencyContactName"),
					Text("DepartmentMemberSensitiveData", "EmergencyContactPhone"),
					Text("DepartmentMemberSensitiveData", "Notes")
				}) with { ProtectedMarkerColumn = "IsProtected" }
			};
		}
	}
}
