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

		public static IReadOnlyList<TemplateVariableDescriptor> GetVariableCatalog(WorkflowTriggerEventType eventType)
		{
			var list = GetCommon();

			switch (eventType)
			{
				case WorkflowTriggerEventType.CallAdded:
				case WorkflowTriggerEventType.CallUpdated:
				case WorkflowTriggerEventType.CallClosed:
					list.AddRange(CallVariables);
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
						new TemplateVariableDescriptor("inventory.id", "Inventory record ID", "int", false),
						new TemplateVariableDescriptor("inventory.type_name", "Inventory type name", "string", false),
						new TemplateVariableDescriptor("inventory.type_description", "Inventory type description", "string", false),
						new TemplateVariableDescriptor("inventory.unit_of_measure", "Unit of measure", "string", false),
						new TemplateVariableDescriptor("inventory.batch", "Batch identifier", "string", false),
						new TemplateVariableDescriptor("inventory.note", "Note", "string", false),
						new TemplateVariableDescriptor("inventory.location", "Storage location", "string", false),
						new TemplateVariableDescriptor("inventory.amount", "Current amount", "double", false),
						new TemplateVariableDescriptor("inventory.previous_amount", "Previous amount before adjustment", "double", false),
						new TemplateVariableDescriptor("inventory.timestamp", "Adjustment timestamp", "datetime", false),
						new TemplateVariableDescriptor("inventory.group_id", "Group ID", "int", false),
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

