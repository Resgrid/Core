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
					columns) with { ProtectedMarkerColumn = binding.ProtectedMarkerColumn, CarrierColumns = binding.CarrierColumns, RowFilterColumn = binding.RowFilterColumn, Discriminator = binding.Discriminator });
			}

			return scoped;
		}

		private static IReadOnlyList<AdpTableBinding> Build()
		{
			AdpColumnSpec Text(string table, string column) =>
				new AdpColumnSpec(column, $"{table.ToLowerInvariant()}.{column.ToLowerInvariant()}", ProtectedFieldStorageKind.Text);
			AdpColumnSpec Binary(string table, string column) =>
				new AdpColumnSpec(column, $"{table.ToLowerInvariant()}.{column.ToLowerInvariant()}", ProtectedFieldStorageKind.Binary);
			AdpColumnSpec Packed(string table, string column) =>
				new AdpColumnSpec(column, $"{table.ToLowerInvariant()}.{column.ToLowerInvariant()}", ProtectedFieldStorageKind.PackedJson);
			AdpColumnSpec Companion(string table, string column, bool boolean = false) =>
				new AdpColumnSpec(column, $"{table.ToLowerInvariant()}.{column.ToLowerInvariant()}",
					ProtectedFieldStorageKind.CompanionColumn, $"Protected{column}Envelope", boolean);

			var bindings = new List<AdpTableBinding>
			{
				AdpTableBinding.Direct("AuditLogs", "AuditLogId", true, "DepartmentId", new[] { Text("AuditLogs", "Data") })
					with { Discriminator = new AdpRowDiscriminator("LogType", Resgrid.Model.Checklists.ReadinessHistoryFields.AuditTypes) },
				AdpTableBinding.Direct("DomainEventOutbox", "DomainEventOutboxId", true, "DepartmentId", new[] { Text("DomainEventOutbox", "PayloadJson"), Text("DomainEventOutbox", "LastError") })
					with { Discriminator = new AdpRowDiscriminator("ProducerSubsystem", Text: "Checklists") },
				AdpTableBinding.Direct("WorkflowRuns", "WorkflowRunId", false, "DepartmentId", new[] { Text("WorkflowRuns", "InputPayload"), Text("WorkflowRuns", "ErrorMessage") })
					with { Discriminator = new AdpRowDiscriminator("TriggerEventType", Resgrid.Model.Checklists.ChecklistWorkflowPayload.Triggers) },
				AdpTableBinding.ViaParent("WorkflowRunLogs", "WorkflowRunLogId", false, "WorkflowRunId", "WorkflowRuns", "WorkflowRunId", new[] { Text("WorkflowRunLogs", "RenderedOutput"), Text("WorkflowRunLogs", "ActionResult"), Text("WorkflowRunLogs", "ErrorMessage") })
					with { Discriminator = new AdpRowDiscriminator("TriggerEventType", Resgrid.Model.Checklists.ChecklistWorkflowPayload.Triggers, OnParent: true) },
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
				// through the unit. (Messages and MessageRecipients were absent for the same reason
				// until M0137 gave them a DepartmentId of their own; they are bound below at v7.)
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
				}) with { ProtectedMarkerColumn = "IsProtected" },

				// Catalog v7: member messaging. Both are Direct on their OWN DepartmentId (M0137)
				// rather than ViaParent — a recipient row is scoped by the same column its parent
				// is, and the AAD needs a value on the row itself. Rows the M0137 backfill could
				// not attribute have a NULL DepartmentId and are therefore never selected by a
				// department-scoped sweep: unresolved ownership means untouched, not guessed.
				//
				// Messages has no IsProtected marker column (M0129 added one to MessageRecipients
				// only); reads detect the envelope prefix, exactly as they do for Contacts.
				AdpTableBinding.Direct("Messages", "MessageId", pkIsNumeric: true, "DepartmentId", new[]
				{
					Text("Messages", "Subject"),
					Text("Messages", "Body")
				}),

				// Note is here only because M0138 moved the prompt metadata it used to share a column
				// with into PromptMetadata (which stays plaintext for the grantless readers).
				AdpTableBinding.Direct("MessageRecipients", "MessageRecipientId", pkIsNumeric: true, "DepartmentId", new[]
				{
					Text("MessageRecipients", "Response"),
					Text("MessageRecipients", "Note"),
					Companion("MessageRecipients", "Latitude"),
					Companion("MessageRecipients", "Longitude")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				// Catalog v8: moderation (section 5.3). All six tables carry their own DepartmentId
				// and a string primary key the service assigns before insert, so every row can be
				// enveloped BEFORE it first reaches the table - no transient plaintext anywhere in
				// this family. Markers come from M0139.
				AdpTableBinding.Direct("ModerationRequests", "ModerationRequestId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("ModerationRequests", "OriginalSubject"),
					Text("ModerationRequests", "OriginalText"),
					Text("ModerationRequests", "OriginalFileName"),
					Text("ModerationRequests", "OriginalContentType"),
					Binary("ModerationRequests", "OriginalContent"),
					Text("ModerationRequests", "OriginalMetadataJson"),
					Text("ModerationRequests", "AdminNote")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("ModerationReports", "ModerationReportId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("ModerationReports", "Note")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("ModerationActions", "ModerationActionId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("ModerationActions", "Note"),
					Text("ModerationActions", "DetailsJson"),
					Text("ModerationActions", "EvidenceText"),
					Binary("ModerationActions", "EvidenceContent"),
					Text("ModerationActions", "EvidenceMetadataJson")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("ChatMessageFlags", "ChatMessageFlagId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("ChatMessageFlags", "Note"),
					Text("ChatMessageFlags", "ResolutionNote")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("ChatModerationActions", "ChatModerationActionId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("ChatModerationActions", "Reason"),
					Text("ChatModerationActions", "DetailsJson")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("ChatExports", "ChatExportId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Binary("ChatExports", "Data"),
					Text("ChatExports", "Error")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				// Catalog v9: the plan's remaining candidates. UnitLogs has no DepartmentId of its
				// own, so it derives ownership through its unit exactly as UnitStates does; the rest
				// carry their own. Markers come from M0140.
				AdpTableBinding.ViaParent("UnitLogs", "UnitLogId", pkIsNumeric: true, "UnitId", "Units", "UnitId", new[]
				{
					Text("UnitLogs", "Narrative")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("UserStates", "UserStateId", pkIsNumeric: true, "DepartmentId", new[]
				{
					Text("UserStates", "Note")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("CalendarItems", "CalendarItemId", pkIsNumeric: true, "DepartmentId", new[]
				{
					Text("CalendarItems", "Title"),
					Text("CalendarItems", "Description"),
					Text("CalendarItems", "Location")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("Documents", "DocumentId", pkIsNumeric: true, "DepartmentId", new[]
				{
					Text("Documents", "Name"),
					Text("Documents", "Description"),
					Text("Documents", "Filename"),
					Binary("Documents", "Data")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("DistributionLists", "DistributionListId", pkIsNumeric: true, "DepartmentId", new[]
				{
					Text("DistributionLists", "Username"),
					Text("DistributionLists", "Password")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				// Catalog v10: Records (RMS). Every RMS table carries its own DepartmentId and a string GUID
				// primary key (client-compatible ids, RMS plan 5.3), so each is a Direct binding with a
				// non-numeric key. Marker columns exist on every table below (M0150-M0171, M0176).

				AdpTableBinding.Direct("RmsOperationalRecordDetails", "RmsOperationalRecordDetailId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsOperationalRecordDetails", "Narrative"), Text("RmsOperationalRecordDetails", "InitialReport"), Text("RmsOperationalRecordDetails", "Cause"), Text("RmsOperationalRecordDetails", "ContactName"), Text("RmsOperationalRecordDetails", "ContactNumber"), Text("RmsOperationalRecordDetails", "OtherPersonnel"), Text("RmsOperationalRecordDetails", "Location"), Text("RmsOperationalRecordDetails", "BodyLocation"), Text("RmsOperationalRecordDetails", "PronouncedDeceasedBy"), Text("RmsOperationalRecordDetails", "CaseNumber"), Text("RmsOperationalRecordDetails", "Destination"), Text("RmsOperationalRecordDetails", "CallName"), Text("RmsOperationalRecordDetails", "CallAddress"), Text("RmsOperationalRecordDetails", "CallNature")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsNarratives", "RmsNarrativeId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsNarratives", "Narrative"), Text("RmsNarratives", "ImpedimentNarrative"), Text("RmsNarratives", "OutcomeNarrative"), Text("RmsNarratives", "SupplementalJson")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsLocations", "RmsLocationId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsLocations", "AddressText"), Text("RmsLocations", "Number"), Text("RmsLocations", "NumberPrefix"), Text("RmsLocations", "NumberSuffix"), Text("RmsLocations", "Street"), Text("RmsLocations", "UnitValue"), Text("RmsLocations", "CrossStreet1"), Text("RmsLocations", "CrossStreet2"), Companion("RmsLocations", "Latitude"), Companion("RmsLocations", "Longitude")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsSourceFacts", "RmsSourceFactId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsSourceFacts", "SourceValue"), Text("RmsSourceFacts", "CurrentValue")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsCasualtyRescues", "RmsCasualtyRescueId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsCasualtyRescues", "PersonnelUserId"), Text("RmsCasualtyRescues", "Rank"), Text("RmsCasualtyRescues", "JobClassification"), Text("RmsCasualtyRescues", "BirthMonthYear"), Text("RmsCasualtyRescues", "Gender"), Text("RmsCasualtyRescues", "Race"), Text("RmsCasualtyRescues", "CasualtyCause"), Text("RmsCasualtyRescues", "CasualtyAction"), Text("RmsCasualtyRescues", "CasualtyTimeline"), Text("RmsCasualtyRescues", "InjuryDetailJson"), Text("RmsCasualtyRescues", "DetailJson")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsExposures", "RmsExposureId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsExposures", "AddressText"), Text("RmsExposures", "Street"), Text("RmsExposures", "DetailJson"), Companion("RmsExposures", "Latitude"), Companion("RmsExposures", "Longitude")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsIncidentModules", "RmsIncidentModuleId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsIncidentModules", "DetailJson")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsIncidentProperties", "RmsIncidentPropertyId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsIncidentProperties", "DetailJson")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsIncidentVehicles", "RmsIncidentVehicleId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsIncidentVehicles", "Vin"), Text("RmsIncidentVehicles", "LicensePlate"), Text("RmsIncidentVehicles", "DetailJson")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsIncidentResources", "RmsIncidentResourceId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsIncidentResources", "Detail")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsRevisions", "RmsRevisionId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsRevisions", "SnapshotJson")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsSubmissions", "RmsSubmissionId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsSubmissions", "PayloadJson"), Text("RmsSubmissions", "ResponseJson")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsSignatures", "RmsSignatureId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsSignatures", "StatementText")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsEvidenceArtifacts", "RmsEvidenceArtifactId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsEvidenceArtifacts", "Title"), Text("RmsEvidenceArtifacts", "CaptureReason"), Text("RmsEvidenceArtifacts", "ManifestJson")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsDisclosureRequests", "RmsDisclosureRequestId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsDisclosureRequests", "RequesterName"), Text("RmsDisclosureRequests", "RequesterOrganization"), Text("RmsDisclosureRequests", "RequesterContact"), Text("RmsDisclosureRequests", "ScopeNarrative"), Text("RmsDisclosureRequests", "DispositionReason")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsDisclosureProductions", "RmsDisclosureProductionId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsDisclosureProductions", "ArtifactJson")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsRecordLegalHolds", "RmsRecordLegalHoldId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsRecordLegalHolds", "ReferenceNumber"), Text("RmsRecordLegalHolds", "Notes"), Text("RmsRecordLegalHolds", "ReleaseNotes")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsRecordAttachments", "RmsRecordAttachmentId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsRecordAttachments", "FileName"), Text("RmsRecordAttachments", "Description"), Binary("RmsRecordAttachments", "Data")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsExportRuns", "RmsExportRunId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Binary("RmsExportRuns", "Data")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				// Catalog v11: typed values of department definitions (RMS-1B). Only rows flagged ProtectionRequired
				// (a Protected-classified field) are swept; the typed siblings are packed into ProtectedEnvelope and
				// cleared while sealed, so Standard/Restricted values stay plaintext for search and reports.
				AdpTableBinding.Direct("RmsRecordValues", "RmsRecordValueId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Packed("RmsRecordValues", "ProtectedEnvelope")
				}) with { ProtectedMarkerColumn = "IsProtected", CarrierColumns = RmsRecordValuePack.CarrierColumns, RowFilterColumn = "ProtectionRequired" },

				// Catalog v12: Contacts pre-plans (Contacts plan Phase A). All three tables carry their own
				// DepartmentId, and each was created with the IsProtected marker (M0183/M0184).
				AdpTableBinding.Direct("ContactPreplans", "ContactPreplanId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("ContactPreplans", "OccupancyNotes"), Text("ContactPreplans", "OccupancyHours"),
					Text("ContactPreplans", "OccupantsNeedingAssistanceNotes"),
					Text("ContactPreplans", "GasShutoffLocation"), Text("ContactPreplans", "ElectricShutoffLocation"),
					Text("ContactPreplans", "WaterShutoffLocation"), Text("ContactPreplans", "UtilityNotes"),
					Text("ContactPreplans", "KnoxBoxLocation"), Text("ContactPreplans", "GateCode"),
					Text("ContactPreplans", "AlarmPanelLocation"), Text("ContactPreplans", "AlarmCompany"),
					Text("ContactPreplans", "AlarmCompanyPhone"), Text("ContactPreplans", "AccessNotes"),
					Text("ContactPreplans", "NearestHydrantLocation"), Text("ContactPreplans", "WaterSupplyNotes"),
					Text("ContactPreplans", "EmergencyContactName"), Text("ContactPreplans", "EmergencyContactPhone"),
					Text("ContactPreplans", "SecondaryContactName"), Text("ContactPreplans", "SecondaryContactPhone"),
					Text("ContactPreplans", "GeneralHazardNotes"), Text("ContactPreplans", "TacticalSummary")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("ContactPreplanHazards", "ContactPreplanHazardId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("ContactPreplanHazards", "Title"), Text("ContactPreplanHazards", "Description"),
					Text("ContactPreplanHazards", "LocationDescription"), Text("ContactPreplanHazards", "GpsCoordinates")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("ContactAttachments", "ContactAttachmentId", pkIsNumeric: true, "DepartmentId", new[]
				{
					Text("ContactAttachments", "Name"), Text("ContactAttachments", "FileName"), Binary("ContactAttachments", "Data")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				// Catalog v13: RMS-5 prevention and investigations plus the RMS-4 quality review (M0185/M0186). Every
				// table carries its own DepartmentId, a string GUID key and the IsProtected marker.
				AdpTableBinding.Direct("RmsOccupancies", "RmsOccupancyId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsOccupancies", "OccupantsNeedingAssistanceNotes"), Text("RmsOccupancies", "UtilityNotes"),
					Text("RmsOccupancies", "KnoxBoxLocation"), Text("RmsOccupancies", "GateCode"),
					Text("RmsOccupancies", "AlarmPanelLocation"), Text("RmsOccupancies", "AlarmCompany"),
					Text("RmsOccupancies", "AlarmCompanyPhone"), Text("RmsOccupancies", "AccessNotes"),
					Text("RmsOccupancies", "WaterSupplyNotes"), Text("RmsOccupancies", "EmergencyContactName"),
					Text("RmsOccupancies", "EmergencyContactPhone"), Text("RmsOccupancies", "GeneralHazardNotes"),
					Text("RmsOccupancies", "TacticalSummary")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsOccupancyHazards", "RmsOccupancyHazardId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsOccupancyHazards", "Description"), Text("RmsOccupancyHazards", "LocationDescription"), Text("RmsOccupancyHazards", "GpsCoordinates")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsInspections", "RmsInspectionId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsInspections", "Notes"), Text("RmsInspections", "SignatureName")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsViolations", "RmsViolationId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsViolations", "Description"), Text("RmsViolations", "CorrectiveAction")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsPermits", "RmsPermitId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsPermits", "ApplicantName"), Text("RmsPermits", "ApplicantPhone"), Text("RmsPermits", "ApplicantEmail"), Text("RmsPermits", "ReviewNotes")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsPlanReviews", "RmsPlanReviewId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsPlanReviews", "Comments")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsInvestigationCases", "RmsInvestigationCaseId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsInvestigationCases", "IncidentSummary"), Text("RmsInvestigationCases", "CauseDetail"),
					Text("RmsInvestigationCases", "OriginDescription"), Text("RmsInvestigationCases", "Findings"), Text("RmsInvestigationCases", "ClosureReason")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsInvestigationNotes", "RmsInvestigationNoteId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsInvestigationNotes", "Subject"), Text("RmsInvestigationNotes", "Body")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsInvestigationEvidence", "RmsInvestigationEvidenceId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsInvestigationEvidence", "Description"), Text("RmsInvestigationEvidence", "CollectedFrom"), Text("RmsInvestigationEvidence", "CurrentCustodianExternal")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsInvestigationCustody", "RmsInvestigationCustodyId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsInvestigationCustody", "FromExternal"), Text("RmsInvestigationCustody", "ToExternal"), Text("RmsInvestigationCustody", "Reason")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsInvestigationReferrals", "RmsInvestigationReferralId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsInvestigationReferrals", "Reason")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsQualityReviews", "RmsQualityReviewId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsQualityReviews", "FindingsJson"), Text("RmsQualityReviews", "Note")
				}) with { ProtectedMarkerColumn = "IsProtected" },

				AdpTableBinding.Direct("RmsPreventionAttachments", "RmsPreventionAttachmentId", pkIsNumeric: false, "DepartmentId", new[]
				{
					Text("RmsPreventionAttachments", "FileName"), Text("RmsPreventionAttachments", "Description"), Binary("RmsPreventionAttachments", "Data")
				}) with { ProtectedMarkerColumn = "IsProtected" }
			};
			return bindings.Concat(Resgrid.Model.Checklists.ChecklistTables.All.Values.Select(table =>
				AdpTableBinding.Direct(table, "Id", false, "DepartmentId", table switch
				{
					"ChecklistCompletionFiles" => new[] { Text(table, "Content"), Binary(table, "Data") },
					"ChecklistCompletions" => new[] { Text(table, "Content"), Companion(table, "Score"), Companion(table, "Passed", true) },
					"ChecklistCompletionItems" => new[] { Text(table, "Content"), Companion(table, "IsFailure", true) },
					_ => new[] { Text(table, "Content") }
				}) with { ProtectedMarkerColumn = "IsProtected" })).ToList();
		}
	}
}
