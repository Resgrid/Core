using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Static compile-time catalog of all Scriban template variables available per workflow trigger event type.
	/// Used by the Web UI variable side panel and API documentation endpoint.
	/// </summary>
	public static class WorkflowTemplateVariableCatalog
	{
		private static readonly List<TemplateVariableDescriptor> CommonDeptVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("department.id", "Department ID", "int", true),
			new TemplateVariableDescriptor("department.name", "Department name", "string", true),
			new TemplateVariableDescriptor("department.code", "4-character department code", "string", true),
			new TemplateVariableDescriptor("department.type", "Department type (Fire, EMS, etc.)", "string", true),
			new TemplateVariableDescriptor("department.time_zone", "Department time zone ID", "string", true),
			new TemplateVariableDescriptor("department.use_24_hour_time", "Whether the department uses 24-hour time", "bool", true),
			new TemplateVariableDescriptor("department.created_on", "Department creation date", "datetime", true),
			new TemplateVariableDescriptor("department.phone_number", "Department phone number", "string", true),
			new TemplateVariableDescriptor("department.display_name", "Department Profile display name (falls back to the department name)", "string", true),
			new TemplateVariableDescriptor("department.logo_url", "Department email masthead logo URL; empty unless a logo is uploaded and 'Use department branding in emails' is on", "string", true),
			new TemplateVariableDescriptor("department.website", "Department Profile website (absolute URL, empty when not set)", "string", true),
			new TemplateVariableDescriptor("department.address.street", "Street address", "string", true),
			new TemplateVariableDescriptor("department.address.city", "City", "string", true),
			new TemplateVariableDescriptor("department.address.state", "State/Province", "string", true),
			new TemplateVariableDescriptor("department.address.postal_code", "Postal/ZIP code", "string", true),
			new TemplateVariableDescriptor("department.address.country", "Country", "string", true),
			new TemplateVariableDescriptor("department.address.full", "Full formatted address", "string", true),
		};

		private static readonly List<TemplateVariableDescriptor> CommonTimestampVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("timestamp.utc_now", "Current UTC timestamp", "datetime", true),
			new TemplateVariableDescriptor("timestamp.department_now", "Current time in department's time zone", "datetime", true),
			new TemplateVariableDescriptor("timestamp.date", "Current date (yyyy-MM-dd) in department time zone", "string", true),
			new TemplateVariableDescriptor("timestamp.time", "Current time (HH:mm:ss) in department time zone", "string", true),
			new TemplateVariableDescriptor("timestamp.day_of_week", "Day of week name, e.g. Monday", "string", true),
		};

		private static readonly List<TemplateVariableDescriptor> CommonUserVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("user.id", "Triggering user ID", "string", true),
			new TemplateVariableDescriptor("user.first_name", "First name", "string", true),
			new TemplateVariableDescriptor("user.last_name", "Last name", "string", true),
			new TemplateVariableDescriptor("user.full_name", "Full name (First Last)", "string", true),
			new TemplateVariableDescriptor("user.email", "Email address", "string", true),
			new TemplateVariableDescriptor("user.mobile_number", "Mobile phone number", "string", true),
			new TemplateVariableDescriptor("user.home_number", "Home phone number", "string", true),
			new TemplateVariableDescriptor("user.identification_number", "ID/badge number", "string", true),
			new TemplateVariableDescriptor("user.username", "Login username", "string", true),
			new TemplateVariableDescriptor("user.time_zone", "User personal time zone", "string", true),
		};

		private static readonly List<TemplateVariableDescriptor> CallVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("call.id", "Call ID", "int", false),
			new TemplateVariableDescriptor("call.number", "Call number/identifier", "string", false),
			new TemplateVariableDescriptor("call.name", "Call name/title", "string", false),
			new TemplateVariableDescriptor("call.nature", "Nature of call description", "string", false),
			new TemplateVariableDescriptor("call.notes", "Call notes", "string", false),
			new TemplateVariableDescriptor("call.address", "Call address", "string", false),
			new TemplateVariableDescriptor("call.geo_location", "Geolocation data (lat,lng)", "string", false),
			new TemplateVariableDescriptor("call.type", "Call type", "string", false),
			new TemplateVariableDescriptor("call.incident_number", "Incident number", "string", false),
			new TemplateVariableDescriptor("call.reference_number", "Reference number", "string", false),
			new TemplateVariableDescriptor("call.map_page", "Map page reference", "string", false),
			new TemplateVariableDescriptor("call.priority", "Priority value (0=Low, 3=Emergency)", "int", false),
			new TemplateVariableDescriptor("call.priority_text", "Priority text (Low/Medium/High/Emergency)", "string", false),
			new TemplateVariableDescriptor("call.is_critical", "Whether the call is critical", "bool", false),
			new TemplateVariableDescriptor("call.state", "State value", "int", false),
			new TemplateVariableDescriptor("call.state_text", "State text (Active/Closed/Cancelled/Unfounded)", "string", false),
			new TemplateVariableDescriptor("call.source", "Call source value", "int", false),
			new TemplateVariableDescriptor("call.external_id", "External identifier", "string", false),
			new TemplateVariableDescriptor("call.logged_on", "When the call was logged", "datetime", false),
			new TemplateVariableDescriptor("call.closed_on", "When the call was closed", "datetime", false),
			new TemplateVariableDescriptor("call.completed_notes", "Completed/closing notes", "string", false),
			new TemplateVariableDescriptor("call.contact_name", "Contact name", "string", false),
			new TemplateVariableDescriptor("call.contact_number", "Contact phone number", "string", false),
			new TemplateVariableDescriptor("call.w3w", "What3Words location", "string", false),
			new TemplateVariableDescriptor("call.dispatch_count", "Number of dispatches", "int", false),
			new TemplateVariableDescriptor("call.dispatch_on", "Scheduled dispatch time", "datetime", false),
			new TemplateVariableDescriptor("call.form_data", "Call form data JSON", "string", false),
			new TemplateVariableDescriptor("call.is_deleted", "Whether the call is deleted", "bool", false),
			new TemplateVariableDescriptor("call.deleted_reason", "Reason for deletion", "string", false),
		};

		private static List<TemplateVariableDescriptor> GetCommon() =>
			new List<TemplateVariableDescriptor>(CommonDeptVariables.Count + CommonTimestampVariables.Count + CommonUserVariables.Count)
				.Also(l => { l.AddRange(CommonDeptVariables); l.AddRange(CommonTimestampVariables); l.AddRange(CommonUserVariables); });

		// Records (RMS) native lifecycle triggers 100-107 (plan section 5.6). The event, record and record_change
		// namespaces are the bounded snapshot the DomainEventOutbox dispatched; nothing is rehydrated from current
		// record state, and no narrative or restricted-section field is exposed here.
		private static readonly List<TemplateVariableDescriptor> RecordEventVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("event.id", "Event ID (stable across retries)", "string", false),
			new TemplateVariableDescriptor("event.name", "Trigger name, e.g. RecordFinalized", "string", false),
			new TemplateVariableDescriptor("event.schema_version", "Event schema version", "int", false),
			new TemplateVariableDescriptor("event.occurred_on", "When the event occurred (UTC)", "datetime", false),
			new TemplateVariableDescriptor("event.correlation_id", "Correlation ID (the record ID)", "string", false),
			new TemplateVariableDescriptor("event.causation_id", "ID of the event that caused this one", "string", false),
			new TemplateVariableDescriptor("event.sequence", "Per-record sequence number", "int", false),
			new TemplateVariableDescriptor("event.is_replay", "Whether this is a replayed event", "bool", false),
			new TemplateVariableDescriptor("event.origin_client", "Originating client (Web, Responder, Unit, IncidentCommand, Dispatch, Api, System)", "string", false),
		};

		private static readonly List<TemplateVariableDescriptor> RecordVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("record.id", "Record ID", "string", false),
			new TemplateVariableDescriptor("record.record_number", "Record number (empty until finalized)", "string", false),
			new TemplateVariableDescriptor("record.draft_reference", "Draft reference shown before numbering", "string", false),
			new TemplateVariableDescriptor("record.definition_key", "Definition key, e.g. system.training", "string", false),
			new TemplateVariableDescriptor("record.definition_version", "Definition version", "int", false),
			new TemplateVariableDescriptor("record.type_key", "Record type (Run, Training, Work, Meeting, Coroner, Callback, UnitActivity)", "string", false),
			new TemplateVariableDescriptor("record.state", "Lifecycle state after this event", "string", false),
			new TemplateVariableDescriptor("record.lifecycle_preset", "Lifecycle preset (QuickEntry, ReviewRequired, ApprovalAcknowledgement)", "string", false),
			new TemplateVariableDescriptor("record.department_id", "Department ID", "int", false),
			new TemplateVariableDescriptor("record.station_group_id", "Station/group ID", "int", false),
			new TemplateVariableDescriptor("record.call_id", "Linked call ID", "int", false),
			new TemplateVariableDescriptor("record.external_id", "External ID", "string", false),
			new TemplateVariableDescriptor("record.author_user_id", "Author user ID", "string", false),
			new TemplateVariableDescriptor("record.owner_user_id", "Current owner user ID", "string", false),
			new TemplateVariableDescriptor("record.started_on", "Start time", "datetime", false),
			new TemplateVariableDescriptor("record.ended_on", "End time", "datetime", false),
			new TemplateVariableDescriptor("record.created_on", "When the record was created", "datetime", false),
			new TemplateVariableDescriptor("record.finalized_on", "When the record was finalized", "datetime", false),
			new TemplateVariableDescriptor("record.revision_id", "Current revision ID", "string", false),
			new TemplateVariableDescriptor("record.revision_number", "Current revision number", "int", false),
			new TemplateVariableDescriptor("record.checksum", "Revision checksum (SHA-256)", "string", false),
			new TemplateVariableDescriptor("record.summary", "Display summary", "string", false),
			new TemplateVariableDescriptor("record.url", "Link to the record in Resgrid", "string", false),
		};

		private static readonly List<TemplateVariableDescriptor> RecordChangeVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("record_change.previous_state", "State before this event", "string", false),
			new TemplateVariableDescriptor("record_change.current_state", "State after this event", "string", false),
			new TemplateVariableDescriptor("record_change.prior_revision_id", "Prior revision ID", "string", false),
			new TemplateVariableDescriptor("record_change.current_revision_id", "Current revision ID", "string", false),
			new TemplateVariableDescriptor("record_change.reason_code", "Normalized reason code", "string", false),
		};

		// submission.* (RMS-2 triggers 108-111): the sanitized delivery outcome — state, external id/status, attempts,
		// codes and field paths. Never the destination payload or response body (plan section 5.6).
		private static readonly List<TemplateVariableDescriptor> SubmissionVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("submission.id", "Submission ID", "string", false),
			new TemplateVariableDescriptor("submission.destination", "Reporting destination, e.g. NERIS", "string", false),
			new TemplateVariableDescriptor("submission.destination_version", "Pinned destination contract version", "string", false),
			new TemplateVariableDescriptor("submission.state", "Submission state (Queued, AwaitingDestination, Accepted, Rejected, Failed)", "string", false),
			new TemplateVariableDescriptor("submission.external_id", "Destination incident ID once assigned", "string", false),
			new TemplateVariableDescriptor("submission.external_status", "Destination status value", "string", false),
			new TemplateVariableDescriptor("submission.attempts", "Delivery attempts so far", "int", false),
			new TemplateVariableDescriptor("submission.max_attempts", "Attempts allowed before the submission fails", "int", false),
			new TemplateVariableDescriptor("submission.error_summary", "Normalized rejection/failure summary (codes and field paths)", "string", false),
			new TemplateVariableDescriptor("submission.queued_on", "When the revision was queued (UTC)", "datetime", false),
			new TemplateVariableDescriptor("submission.sent_on", "When the last attempt was sent (UTC)", "datetime", false),
			new TemplateVariableDescriptor("submission.completed_on", "When the submission reached a final state (UTC)", "datetime", false),
		};

		// obligation.* rides only on trigger 112 (RMS-3): what the record is late for and by how long. Nothing about
		// the record's content — a workflow that chases an overdue report never needs to read one.
		private static readonly List<TemplateVariableDescriptor> ObligationVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("obligation.type", "What the record is late for (Review, Correction, Submission)", "string", false),
			new TemplateVariableDescriptor("obligation.due_on", "When the obligation fell due (UTC)", "datetime", false),
			new TemplateVariableDescriptor("obligation.overdue_hours", "Hours past due at evaluation time", "int", false),
			new TemplateVariableDescriptor("obligation.responsible_user_id", "Who the obligation rests with", "string", false),
			new TemplateVariableDescriptor("obligation.overdue_count", "How many times this obligation has gone overdue", "int", false),
		};

		// protection.* (RMS plan section 5.9.3) rides every Records trigger: the department's ADP posture, never a value.
		private static readonly List<TemplateVariableDescriptor> ProtectionVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("protection.is_protected", "Whether the department protects record content (Advanced Data Protection)", "bool", false),
			new TemplateVariableDescriptor("protection.is_redacted", "Whether any value in this payload was withheld (always false: payloads carry header facts only)", "bool", false),
			new TemplateVariableDescriptor("protection.protected_catalog_version", "The department's pinned protection catalog version (0 when unprotected)", "int", false),
		};

		// attachment.* (trigger 115): identity, type, size and scan state of the file just added; never its name, description or bytes.
		private static readonly List<TemplateVariableDescriptor> AttachmentVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("attachment.id", "Attachment ID", "string", false),
			new TemplateVariableDescriptor("attachment.content_type", "MIME type", "string", false),
			new TemplateVariableDescriptor("attachment.byte_size", "Size in bytes", "int", false),
			new TemplateVariableDescriptor("attachment.checksum", "SHA-256 of the stored bytes", "string", false),
			new TemplateVariableDescriptor("attachment.classification", "Unrestricted or Restricted", "string", false),
			new TemplateVariableDescriptor("attachment.scan_state", "Malware scan state (Pending, Clean, Rejected)", "string", false),
			new TemplateVariableDescriptor("attachment.uploaded_by_user_id", "Uploader user ID", "string", false),
			new TemplateVariableDescriptor("attachment.uploaded_on", "When it was uploaded (UTC)", "datetime", false),
			new TemplateVariableDescriptor("attachment.count", "Attachments now on the record", "int", false),
		};

		// disclosure.* (triggers 152-155): the request's clock and profiles plus the production's identity; never the requester or the packet.
		private static readonly List<TemplateVariableDescriptor> DisclosureVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("disclosure.request_id", "Disclosure request ID", "string", false),
			new TemplateVariableDescriptor("disclosure.request_number", "Department request number", "string", false),
			new TemplateVariableDescriptor("disclosure.state", "Request state (Received, Scoping, InReview, Produced, Released, Denied, Withdrawn, Closed)", "string", false),
			new TemplateVariableDescriptor("disclosure.received_on", "When the request was received (UTC)", "datetime", false),
			new TemplateVariableDescriptor("disclosure.statutory_due_on", "Statutory deadline (UTC)", "datetime", false),
			new TemplateVariableDescriptor("disclosure.jurisdiction_profile", "Jurisdiction profile", "string", false),
			new TemplateVariableDescriptor("disclosure.redaction_profile", "Redaction profile (Standard, NoPersonalIdentifiers, FullDisclosure)", "string", false),
			new TemplateVariableDescriptor("disclosure.assigned_to_user_id", "Assigned custodian user ID", "string", false),
			new TemplateVariableDescriptor("disclosure.closed_on", "When the request closed (UTC)", "datetime", false),
			new TemplateVariableDescriptor("disclosure.disposition", "Closing disposition (Released, Denied, Withdrawn, Closed)", "string", false),
			new TemplateVariableDescriptor("disclosure.production_id", "Production ID (produce/release only)", "string", false),
			new TemplateVariableDescriptor("disclosure.production_number", "Production number within the request", "int", false),
			new TemplateVariableDescriptor("disclosure.record_count", "Records in the packet", "int", false),
			new TemplateVariableDescriptor("disclosure.withheld_field_count", "Fields withheld in the packet", "int", false),
			new TemplateVariableDescriptor("disclosure.checksum", "SHA-256 of the packet artifact", "string", false),
			new TemplateVariableDescriptor("disclosure.byte_size", "Packet size in bytes", "int", false),
			new TemplateVariableDescriptor("disclosure.released_on", "When the packet was released (UTC)", "datetime", false),
			new TemplateVariableDescriptor("disclosure.released_by_user_id", "Releasing user ID", "string", false),
			new TemplateVariableDescriptor("disclosure.delivery_method", "How the packet was delivered", "string", false),
		};

		// legal_hold.* (triggers 156/157): scope, period, reason and actors; never the reference number or the notes.
		private static readonly List<TemplateVariableDescriptor> LegalHoldVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("legal_hold.id", "Hold ID", "string", false),
			new TemplateVariableDescriptor("legal_hold.record_id", "Held record ID (empty for a definition/date scope)", "string", false),
			new TemplateVariableDescriptor("legal_hold.definition_key", "Held definition (empty for all)", "string", false),
			new TemplateVariableDescriptor("legal_hold.period_start", "Scope period start (UTC)", "datetime", false),
			new TemplateVariableDescriptor("legal_hold.period_end", "Scope period end (UTC)", "datetime", false),
			new TemplateVariableDescriptor("legal_hold.reason", "Hold reason (Litigation, Investigation, Public records request, Other)", "string", false),
			new TemplateVariableDescriptor("legal_hold.placed_by_user_id", "Who placed the hold", "string", false),
			new TemplateVariableDescriptor("legal_hold.placed_on", "When it was placed (UTC)", "datetime", false),
			new TemplateVariableDescriptor("legal_hold.released_by_user_id", "Who released it", "string", false),
			new TemplateVariableDescriptor("legal_hold.released_on", "When it was released (UTC)", "datetime", false),
			new TemplateVariableDescriptor("legal_hold.is_released", "Whether the hold is released", "bool", false),
		};

		// evidence.* (trigger 158): the captured artifact's identity, source and checksum; never its manifest, title or reason.
		private static readonly List<TemplateVariableDescriptor> EvidenceVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("evidence.id", "Evidence artifact ID", "string", false),
			new TemplateVariableDescriptor("evidence.record_id", "Record the evidence supports", "string", false),
			new TemplateVariableDescriptor("evidence.record_kind", "Operational or IncidentReport", "string", false),
			new TemplateVariableDescriptor("evidence.kind", "Evidence source (ReadinessPacket, RunCardActivation, TrackingFix, ChatPromotion, InventoryUsage, CertificationSnapshot)", "string", false),
			new TemplateVariableDescriptor("evidence.source_subsystem", "Source subsystem", "string", false),
			new TemplateVariableDescriptor("evidence.source_entity_type", "Source entity type", "string", false),
			new TemplateVariableDescriptor("evidence.source_entity_id", "Source entity ID", "string", false),
			new TemplateVariableDescriptor("evidence.classification", "Unrestricted or Restricted", "string", false),
			new TemplateVariableDescriptor("evidence.checksum", "SHA-256 of the manifest", "string", false),
			new TemplateVariableDescriptor("evidence.byte_size", "Manifest size in bytes", "int", false),
			new TemplateVariableDescriptor("evidence.source_item_count", "Items the manifest covers", "int", false),
			new TemplateVariableDescriptor("evidence.coverage_start", "Coverage window start (UTC)", "datetime", false),
			new TemplateVariableDescriptor("evidence.coverage_end", "Coverage window end (UTC)", "datetime", false),
			new TemplateVariableDescriptor("evidence.captured_by_user_id", "Capturing user ID", "string", false),
			new TemplateVariableDescriptor("evidence.captured_on", "When it was captured (UTC)", "datetime", false),
		};

		// purge.* (trigger 159): what retention removed. The content is gone, so nothing else can be carried.
		private static readonly List<TemplateVariableDescriptor> PurgeVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("purge.purged_on", "When the content was purged (UTC)", "datetime", false),
			new TemplateVariableDescriptor("purge.attachments_purged", "Attachments removed with the record", "int", false),
			new TemplateVariableDescriptor("purge.search_erasure_pending", "Whether the search index erasure is still pending", "bool", false),
			new TemplateVariableDescriptor("purge.reason", "Retention reason recorded by the sweep", "string", false),
		};

		// export.* (trigger 160): the scheduled export run a Workflow step can carry; never the rendered content.
		private static readonly List<TemplateVariableDescriptor> ExportVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("export.run_id", "Export run ID (a step attaches this run)", "string", false),
			new TemplateVariableDescriptor("export.template_id", "Export template ID", "string", false),
			new TemplateVariableDescriptor("export.template_key", "Export template key", "string", false),
			new TemplateVariableDescriptor("export.template_name", "Export template name", "string", false),
			new TemplateVariableDescriptor("export.format", "Csv, Json or Pdf", "string", false),
			new TemplateVariableDescriptor("export.scope", "TriggeringRecord or Window", "string", false),
			new TemplateVariableDescriptor("export.window_start", "Finalized-on window start (UTC)", "datetime", false),
			new TemplateVariableDescriptor("export.window_end", "Finalized-on window end (UTC)", "datetime", false),
			new TemplateVariableDescriptor("export.record_count", "Records in the export", "int", false),
			new TemplateVariableDescriptor("export.file_name", "File name", "string", false),
			new TemplateVariableDescriptor("export.content_type", "MIME type", "string", false),
			new TemplateVariableDescriptor("export.byte_size", "Size in bytes", "int", false),
			new TemplateVariableDescriptor("export.checksum", "SHA-256 of the file", "string", false),
			new TemplateVariableDescriptor("export.redacted", "Whether protected fields were withheld from the file", "bool", false),
			new TemplateVariableDescriptor("export.generated_on", "When it was rendered (UTC)", "datetime", false),
			new TemplateVariableDescriptor("export.expires_on", "When the stored copy expires (UTC)", "datetime", false),
		};

		private static readonly List<TemplateVariableDescriptor> InspectionVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("inspection.id", "Inspection ID", "string", false),
			new TemplateVariableDescriptor("inspection.number", "Inspection number (INSP-yyyy-nnnn)", "string", false),
			new TemplateVariableDescriptor("inspection.occupancy_id", "Occupancy ID", "string", false),
			new TemplateVariableDescriptor("inspection.occupancy_number", "Occupancy number", "string", false),
			new TemplateVariableDescriptor("inspection.occupancy_name", "Occupancy name", "string", false),
			new TemplateVariableDescriptor("inspection.program_id", "Inspection program ID", "string", false),
			new TemplateVariableDescriptor("inspection.program_name", "Inspection program name", "string", false),
			new TemplateVariableDescriptor("inspection.state", "Completed or ReinspectionRequired", "string", false),
			new TemplateVariableDescriptor("inspection.result", "Pass, Fail or Conditional", "string", false),
			new TemplateVariableDescriptor("inspection.scheduled_on", "When it was scheduled (UTC)", "datetime", false),
			new TemplateVariableDescriptor("inspection.completed_on", "When it was completed (UTC)", "datetime", false),
			new TemplateVariableDescriptor("inspection.inspector_user_id", "Inspector user ID", "string", false),
			new TemplateVariableDescriptor("inspection.violation_count", "Violations opened by this inspection", "int", false),
			new TemplateVariableDescriptor("inspection.critical_violation_count", "Critical violations opened", "int", false),
			new TemplateVariableDescriptor("inspection.is_reinspection", "Whether this re-checks an earlier inspection", "bool", false),
		};

		private static readonly List<TemplateVariableDescriptor> ViolationVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("violation.id", "Violation ID", "string", false),
			new TemplateVariableDescriptor("violation.inspection_id", "Inspection ID", "string", false),
			new TemplateVariableDescriptor("violation.occupancy_id", "Occupancy ID", "string", false),
			new TemplateVariableDescriptor("violation.occupancy_number", "Occupancy number", "string", false),
			new TemplateVariableDescriptor("violation.occupancy_name", "Occupancy name", "string", false),
			new TemplateVariableDescriptor("violation.code_set_id", "Adopted code set ID", "string", false),
			new TemplateVariableDescriptor("violation.code_section_id", "Code section ID", "string", false),
			new TemplateVariableDescriptor("violation.code_section_number", "Cited code section", "string", false),
			new TemplateVariableDescriptor("violation.severity", "Minor, Moderate, Serious or Critical", "string", false),
			new TemplateVariableDescriptor("violation.state", "Open or Escalated", "string", false),
			new TemplateVariableDescriptor("violation.due_on", "Correction due date (UTC)", "datetime", false),
			new TemplateVariableDescriptor("violation.days_overdue", "Whole days past the due date", "int", false),
		};

		private static readonly List<TemplateVariableDescriptor> PermitVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("permit.id", "Permit ID", "string", false),
			new TemplateVariableDescriptor("permit.number", "Permit number (PRM-yyyy-nnnn)", "string", false),
			new TemplateVariableDescriptor("permit.type_id", "Permit type ID", "string", false),
			new TemplateVariableDescriptor("permit.type_name", "Permit type", "string", false),
			new TemplateVariableDescriptor("permit.type_code", "Permit type code", "string", false),
			new TemplateVariableDescriptor("permit.occupancy_id", "Occupancy ID", "string", false),
			new TemplateVariableDescriptor("permit.occupancy_number", "Occupancy number", "string", false),
			new TemplateVariableDescriptor("permit.occupancy_name", "Occupancy name", "string", false),
			new TemplateVariableDescriptor("permit.state", "Always Issued", "string", false),
			new TemplateVariableDescriptor("permit.issued_on", "Issue date (UTC)", "datetime", false),
			new TemplateVariableDescriptor("permit.effective_on", "Effective date (UTC)", "datetime", false),
			new TemplateVariableDescriptor("permit.expires_on", "Expiry date (UTC)", "datetime", false),
			new TemplateVariableDescriptor("permit.days_until_expiry", "Whole days until expiry", "int", false),
			new TemplateVariableDescriptor("permit.fee_paid", "Whether the fee was recorded as paid", "bool", false),
		};

		// definition.* (RMS-1B): stable definition identity on every department-definition Record event and on the
		// definition lifecycle triggers 113/114. Never field values.
		private static readonly List<TemplateVariableDescriptor> DefinitionVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("definition.id", "Definition ID", "string", false),
			new TemplateVariableDescriptor("definition.key", "Definition key, e.g. security-patrol", "string", false),
			new TemplateVariableDescriptor("definition.name", "Definition name", "string", false),
			new TemplateVariableDescriptor("definition.category", "Definition category", "string", false),
			new TemplateVariableDescriptor("definition.owner", "System or Department", "string", false),
			new TemplateVariableDescriptor("definition.version", "Definition version", "int", false),
			new TemplateVariableDescriptor("definition.previous_version", "Previously published version (publish trigger)", "int", false),
			new TemplateVariableDescriptor("definition.state", "Draft, Published or Retired", "string", false),
			new TemplateVariableDescriptor("definition.lifecycle_preset", "Lifecycle preset", "string", false),
			new TemplateVariableDescriptor("definition.template_key", "Product template the definition was cloned from", "string", false),
			new TemplateVariableDescriptor("definition.jurisdiction_profile_key", "Jurisdiction profile (generic, us, ca, us-ca)", "string", false),
			new TemplateVariableDescriptor("definition.minimum_client_capability", "Client capability floor", "string", false),
			new TemplateVariableDescriptor("definition.schema_checksum", "Published schema checksum", "string", false),
			new TemplateVariableDescriptor("definition.published_on", "When the version was published (UTC)", "datetime", false),
			new TemplateVariableDescriptor("definition.retired", "Whether the definition is retired", "bool", false),
			new TemplateVariableDescriptor("definition.reason", "Retirement reason (retire trigger)", "string", false),
			new TemplateVariableDescriptor("definition.exposed_field_keys", "Field keys available under fields.*", "array", false),
			new TemplateVariableDescriptor("definition.section_keys", "Section keys", "array", false),
		};

		// fields.* (RMS-1B): the values of fields the definition author marked WorkflowExposed; restricted and
		// protected fields never appear. Repeating sections arrive as fields.<section> (an array of rows) plus
		// fields.<section>_count.
		private static readonly List<TemplateVariableDescriptor> FieldsVariables = new List<TemplateVariableDescriptor>
		{
			// Keys are the definition's own field keys (definition.exposed_field_keys); repeating sections add <section_key> (rows) and <section_key>_count.
			new TemplateVariableDescriptor("fields", "Workflow-exposed field values of the department definition, keyed by field key; repeating sections appear as arrays plus a <section_key>_count", "object", false),
		};

		// review.* rides only on the two review-path triggers: review bookkeeping, never record content.
		private static readonly List<TemplateVariableDescriptor> ReviewVariables = new List<TemplateVariableDescriptor>
		{
			new TemplateVariableDescriptor("review.reviewer_user_id", "Reviewer user ID (set when the record was returned)", "string", false),
			new TemplateVariableDescriptor("review.submitted_for_review_on", "When the record was submitted for review (UTC)", "datetime", false),
			new TemplateVariableDescriptor("review.review_due_on", "Review due time from the department's review-due hours (UTC)", "datetime", false),
			new TemplateVariableDescriptor("review.returned_on", "When the record was last returned for correction (UTC)", "datetime", false),
			new TemplateVariableDescriptor("review.return_count", "How many times the record has been returned", "int", false),
			new TemplateVariableDescriptor("review.reason_code", "Return reason code", "string", false),
			new TemplateVariableDescriptor("review.reason_text", "Return reason text entered by the reviewer", "string", false),
		};

		public static IReadOnlyList<TemplateVariableDescriptor> GetVariableCatalog(WorkflowTriggerEventType eventType)
		{
			var list = GetCommon();

			switch (eventType)
			{
				case WorkflowTriggerEventType.WorkOrderCreated:
				case WorkflowTriggerEventType.WorkOrderStatusChanged:
				case WorkflowTriggerEventType.WorkOrderAssigned:
					foreach (var pair in WorkOrders.WorkOrderWorkflowPayload.Variables)
						list.Add(new TemplateVariableDescriptor("work_order." + pair.Variable, "Work order " + pair.Variable.Replace('_', ' ') + (pair.Variable == "title" ? "; always REDACTED. Do not compare or render this value." : ""), pair.Variable is "asset_id" or "due_on" or "title" ? "string" : "int", false));
					list.Add(new TemplateVariableDescriptor("work_order.url", "Authenticated work-order link", "string", false));
					list.Add(new TemplateVariableDescriptor("protection.is_redacted", "Sensitive work-order fields are withheld", "bool", false));
					list.Add(new TemplateVariableDescriptor("protection.redacted_fields", "Withheld fields", "array", false));
					list.Add(new TemplateVariableDescriptor("protection.catalog_version", "Protection catalog", "int", false));
					break;
				case WorkflowTriggerEventType.ChecklistCompleted:
				case WorkflowTriggerEventType.ChecklistFailed:
                case WorkflowTriggerEventType.ChecklistMissed:
                case WorkflowTriggerEventType.ChecklistScheduleChanged:
                case WorkflowTriggerEventType.ChecklistOccurrenceSkipped:
					foreach (var field in new[] { "completion_id", "definition_id", "version_id", "target_type", "target_id", "score", "passed", "item_id", "schedule_id", "occurrence_id", "period_start_utc", "window_end_utc", "state", "is_active", "revision", "url" })
						list.Add(new TemplateVariableDescriptor("checklist." + field, "Checklist " + field + (field == "score" || field == "passed" ? "; REDACTED when protected. Check protection.is_redacted before comparing results." : ""), field == "passed" || field == "is_active" ? "bool" : field == "score" ? "decimal" : field == "target_type" || field == "state" || field == "revision" ? "int" : "string", false));
					list.Add(new TemplateVariableDescriptor("protection.is_redacted", "Whether checklist outcomes were withheld", "bool", false));
					list.Add(new TemplateVariableDescriptor("protection.redacted_fields", "Names of withheld checklist outcomes", "array", false));
					list.Add(new TemplateVariableDescriptor("protection.catalog_version", "Protection catalog used for this projection", "int", false));
					break;
				case WorkflowTriggerEventType.CommandEstablished:
				case WorkflowTriggerEventType.CommandTransferred:
				case WorkflowTriggerEventType.IncidentClosed:
				case WorkflowTriggerEventType.ResourceAssigned:
				case WorkflowTriggerEventType.ResourceReleased:
				case WorkflowTriggerEventType.ObjectiveCompleted:
				case WorkflowTriggerEventType.CriticalParDetected:
				case WorkflowTriggerEventType.IncidentRoleAssigned:
				case WorkflowTriggerEventType.AdHocResourceCreated:
				case WorkflowTriggerEventType.IncidentChannelOpened:
				case WorkflowTriggerEventType.PublicIncidentNoteAdded:
				case WorkflowTriggerEventType.InternalIncidentNoteAdded:
				case WorkflowTriggerEventType.PublicIncidentDocumentAdded:
				case WorkflowTriggerEventType.InternalIncidentDocumentAdded:
				case WorkflowTriggerEventType.IncidentNoteRemoved:
				case WorkflowTriggerEventType.IncidentDocumentRemoved:
				case WorkflowTriggerEventType.IncidentActionPlanUpdated:
				case WorkflowTriggerEventType.IncidentCommandPostUpdated:
				case WorkflowTriggerEventType.IncidentPublicSharingEnabled:
				case WorkflowTriggerEventType.IncidentPublicSharingDisabled:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("incident.command_id", "Incident command identifier", "string", false),
						new TemplateVariableDescriptor("incident.call_id", "Call/incident identifier", "int", false),
						new TemplateVariableDescriptor("incident.department_id", "Department identifier", "int", false),
						new TemplateVariableDescriptor("incident.user_id", "User associated with the event (when applicable)", "string", false),
						new TemplateVariableDescriptor("incident.name", "Name associated with the event (objective/resource/channel)", "string", false),
						new TemplateVariableDescriptor("incident.visibility", "Content visibility (0=Internal, 1=Public)", "int", false),
						new TemplateVariableDescriptor("incident.note_id", "Incident note identifier", "string", false),
						new TemplateVariableDescriptor("incident.note_type", "Operational note type", "int", false),
						new TemplateVariableDescriptor("incident.title", "Status-note title", "string", false),
						new TemplateVariableDescriptor("incident.body", "Status-note body", "string", false),
						new TemplateVariableDescriptor("incident.containment_percent", "Optional containment percentage", "decimal", false),
						new TemplateVariableDescriptor("incident.attachment_id", "Incident attachment identifier", "string", false),
						new TemplateVariableDescriptor("incident.file_name", "Attachment file name", "string", false),
						new TemplateVariableDescriptor("incident.content_type", "Attachment MIME type", "string", false),
						new TemplateVariableDescriptor("incident.content_length", "Attachment size in bytes", "long", false),
						new TemplateVariableDescriptor("incident.sha256_hash", "Attachment SHA-256 integrity hash", "string", false),
						new TemplateVariableDescriptor("incident.description", "Attachment/event description", "string", false),
						new TemplateVariableDescriptor("incident.action_plan", "Current incident action plan", "string", false),
						new TemplateVariableDescriptor("incident.latitude", "Command-post latitude", "string", false),
						new TemplateVariableDescriptor("incident.longitude", "Command-post longitude", "string", false),
						new TemplateVariableDescriptor("incident.enabled", "Whether public sharing is enabled", "bool", false),
					});
					break;

				case WorkflowTriggerEventType.CallAdded:
				case WorkflowTriggerEventType.CallUpdated:
				case WorkflowTriggerEventType.CallClosed:
					list.AddRange(CallVariables);
					list.AddRange(new[]
					{
						// Personnel dispatches
						new TemplateVariableDescriptor("call.dispatches", "Array of dispatched personnel objects", "array", false),
						new TemplateVariableDescriptor("call.dispatches[n].user_id", "Dispatched user ID", "string", false),
						new TemplateVariableDescriptor("call.dispatches[n].first_name", "Dispatched person first name", "string", false),
						new TemplateVariableDescriptor("call.dispatches[n].last_name", "Dispatched person last name", "string", false),
						new TemplateVariableDescriptor("call.dispatches[n].full_name", "Dispatched person full name (First Last)", "string", false),
						new TemplateVariableDescriptor("call.dispatches[n].email", "Dispatched person email address", "string", false),
						new TemplateVariableDescriptor("call.dispatches[n].mobile_number", "Dispatched person mobile number", "string", false),
						new TemplateVariableDescriptor("call.dispatches[n].identification_number", "Dispatched person ID/badge number", "string", false),
						new TemplateVariableDescriptor("call.dispatches[n].group_id", "Dispatched person's group ID (0 if none)", "int", false),
						new TemplateVariableDescriptor("call.dispatches[n].group_name", "Dispatched person's group name (empty if none)", "string", false),
						new TemplateVariableDescriptor("call.dispatches[n].role_names", "Comma-separated list of role names for the dispatched person", "string", false),
						new TemplateVariableDescriptor("call.dispatches[n].roles", "Array of role name strings for the dispatched person", "array", false),
						new TemplateVariableDescriptor("call.dispatches[n].dispatch_count", "Number of times this user was dispatched", "int", false),
						new TemplateVariableDescriptor("call.dispatches[n].dispatched_on", "Time of dispatch", "datetime", false),
						// Unit dispatches
						new TemplateVariableDescriptor("call.unit_dispatches", "Array of dispatched unit objects", "array", false),
						new TemplateVariableDescriptor("call.unit_dispatches[n].unit_id", "Dispatched unit ID", "int", false),
						new TemplateVariableDescriptor("call.unit_dispatches[n].unit_name", "Dispatched unit name", "string", false),
						new TemplateVariableDescriptor("call.unit_dispatches[n].unit_type", "Dispatched unit type", "string", false),
						new TemplateVariableDescriptor("call.unit_dispatches[n].vin", "Unit VIN", "string", false),
						new TemplateVariableDescriptor("call.unit_dispatches[n].plate_number", "Unit license plate number", "string", false),
						new TemplateVariableDescriptor("call.unit_dispatches[n].station_group_id", "Unit station group ID", "int", false),
						new TemplateVariableDescriptor("call.unit_dispatches[n].dispatch_count", "Number of times this unit was dispatched", "int", false),
						new TemplateVariableDescriptor("call.unit_dispatches[n].dispatched_on", "Time of unit dispatch", "datetime", false),
						// Group dispatches
						new TemplateVariableDescriptor("call.group_dispatches", "Array of dispatched group objects", "array", false),
						new TemplateVariableDescriptor("call.group_dispatches[n].group_id", "Dispatched group ID", "int", false),
						new TemplateVariableDescriptor("call.group_dispatches[n].group_name", "Dispatched group name", "string", false),
						new TemplateVariableDescriptor("call.group_dispatches[n].group_type", "Dispatched group type value", "int", false),
						new TemplateVariableDescriptor("call.group_dispatches[n].dispatch_email", "Dispatched group dispatch email", "string", false),
						new TemplateVariableDescriptor("call.group_dispatches[n].latitude", "Dispatched group latitude", "string", false),
						new TemplateVariableDescriptor("call.group_dispatches[n].longitude", "Dispatched group longitude", "string", false),
						new TemplateVariableDescriptor("call.group_dispatches[n].dispatch_count", "Number of times this group was dispatched", "int", false),
						new TemplateVariableDescriptor("call.group_dispatches[n].dispatched_on", "Time of group dispatch", "datetime", false),
						// Role dispatches
						new TemplateVariableDescriptor("call.role_dispatches", "Array of dispatched role objects", "array", false),
						new TemplateVariableDescriptor("call.role_dispatches[n].role_id", "Dispatched role ID", "int", false),
						new TemplateVariableDescriptor("call.role_dispatches[n].role_name", "Dispatched role name", "string", false),
						new TemplateVariableDescriptor("call.role_dispatches[n].role_description", "Dispatched role description", "string", false),
						new TemplateVariableDescriptor("call.role_dispatches[n].dispatch_count", "Number of times this role was dispatched", "int", false),
						new TemplateVariableDescriptor("call.role_dispatches[n].dispatched_on", "Time of role dispatch", "datetime", false),
						// Call notes list
						new TemplateVariableDescriptor("call.notes_list", "Array of call note objects", "array", false),
						new TemplateVariableDescriptor("call.notes_list[n].note", "Note text", "string", false),
						new TemplateVariableDescriptor("call.notes_list[n].source", "Note source", "string", false),
						new TemplateVariableDescriptor("call.notes_list[n].timestamp", "Note timestamp", "datetime", false),
						new TemplateVariableDescriptor("call.notes_list[n].user_id", "User who added the note", "string", false),
						// Contacts
						new TemplateVariableDescriptor("call.contacts", "Array of call contact objects", "array", false),
						new TemplateVariableDescriptor("call.contacts[n].contact_id", "Contact record ID", "string", false),
						new TemplateVariableDescriptor("call.contacts[n].contact_type", "Contact type (Primary, Additional)", "string", false),
					});
					break;

				case WorkflowTriggerEventType.UnitStatusChanged:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("unit_status.id", "UnitState ID", "int", false),
						new TemplateVariableDescriptor("unit_status.state", "State value", "int", false),
						new TemplateVariableDescriptor("unit_status.state_text", "State text", "string", false),
						new TemplateVariableDescriptor("unit_status.timestamp", "Status timestamp", "datetime", false),
						new TemplateVariableDescriptor("unit_status.note", "Status note", "string", false),
						new TemplateVariableDescriptor("unit_status.latitude", "Latitude", "decimal", false),
						new TemplateVariableDescriptor("unit_status.longitude", "Longitude", "decimal", false),
						new TemplateVariableDescriptor("unit_status.destination_id", "Destination ID", "int", false),
						new TemplateVariableDescriptor("unit.id", "Unit ID", "int", false),
						new TemplateVariableDescriptor("unit.name", "Unit name", "string", false),
						new TemplateVariableDescriptor("unit.type", "Unit type", "string", false),
						new TemplateVariableDescriptor("unit.vin", "VIN", "string", false),
						new TemplateVariableDescriptor("unit.plate_number", "License plate", "string", false),
						new TemplateVariableDescriptor("unit.station_group_id", "Station group ID", "int", false),
						new TemplateVariableDescriptor("previous_unit_status.state", "Previous state value", "int", false),
						new TemplateVariableDescriptor("previous_unit_status.state_text", "Previous state text", "string", false),
						new TemplateVariableDescriptor("previous_unit_status.timestamp", "Previous status timestamp", "datetime", false),
					});
					break;

				case WorkflowTriggerEventType.PersonnelStaffingChanged:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("staffing.id", "UserState ID", "int", false),
						new TemplateVariableDescriptor("staffing.state", "Staffing state value", "int", false),
						new TemplateVariableDescriptor("staffing.state_text", "Staffing state text", "string", false),
						new TemplateVariableDescriptor("staffing.timestamp", "Staffing timestamp", "datetime", false),
						new TemplateVariableDescriptor("staffing.note", "Staffing note", "string", false),
						new TemplateVariableDescriptor("previous_staffing.state", "Previous staffing state value", "int", false),
						new TemplateVariableDescriptor("previous_staffing.state_text", "Previous staffing state text", "string", false),
						new TemplateVariableDescriptor("previous_staffing.timestamp", "Previous staffing timestamp", "datetime", false),
					});
					break;

				case WorkflowTriggerEventType.PersonnelStatusChanged:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("status.id", "ActionLog ID", "int", false),
						new TemplateVariableDescriptor("status.action_type", "Action type value", "int", false),
						new TemplateVariableDescriptor("status.action_text", "Action text (Standing By/Responding/etc.)", "string", false),
						new TemplateVariableDescriptor("status.timestamp", "Status timestamp", "datetime", false),
						new TemplateVariableDescriptor("status.geo_location", "Geolocation data", "string", false),
						new TemplateVariableDescriptor("status.destination_id", "Destination ID", "int", false),
						new TemplateVariableDescriptor("status.note", "Status note", "string", false),
						new TemplateVariableDescriptor("previous_status.action_type", "Previous action type value", "int", false),
						new TemplateVariableDescriptor("previous_status.action_text", "Previous action text", "string", false),
						new TemplateVariableDescriptor("previous_status.timestamp", "Previous status timestamp", "datetime", false),
					});
					break;

				case WorkflowTriggerEventType.UserCreated:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("new_user.id", "New user ID", "string", false),
						new TemplateVariableDescriptor("new_user.username", "New user login username", "string", false),
						new TemplateVariableDescriptor("new_user.email", "New user email address", "string", false),
						new TemplateVariableDescriptor("new_user.name", "New user display name", "string", false),
					});
					break;

				case WorkflowTriggerEventType.UserAssignedToGroup:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("assigned_user.id", "Assigned user ID", "string", false),
						new TemplateVariableDescriptor("assigned_user.name", "Assigned user name", "string", false),
						new TemplateVariableDescriptor("group.id", "Group ID", "int", false),
						new TemplateVariableDescriptor("group.name", "Group name", "string", false),
						new TemplateVariableDescriptor("group.type", "Group type", "int", false),
						new TemplateVariableDescriptor("group.dispatch_email", "Group dispatch email", "string", false),
						new TemplateVariableDescriptor("previous_group.id", "Previous group ID", "int", false),
						new TemplateVariableDescriptor("previous_group.name", "Previous group name", "string", false),
					});
					break;

				case WorkflowTriggerEventType.DocumentAdded:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("document.id", "Document ID", "int", false),
						new TemplateVariableDescriptor("document.name", "Document name", "string", false),
						new TemplateVariableDescriptor("document.category", "Document category", "string", false),
						new TemplateVariableDescriptor("document.description", "Document description", "string", false),
						new TemplateVariableDescriptor("document.type", "MIME type", "string", false),
						new TemplateVariableDescriptor("document.filename", "File name", "string", false),
						new TemplateVariableDescriptor("document.admins_only", "Whether admins-only", "bool", false),
						new TemplateVariableDescriptor("document.added_on", "Date added", "datetime", false),
					});
					break;

				case WorkflowTriggerEventType.NoteAdded:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("note.id", "Note ID", "int", false),
						new TemplateVariableDescriptor("note.title", "Note title", "string", false),
						new TemplateVariableDescriptor("note.body", "Note body text", "string", false),
						new TemplateVariableDescriptor("note.color", "Note color", "string", false),
						new TemplateVariableDescriptor("note.category", "Note category", "string", false),
						new TemplateVariableDescriptor("note.is_admin_only", "Whether admin-only", "bool", false),
						new TemplateVariableDescriptor("note.added_on", "Date added", "datetime", false),
						new TemplateVariableDescriptor("note.expires_on", "Expiry date", "datetime", false),
					});
					break;

				case WorkflowTriggerEventType.UnitAdded:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("unit.id", "Unit ID", "int", false),
						new TemplateVariableDescriptor("unit.name", "Unit name", "string", false),
						new TemplateVariableDescriptor("unit.type", "Unit type", "string", false),
						new TemplateVariableDescriptor("unit.vin", "VIN", "string", false),
						new TemplateVariableDescriptor("unit.plate_number", "License plate", "string", false),
						new TemplateVariableDescriptor("unit.station_group_id", "Station group ID", "int", false),
						new TemplateVariableDescriptor("unit.four_wheel", "4-wheel drive", "bool", false),
						new TemplateVariableDescriptor("unit.special_permit", "Special permit", "bool", false),
					});
					break;

				case WorkflowTriggerEventType.LogAdded:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("log.id", "Log ID", "int", false),
						new TemplateVariableDescriptor("log.narrative", "Narrative text", "string", false),
						new TemplateVariableDescriptor("log.type", "Log type string", "string", false),
						new TemplateVariableDescriptor("log.log_type", "Log type int", "int", false),
						new TemplateVariableDescriptor("log.external_id", "External ID", "string", false),
						new TemplateVariableDescriptor("log.initial_report", "Initial report text", "string", false),
						new TemplateVariableDescriptor("log.course", "Course name", "string", false),
						new TemplateVariableDescriptor("log.course_code", "Course code", "string", false),
						new TemplateVariableDescriptor("log.instructors", "Instructors", "string", false),
						new TemplateVariableDescriptor("log.cause", "Cause", "string", false),
						new TemplateVariableDescriptor("log.contact_name", "Contact name", "string", false),
						new TemplateVariableDescriptor("log.contact_number", "Contact number", "string", false),
						new TemplateVariableDescriptor("log.location", "Location", "string", false),
						new TemplateVariableDescriptor("log.started_on", "Start time", "datetime", false),
						new TemplateVariableDescriptor("log.ended_on", "End time", "datetime", false),
						new TemplateVariableDescriptor("log.logged_on", "Logged on date", "datetime", false),
						new TemplateVariableDescriptor("log.other_agencies", "Other agencies", "string", false),
						new TemplateVariableDescriptor("log.other_units", "Other units", "string", false),
						new TemplateVariableDescriptor("log.other_personnel", "Other personnel", "string", false),
						new TemplateVariableDescriptor("log.call_id", "Linked call ID", "int", false),
					});
					break;

				case WorkflowTriggerEventType.CalendarEventAdded:
				case WorkflowTriggerEventType.CalendarEventUpdated:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("calendar.id", "Calendar item ID", "int", false),
						new TemplateVariableDescriptor("calendar.title", "Event title", "string", false),
						new TemplateVariableDescriptor("calendar.description", "Event description", "string", false),
						new TemplateVariableDescriptor("calendar.location", "Event location", "string", false),
						new TemplateVariableDescriptor("calendar.start", "Start date/time", "datetime", false),
						new TemplateVariableDescriptor("calendar.end", "End date/time", "datetime", false),
						new TemplateVariableDescriptor("calendar.is_all_day", "All-day event", "bool", false),
						new TemplateVariableDescriptor("calendar.item_type", "Item type value", "int", false),
						new TemplateVariableDescriptor("calendar.signup_type", "Signup type value", "int", false),
						new TemplateVariableDescriptor("calendar.is_public", "Public event", "bool", false),
					});
					break;

				case WorkflowTriggerEventType.ShiftCreated:
				case WorkflowTriggerEventType.ShiftUpdated:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("shift.id", "Shift ID", "int", false),
						new TemplateVariableDescriptor("shift.name", "Shift name", "string", false),
						new TemplateVariableDescriptor("shift.code", "Shift code", "string", false),
						new TemplateVariableDescriptor("shift.schedule_type", "Schedule type value", "int", false),
						new TemplateVariableDescriptor("shift.assignment_type", "Assignment type value", "int", false),
						new TemplateVariableDescriptor("shift.color", "Shift color", "string", false),
						new TemplateVariableDescriptor("shift.start_day", "Start day", "datetime", false),
						new TemplateVariableDescriptor("shift.start_time", "Start time string", "string", false),
						new TemplateVariableDescriptor("shift.end_time", "End time string", "string", false),
						new TemplateVariableDescriptor("shift.hours", "Shift hours", "int", false),
						new TemplateVariableDescriptor("shift.department_number", "Department number", "string", false),
					});
					break;

				case WorkflowTriggerEventType.ResourceOrderAdded:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("order.id", "Resource order ID", "int", false),
						new TemplateVariableDescriptor("order.title", "Order title", "string", false),
						new TemplateVariableDescriptor("order.incident_number", "Incident number", "string", false),
						new TemplateVariableDescriptor("order.incident_name", "Incident name", "string", false),
						new TemplateVariableDescriptor("order.incident_address", "Incident address", "string", false),
						new TemplateVariableDescriptor("order.summary", "Summary", "string", false),
						new TemplateVariableDescriptor("order.open_date", "Open date", "datetime", false),
						new TemplateVariableDescriptor("order.needed_by", "Needed by date", "datetime", false),
						new TemplateVariableDescriptor("order.contact_name", "Contact name", "string", false),
						new TemplateVariableDescriptor("order.contact_number", "Contact number", "string", false),
						new TemplateVariableDescriptor("order.special_instructions", "Special instructions", "string", false),
						new TemplateVariableDescriptor("order.meetup_location", "Meetup location", "string", false),
						new TemplateVariableDescriptor("order.financial_code", "Financial code", "string", false),
					});
					break;

				case WorkflowTriggerEventType.ShiftTradeRequested:
				case WorkflowTriggerEventType.ShiftTradeFilled:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("shift_trade.id", "Shift signup trade ID", "int", false),
						new TemplateVariableDescriptor("shift_trade.filled_by_user_id", "User ID who filled the trade (ShiftTradeFilled only)", "string", false),
						new TemplateVariableDescriptor("shift_trade.department_number", "Department number", "string", false),
					});
					break;

				case WorkflowTriggerEventType.MessageSent:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("message.id", "Message ID", "int", false),
						new TemplateVariableDescriptor("message.subject", "Message subject", "string", false),
						new TemplateVariableDescriptor("message.body", "Message body", "string", false),
						new TemplateVariableDescriptor("message.is_broadcast", "Is broadcast message", "bool", false),
						new TemplateVariableDescriptor("message.sent_on", "Sent date/time", "datetime", false),
						new TemplateVariableDescriptor("message.type", "Message type value", "int", false),
						new TemplateVariableDescriptor("message.recipients", "Recipients string", "string", false),
						new TemplateVariableDescriptor("message.expire_on", "Expiry date/time", "datetime", false),
					});
					break;

				case WorkflowTriggerEventType.TrainingAdded:
				case WorkflowTriggerEventType.TrainingUpdated:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("training.id", "Training ID", "int", false),
						new TemplateVariableDescriptor("training.name", "Training name", "string", false),
						new TemplateVariableDescriptor("training.description", "Training description", "string", false),
						new TemplateVariableDescriptor("training.training_text", "Training text/content", "string", false),
						new TemplateVariableDescriptor("training.minimum_score", "Minimum passing score", "double", false),
						new TemplateVariableDescriptor("training.created_on", "Created date", "datetime", false),
						new TemplateVariableDescriptor("training.to_be_completed_by", "Completion deadline", "datetime", false),
					});
					break;

				case WorkflowTriggerEventType.InventoryAdjusted:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("inventory.id", "Deprecated alias: transaction GUID for modern events, integer inventory ID for historical events; use inventory.transaction_id", "string", false),
						new TemplateVariableDescriptor("inventory.type_name", "Deprecated item name alias; REDACTED for modern events", "string", false),
						new TemplateVariableDescriptor("inventory.type_description", "Legacy type description; REDACTED for modern events", "string", false),
						new TemplateVariableDescriptor("inventory.unit_of_measure", "Legacy unit of measure; empty for modern events", "string", false),
						new TemplateVariableDescriptor("inventory.batch", "Legacy batch identifier; REDACTED for modern events", "string", false),
						new TemplateVariableDescriptor("inventory.note", "Legacy note; REDACTED for modern events", "string", false),
						new TemplateVariableDescriptor("inventory.location", "Deprecated location alias: destination GUID when present, otherwise source GUID", "string", false),
						new TemplateVariableDescriptor("inventory.amount", "Deprecated balance alias: destination after quantity when present, otherwise source after quantity; use the explicit from/to quantity variables", "decimal", false),
						new TemplateVariableDescriptor("inventory.previous_amount", "Deprecated balance alias: before quantity for the same location as inventory.amount", "decimal", false),
						new TemplateVariableDescriptor("inventory.timestamp", "Deprecated alias for inventory.occurred_on", "datetime", false),
						new TemplateVariableDescriptor("inventory.group_id", "Legacy group ID; zero for modern events", "int", false),
					});
					goto case WorkflowTriggerEventType.InventoryTransferCompleted;
				case WorkflowTriggerEventType.InventoryTransferCompleted:
				case WorkflowTriggerEventType.InventoryIssued:
				case WorkflowTriggerEventType.InventoryReturned:
				case WorkflowTriggerEventType.InventoryAssetStatusChanged:
				case WorkflowTriggerEventType.ControlledSubstanceRecorded:
					foreach (var pair in Inventories.InventoryWorkflowPayload.Variables)
					{
						var type = pair.Variable switch
						{
							"transaction_type" or "previous_status" or "status" or "reference_type" => "int",
							"quantity" or "from_quantity_before" or "from_quantity_after" or "to_quantity_before" or "to_quantity_after" => "decimal",
							"occurred_on" => "datetime",
							_ => "string"
						};
						list.Add(new TemplateVariableDescriptor("inventory." + pair.Variable, pair.Property + (pair.Variable == "item_name" ? "; always REDACTED" : string.Empty), type, false));
					}
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("event.id", "Stable event ID across delivery attempts", "string", false),
						new TemplateVariableDescriptor("event.name", "Inventory trigger name", "string", false),
						new TemplateVariableDescriptor("event.schema_version", "Inventory payload schema version", "int", false),
						new TemplateVariableDescriptor("event.occurred_on", "When the event occurred (UTC)", "datetime", false),
						new TemplateVariableDescriptor("event.correlation_id", "Inventory operation correlation ID", "string", false),
						new TemplateVariableDescriptor("event.causation_id", "ID of the event that caused this event", "string", false),
						new TemplateVariableDescriptor("event.sequence", "Sequence within the inventory aggregate", "int", false),
						new TemplateVariableDescriptor("event.is_replay", "Whether this delivery is a retry", "bool", false),
						new TemplateVariableDescriptor("event.origin_client", "Originating Resgrid client", "string", false),
						new TemplateVariableDescriptor("protection.is_redacted", "Inventory personnel and authored content are always withheld", "bool", false),
						new TemplateVariableDescriptor("protection.redacted_fields", "Withheld inventory fields", "array", false),
						new TemplateVariableDescriptor("protection.catalog_version", "Inventory protection catalog version", "int", false),
					});
					break;

				case WorkflowTriggerEventType.CertificationExpiring:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("certification.id", "Certification ID", "int", false),
						new TemplateVariableDescriptor("certification.name", "Certification name", "string", false),
						new TemplateVariableDescriptor("certification.number", "Certification number", "string", false),
						new TemplateVariableDescriptor("certification.type", "Certification type", "string", false),
						new TemplateVariableDescriptor("certification.area", "Certification area", "string", false),
						new TemplateVariableDescriptor("certification.issued_by", "Issuing authority", "string", false),
						new TemplateVariableDescriptor("certification.expires_on", "Expiry date", "datetime", false),
						new TemplateVariableDescriptor("certification.received_on", "Received date", "datetime", false),
						new TemplateVariableDescriptor("certification.days_until_expiry", "Days until expiry", "int", false),
					});
					break;

				case WorkflowTriggerEventType.FormSubmitted:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("form.id", "Form ID", "string", false),
						new TemplateVariableDescriptor("form.name", "Form name", "string", false),
						new TemplateVariableDescriptor("form.type", "Form type value", "int", false),
						new TemplateVariableDescriptor("form.submitted_data", "Submitted form data JSON", "string", false),
						new TemplateVariableDescriptor("form.submitted_by_user_id", "User ID who submitted", "string", false),
						new TemplateVariableDescriptor("form.submitted_on", "Submission date/time", "datetime", false),
					});
					break;

				case WorkflowTriggerEventType.PersonnelRoleChanged:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("role_change.user_id", "User ID whose role changed", "string", false),
						new TemplateVariableDescriptor("role_change.role_id", "Role ID", "int", false),
						new TemplateVariableDescriptor("role_change.role_name", "Role name", "string", false),
						new TemplateVariableDescriptor("role_change.role_description", "Role description", "string", false),
						new TemplateVariableDescriptor("role_change.action", "Action: Added or Removed", "string", false),
					});
					break;

				case WorkflowTriggerEventType.GroupAdded:
				case WorkflowTriggerEventType.GroupUpdated:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("group.id", "Group ID", "int", false),
						new TemplateVariableDescriptor("group.name", "Group name", "string", false),
						new TemplateVariableDescriptor("group.type", "Group type", "int", false),
						new TemplateVariableDescriptor("group.dispatch_email", "Dispatch email", "string", false),
						new TemplateVariableDescriptor("group.message_email", "Message email", "string", false),
						new TemplateVariableDescriptor("group.latitude", "Latitude", "string", false),
						new TemplateVariableDescriptor("group.longitude", "Longitude", "string", false),
						new TemplateVariableDescriptor("group.what3words", "What3Words location", "string", false),
						new TemplateVariableDescriptor("group.address.street", "Street address", "string", false),
						new TemplateVariableDescriptor("group.address.city", "City", "string", false),
						new TemplateVariableDescriptor("group.address.state", "State", "string", false),
						new TemplateVariableDescriptor("group.address.postal_code", "Postal code", "string", false),
						new TemplateVariableDescriptor("group.address.country", "Country", "string", false),
					});
					break;

				case WorkflowTriggerEventType.RunCardActivated:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("run_card.call_id", "Call ID", "int", false),
						new TemplateVariableDescriptor("run_card.run_card_id", "Run card ID", "int", false),
						new TemplateVariableDescriptor("run_card.run_card_name", "Run card name", "string", false),
						new TemplateVariableDescriptor("run_card.alarm_level", "Alarm level", "int", false),
						new TemplateVariableDescriptor("run_card.mode", "Dispatch mode used (1 = station based, 2 = closest unit)", "int", false),
						new TemplateVariableDescriptor("run_card.was_auto_dispatched", "True when resources were auto-dispatched", "bool", false),
						new TemplateVariableDescriptor("run_card.unit_count", "Number of units recommended", "int", false),
						new TemplateVariableDescriptor("run_card.personnel_count", "Number of personnel recommended", "int", false),
					});
					break;

				case WorkflowTriggerEventType.CallAlarmEscalated:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("escalation.call_id", "Call ID", "int", false),
						new TemplateVariableDescriptor("escalation.previous_alarm_level", "Alarm level before escalation", "int", false),
						new TemplateVariableDescriptor("escalation.new_alarm_level", "Alarm level after escalation", "int", false),
						new TemplateVariableDescriptor("escalation.added_unit_count", "Units added by the escalation", "int", false),
						new TemplateVariableDescriptor("escalation.added_personnel_count", "Personnel added by the escalation", "int", false),
					});
					break;

				case WorkflowTriggerEventType.DispatchShortfallDetected:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("shortfall.call_id", "Call ID", "int", false),
						new TemplateVariableDescriptor("shortfall.run_card_id", "Run card ID", "int", false),
						new TemplateVariableDescriptor("shortfall.alarm_level", "Alarm level", "int", false),
						new TemplateVariableDescriptor("shortfall.shortfall_count", "Number of unfilled requirements", "int", false),
						new TemplateVariableDescriptor("shortfall.summary", "Human-readable shortfall summary", "string", false),
					});
					break;

				case WorkflowTriggerEventType.StationCoverageGapDetected:
					list.AddRange(new[]
					{
						new TemplateVariableDescriptor("coverage_gap.call_id", "Call ID that triggered the gap (0 when none)", "int", false),
						new TemplateVariableDescriptor("coverage_gap.gap_count", "Number of stations below minimum coverage", "int", false),
						new TemplateVariableDescriptor("coverage_gap.summary", "Human-readable coverage gap summary", "string", false),
					});
					break;

				case WorkflowTriggerEventType.RecordCreated:
				case WorkflowTriggerEventType.RecordSubmittedForReview:
				case WorkflowTriggerEventType.RecordReturnedForCorrection:
				case WorkflowTriggerEventType.RecordApproved:
				case WorkflowTriggerEventType.RecordFinalized:
				case WorkflowTriggerEventType.RecordAmended:
				case WorkflowTriggerEventType.RecordVoided:
				case WorkflowTriggerEventType.RecordCancelled:
					list.AddRange(RecordEventVariables);
					list.AddRange(RecordVariables);
					list.Add(new TemplateVariableDescriptor("record.kind", "Record kind (Operational, IncidentReport or IncidentAnalysis)", "string", false));
					list.AddRange(RecordChangeVariables);
					if (eventType == WorkflowTriggerEventType.RecordCancelled)
						list.Add(new TemplateVariableDescriptor("record_change.number_disposition", "What happened to a reserved record number (none or voided)", "string", false));
					if (eventType == WorkflowTriggerEventType.RecordSubmittedForReview || eventType == WorkflowTriggerEventType.RecordReturnedForCorrection)
						list.AddRange(ReviewVariables);
					if (eventType == WorkflowTriggerEventType.RecordApproved)
					{
						list.Add(new TemplateVariableDescriptor("review.reviewer_user_id", "Reviewer user ID", "string", false));
						list.Add(new TemplateVariableDescriptor("review.approver_user_id", "Approving user ID", "string", false));
						list.Add(new TemplateVariableDescriptor("review.approved_on", "When the record was approved (UTC)", "datetime", false));
						list.Add(new TemplateVariableDescriptor("review.submitted_for_review_on", "When it was submitted for review (UTC)", "datetime", false));
						list.Add(new TemplateVariableDescriptor("review.review_due_on", "When the review was due (UTC)", "datetime", false));
						list.Add(new TemplateVariableDescriptor("review.return_count", "How many times it was returned", "int", false));
					}
					list.AddRange(DefinitionVariables);
					list.AddRange(FieldsVariables);
					list.AddRange(ProtectionVariables);
					break;

				case WorkflowTriggerEventType.RecordDefinitionPublished:
				case WorkflowTriggerEventType.RecordDefinitionRetired:
					list.AddRange(RecordEventVariables);
					list.AddRange(DefinitionVariables);
					list.AddRange(ProtectionVariables);
					break;

				case WorkflowTriggerEventType.RecordAttachmentAdded:
					list.AddRange(RecordEventVariables);
					list.AddRange(RecordVariables);
					list.Add(new TemplateVariableDescriptor("record.kind", "Record kind (Operational or IncidentReport)", "string", false));
					list.AddRange(RecordChangeVariables);
					list.AddRange(AttachmentVariables);
					list.AddRange(ProtectionVariables);
					break;

				case WorkflowTriggerEventType.RecordDisclosureRequested:
				case WorkflowTriggerEventType.RecordDisclosureProduced:
				case WorkflowTriggerEventType.RecordDisclosureReleased:
				case WorkflowTriggerEventType.RecordDisclosureClosed:
					list.AddRange(RecordEventVariables);
					list.Add(new TemplateVariableDescriptor("record.kind", "Always Disclosure", "string", false));
					list.Add(new TemplateVariableDescriptor("record.department_id", "Department ID", "int", false));
					list.AddRange(DisclosureVariables);
					list.AddRange(ProtectionVariables);
					break;

				case WorkflowTriggerEventType.RecordLegalHoldPlaced:
				case WorkflowTriggerEventType.RecordLegalHoldReleased:
					list.AddRange(RecordEventVariables);
					list.AddRange(RecordVariables);
					list.Add(new TemplateVariableDescriptor("record.kind", "Record kind of the held record (empty for a definition/date scope)", "string", false));
					list.AddRange(LegalHoldVariables);
					list.AddRange(ProtectionVariables);
					break;

				case WorkflowTriggerEventType.RecordEvidenceCaptured:
					list.AddRange(RecordEventVariables);
					list.AddRange(RecordVariables);
					list.Add(new TemplateVariableDescriptor("record.kind", "Record kind (Operational or IncidentReport)", "string", false));
					list.AddRange(EvidenceVariables);
					list.AddRange(ProtectionVariables);
					break;

				case WorkflowTriggerEventType.RecordPurged:
					list.AddRange(RecordEventVariables);
					list.Add(new TemplateVariableDescriptor("record.id", "Purged record ID", "string", false));
					list.Add(new TemplateVariableDescriptor("record.kind", "Record kind (Operational or IncidentReport)", "string", false));
					list.Add(new TemplateVariableDescriptor("record.department_id", "Department ID", "int", false));
					list.Add(new TemplateVariableDescriptor("record.state", "Always Purged", "string", false));
					list.AddRange(PurgeVariables);
					list.AddRange(ProtectionVariables);
					break;

				case WorkflowTriggerEventType.RecordExportScheduled:
					list.AddRange(RecordEventVariables);
					list.Add(new TemplateVariableDescriptor("record.kind", "Always Export", "string", false));
					list.Add(new TemplateVariableDescriptor("record.department_id", "Department ID", "int", false));
					list.AddRange(ExportVariables);
					list.AddRange(ProtectionVariables);
					break;

				case WorkflowTriggerEventType.RecordInspectionCompleted:
					list.AddRange(RecordEventVariables);
					list.Add(new TemplateVariableDescriptor("record.kind", "Always Prevention", "string", false));
					list.Add(new TemplateVariableDescriptor("record.department_id", "Department ID", "int", false));
					list.AddRange(InspectionVariables);
					list.AddRange(ProtectionVariables);
					break;

				case WorkflowTriggerEventType.RecordViolationOverdue:
					list.AddRange(RecordEventVariables);
					list.Add(new TemplateVariableDescriptor("record.kind", "Always Prevention", "string", false));
					list.Add(new TemplateVariableDescriptor("record.department_id", "Department ID", "int", false));
					list.AddRange(ViolationVariables);
					list.AddRange(ProtectionVariables);
					break;

				case WorkflowTriggerEventType.RecordPermitExpiring:
					list.AddRange(RecordEventVariables);
					list.Add(new TemplateVariableDescriptor("record.kind", "Always Prevention", "string", false));
					list.Add(new TemplateVariableDescriptor("record.department_id", "Department ID", "int", false));
					list.AddRange(PermitVariables);
					list.AddRange(ProtectionVariables);
					break;

				case WorkflowTriggerEventType.RecordSubmissionQueued:
				case WorkflowTriggerEventType.RecordSubmissionAccepted:
				case WorkflowTriggerEventType.RecordSubmissionRejected:
				case WorkflowTriggerEventType.RecordSubmissionFailed:
					list.AddRange(RecordEventVariables);
					list.AddRange(RecordVariables);
					list.Add(new TemplateVariableDescriptor("record.kind", "Record kind (Operational, IncidentReport or IncidentAnalysis)", "string", false));
					list.Add(new TemplateVariableDescriptor("record.incident_number", "Department incident number sent to the destination", "string", false));
					list.Add(new TemplateVariableDescriptor("record.neris_incident_id", "NERIS incident ID once assigned", "string", false));
					list.AddRange(RecordChangeVariables);
					list.AddRange(SubmissionVariables);
					list.AddRange(ProtectionVariables);
					break;

				case WorkflowTriggerEventType.RecordOverdue:
					list.AddRange(RecordEventVariables);
					list.AddRange(RecordVariables);
					list.Add(new TemplateVariableDescriptor("record.kind", "Record kind (Operational or IncidentReport)", "string", false));
					list.AddRange(ObligationVariables);
					list.AddRange(ProtectionVariables);
					break;
			}

			return list.AsReadOnly();
		}
	}

	internal static class ListExtensions
	{
		internal static List<T> Also<T>(this List<T> list, System.Action<List<T>> action)
		{
			action(list);
			return list;
		}
	}
}
