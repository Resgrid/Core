using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>How sensitive an export column is; decides which template flags and acknowledgements it needs (RMS plan section 5.9.2).</summary>
	public enum RmsExportFieldTier
	{
		/// <summary>Identity, dates, codes, counts: safe in any export.</summary>
		Safe = 0,

		/// <summary>Narrative-class free text and personal detail (Tier 2): opt-in, egress acknowledged, withheld under ADP enforcement.</summary>
		Narrative = 1,

		/// <summary>Restricted sections (Tier 1): opt-in, egress acknowledged, RecordRestricted_View to author, withheld under ADP enforcement.</summary>
		Restricted = 2
	}

	public sealed class RecordsExportField
	{
		public RecordsExportField(string key, string label, RmsExportFieldTier tier, bool operational = true, bool incident = true)
		{
			Key = key;
			Label = label;
			Tier = tier;
			Operational = operational;
			Incident = incident;
		}

		/// <summary>Stable column key, e.g. <c>record.number</c>; also the CSV header and JSON property.</summary>
		public string Key { get; }
		public string Label { get; }
		public RmsExportFieldTier Tier { get; }
		/// <summary>Applies to locked operational definitions.</summary>
		public bool Operational { get; }
		/// <summary>Applies to NERIS incident reports.</summary>
		public bool Incident { get; }
	}

	/// <summary>
	/// The finite, reviewed set of columns a department export may carry (RMS plan section 5.6: "the base
	/// catalog remains finite"). A template names keys from here; anything else is rejected at save. Keys are
	/// stable identifiers, never labels, so a renamed label never breaks an agency's import.
	/// </summary>
	public static class RecordsExportFieldCatalog
	{
		public static readonly IReadOnlyList<RecordsExportField> Fields = new List<RecordsExportField>
		{
			new RecordsExportField("record.id", "Record ID", RmsExportFieldTier.Safe),
			new RecordsExportField("record.kind", "Record kind", RmsExportFieldTier.Safe),
			new RecordsExportField("record.number", "Record number", RmsExportFieldTier.Safe),
			new RecordsExportField("record.definition_key", "Definition key", RmsExportFieldTier.Safe),
			new RecordsExportField("record.type", "Record type", RmsExportFieldTier.Safe),
			new RecordsExportField("record.state", "State", RmsExportFieldTier.Safe),
			new RecordsExportField("record.revision_number", "Revision number", RmsExportFieldTier.Safe),
			new RecordsExportField("record.revision_checksum", "Revision checksum", RmsExportFieldTier.Safe),
			new RecordsExportField("record.station_group_id", "Station/group ID", RmsExportFieldTier.Safe),
			new RecordsExportField("record.station_group_name", "Station/group name", RmsExportFieldTier.Safe),
			new RecordsExportField("record.author_user_id", "Author user ID", RmsExportFieldTier.Safe),
			new RecordsExportField("record.author_name", "Author", RmsExportFieldTier.Safe),
			new RecordsExportField("record.started_on", "Start (UTC)", RmsExportFieldTier.Safe),
			new RecordsExportField("record.ended_on", "End (UTC)", RmsExportFieldTier.Safe),
			new RecordsExportField("record.duration_minutes", "Duration (minutes)", RmsExportFieldTier.Safe),
			new RecordsExportField("record.created_on", "Created (UTC)", RmsExportFieldTier.Safe),
			new RecordsExportField("record.finalized_on", "Finalized (UTC)", RmsExportFieldTier.Safe),
			new RecordsExportField("record.external_id", "External ID", RmsExportFieldTier.Safe),
			new RecordsExportField("call.id", "Call ID", RmsExportFieldTier.Safe),
			new RecordsExportField("call.number", "Call number", RmsExportFieldTier.Safe),
			new RecordsExportField("call.type", "Call type", RmsExportFieldTier.Safe),
			new RecordsExportField("call.priority", "Call priority", RmsExportFieldTier.Safe),
			new RecordsExportField("call.logged_on", "Call logged (UTC)", RmsExportFieldTier.Safe),
			new RecordsExportField("call.name", "Call name", RmsExportFieldTier.Narrative),
			new RecordsExportField("call.address", "Call address", RmsExportFieldTier.Narrative),
			new RecordsExportField("call.nature", "Nature of call", RmsExportFieldTier.Narrative),
			new RecordsExportField("participants.count", "Participant count", RmsExportFieldTier.Safe),
			new RecordsExportField("participants.user_ids", "Participant user IDs", RmsExportFieldTier.Safe),
			new RecordsExportField("participants.names", "Participants", RmsExportFieldTier.Safe),
			new RecordsExportField("units.count", "Unit count", RmsExportFieldTier.Safe),
			new RecordsExportField("units.names", "Units", RmsExportFieldTier.Safe),
			new RecordsExportField("units.first_dispatched", "First unit dispatched (UTC)", RmsExportFieldTier.Safe),
			new RecordsExportField("units.first_on_scene", "First unit on scene (UTC)", RmsExportFieldTier.Safe),
			new RecordsExportField("units.last_cleared", "Last unit cleared (UTC)", RmsExportFieldTier.Safe),
			new RecordsExportField("attachments.count", "Attachment count", RmsExportFieldTier.Safe),
			new RecordsExportField("details.type", "Type", RmsExportFieldTier.Safe, incident: false),
			new RecordsExportField("details.course", "Course", RmsExportFieldTier.Safe, incident: false),
			new RecordsExportField("details.course_code", "Course code", RmsExportFieldTier.Safe, incident: false),
			new RecordsExportField("details.instructors", "Instructors", RmsExportFieldTier.Safe, incident: false),
			new RecordsExportField("details.facilitator", "Facilitator", RmsExportFieldTier.Safe, incident: false),
			new RecordsExportField("details.other_agencies", "Other agencies", RmsExportFieldTier.Safe, incident: false),
			new RecordsExportField("details.other_units", "Other units", RmsExportFieldTier.Safe, incident: false),
			new RecordsExportField("details.unit_id", "Activity unit ID", RmsExportFieldTier.Safe, incident: false),
			new RecordsExportField("details.activity_on", "Activity time (UTC)", RmsExportFieldTier.Safe, incident: false),
			new RecordsExportField("details.narrative", "Narrative", RmsExportFieldTier.Narrative, incident: false),
			new RecordsExportField("details.initial_report", "Initial report", RmsExportFieldTier.Narrative, incident: false),
			new RecordsExportField("details.cause", "Cause", RmsExportFieldTier.Narrative, incident: false),
			new RecordsExportField("details.location", "Location", RmsExportFieldTier.Narrative, incident: false),
			new RecordsExportField("details.contact_name", "Contact name", RmsExportFieldTier.Narrative, incident: false),
			new RecordsExportField("details.contact_number", "Contact number", RmsExportFieldTier.Narrative, incident: false),
			new RecordsExportField("details.investigated_by_user_id", "Investigated by", RmsExportFieldTier.Safe, incident: false),
			new RecordsExportField("details.other_personnel", "Other personnel", RmsExportFieldTier.Restricted, incident: false),
			new RecordsExportField("details.body_location", "Body location", RmsExportFieldTier.Restricted, incident: false),
			new RecordsExportField("details.pronounced_deceased_by", "Pronounced deceased by", RmsExportFieldTier.Restricted, incident: false),
			new RecordsExportField("details.case_number", "Case number", RmsExportFieldTier.Restricted, incident: false),
			new RecordsExportField("details.destination", "Destination", RmsExportFieldTier.Restricted, incident: false),
			new RecordsExportField("incident.number", "Incident number", RmsExportFieldTier.Safe, operational: false),
			new RecordsExportField("incident.reporting_entity_id", "Reporting entity ID", RmsExportFieldTier.Safe, operational: false),
			new RecordsExportField("incident.neris_incident_id", "NERIS incident ID", RmsExportFieldTier.Safe, operational: false),
			new RecordsExportField("incident.dispatch_code", "Dispatch incident code", RmsExportFieldTier.Safe, operational: false),
			new RecordsExportField("incident.primary_type", "Primary incident type", RmsExportFieldTier.Safe, operational: false),
			new RecordsExportField("incident.type_codes", "Incident type codes", RmsExportFieldTier.Safe, operational: false),
			new RecordsExportField("incident.call_created_on", "Call created (UTC)", RmsExportFieldTier.Safe, operational: false),
			new RecordsExportField("incident.cleared_on", "Incident cleared (UTC)", RmsExportFieldTier.Safe, operational: false),
			new RecordsExportField("incident.disposition", "Disposition", RmsExportFieldTier.Safe, operational: false),
			new RecordsExportField("incident.last_submission_state", "Last submission state", RmsExportFieldTier.Safe, operational: false),
			new RecordsExportField("incident.aid_count", "Aid count", RmsExportFieldTier.Safe, operational: false),
			new RecordsExportField("incident.tactic_codes", "Action/tactic codes", RmsExportFieldTier.Safe, operational: false),
			new RecordsExportField("incident.location_use", "Location use", RmsExportFieldTier.Safe, operational: false),
			new RecordsExportField("incident.address", "Incident address", RmsExportFieldTier.Narrative, operational: false),
			new RecordsExportField("incident.narrative", "Narrative", RmsExportFieldTier.Narrative, operational: false),
			new RecordsExportField("incident.casualty_count", "Casualty/rescue count", RmsExportFieldTier.Safe, operational: false),
			new RecordsExportField("incident.exposure_count", "Exposure count", RmsExportFieldTier.Safe, operational: false)
		};

		private static readonly Dictionary<string, RecordsExportField> ByKey = Fields.ToDictionary(f => f.Key, StringComparer.Ordinal);

		public static RecordsExportField Get(string key) => key != null && ByKey.TryGetValue(key, out var field) ? field : null;

		public static bool IsKnown(string key) => key != null && ByKey.ContainsKey(key);

		/// <summary>Default column set for a new template: the safe identity/time columns any agency report starts from.</summary>
		public static readonly string[] DefaultColumns =
		{
			"record.number", "record.kind", "record.type", "record.state", "record.started_on", "record.ended_on", "record.finalized_on",
			"record.station_group_name", "record.author_name", "call.number", "call.type", "units.names", "participants.count"
		};
	}

	/// <summary>Everything a render needs beyond the template; the service fills what the caller leaves null.</summary>
	public sealed class RecordsExportRequest
	{
		public RmsExportTrigger Trigger { get; set; } = RmsExportTrigger.Manual;

		/// <summary>TriggeringRecord scope: the one record to export.</summary>
		public string RecordId { get; set; }

		public RmsRecordKind? RecordKind { get; set; }

		/// <summary>Window scope: finalized-on window (UTC); defaults to the template's schedule period ending now.</summary>
		public DateTime? WindowStart { get; set; }

		public DateTime? WindowEnd { get; set; }

		/// <summary>The acting member for an attended render; null for the worker. A member sees only records the queue would show them.</summary>
		public string ActingUserId { get; set; }

		public string WorkflowRunId { get; set; }

		/// <summary>Audit purpose text, e.g. "Workflow export {name}".</summary>
		public string Purpose { get; set; }
	}

	/// <summary>Template validation outcome; a template with any error is never saved.</summary>
	public sealed class RecordsExportTemplateValidation
	{
		public List<string> Errors { get; } = new List<string>();
		public List<string> Warnings { get; } = new List<string>();
		public bool IsValid => Errors.Count == 0;
	}

	/// <summary>Result of the worker 45 sweep.</summary>
	public sealed class RecordsExportScheduleSweepResult
	{
		public int TemplatesEvaluated { get; set; }
		public int RunsRendered { get; set; }
		public int Errors { get; set; }
		public List<string> RunIds { get; } = new List<string>();
	}
}
