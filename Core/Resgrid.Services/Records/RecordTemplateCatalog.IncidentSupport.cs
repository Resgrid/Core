using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// The Incident Support pack (Back Office plan section 10A.2, extension E5): the ICS/NWCG planning, resource,
	/// communications, safety, incident-business, support-operations and business-administration records of a large
	/// incident, published through the ordinary RMS pack pipeline as product content rather than as schema.
	///
	/// Three rules shape everything below.
	///
	/// 1. Web only. Every definition ships <see cref="RecordDefinitionClientSurface.WebOnly"/> (extension E7). These
	///    are desk products authored in a planning section, not phone forms; the four field apps exclude them with
	///    SurfaceNotEnabled and nothing here is ever written to a device.
	/// 2. Incident-scoped numbering (extension E2). The third ICS 214 on an incident is 003 on that incident, not
	///    003 for the department this year.
	/// 3. The ledger is authoritative (section 10A.0). Time, equipment use, expense, cost and lodging arithmetic
	///    belongs to the Incident Back Office. Where a form below carries those numbers it carries the entry made on
	///    the incident plus an external reference to the authoritative row; when that ledger ships, the reference is
	///    what a rendered total is computed from. Nothing here becomes a second place a bed-night is counted.
	///
	/// Nothing in this pack is an exact named form. It ships Preview and carries no claim that NWCG, FEMA, Cal OES,
	/// CIFFC or any member agency accepts its output, until a real artifact has been produced and reconciled.
	/// </summary>
	public static partial class RecordTemplateCatalog
	{
		public const string IncidentSupportPackKey = "pack.incident-support";

		/// <summary>A pack definition: Web-only surface and incident-scoped numbering, per the pack rules above.</summary>
		private static RecordTemplateDefinition Ics(string key, string name, string category, string description, RmsLifecyclePreset preset, string prefix, string subjects, params RecordSectionSchema[] sections)
		{
			var template = Template(IncidentSupportPackKey + "." + key, IncidentSupportPackKey, name, category, description, preset, prefix, subjects, sections);
			template.ClientSurface = RecordDefinitionClientSurface.WebOnly();
			template.PerIncidentSequence = true;
			template.RetentionYears = 7;
			return template;
		}

		/// <summary>
		/// The plan products a period has exactly one of (§10A.2). A second ICS 202 for the same operational period
		/// is an error rather than a second opinion, so these declare `OnePerSubjectPerCall`; everything else in the
		/// pack — messages, tickets, activity logs, time reports — is legitimately many per incident.
		/// </summary>
		/// <summary>
		/// A local rather than a static field on purpose: static initializers across the two halves of this partial
		/// class run in an order the compiler picks, and `Packs` in the other half would read this one while it was
		/// still null.
		/// </summary>
		private static string[] OnePerOperationalPeriod() => new[]
		{
			"ics-202-objectives", "ics-203-organization", "ics-204-assignment", "ics-207-org-chart", "iap-package",
			"ics-205-comms-plan", "ics-205a-comms-list", "ics-217a-frequency-inventory",
			"ics-206-medical-plan", "ics-208-safety-message", "ics-215a-hazard-analysis",
			"ics-215-planning-worksheet", "ics-220-air-operations"
		};

		/// <summary>The header every incident product carries: which incident, which operational period, who prepared it.</summary>
		private static RecordSectionSchema Header(bool withPeriod = true)
		{
			var fields = new List<RecordFieldSchema>
			{
				F("incident_name", "Incident name", RmsFieldType.ShortText, true, true, true),
				F("incident_number", "Incident number", RmsFieldType.ShortText, false, true, true)
			};
			if (withPeriod)
			{
				fields.Add(F("operational_period", "Operational period", RmsFieldType.ShortText, true, true, true));
				fields.Add(F("period_from", "Period from", RmsFieldType.DateTime, true));
				fields.Add(F("period_to", "Period to", RmsFieldType.DateTime, true));
			}
			fields.Add(F("prepared_by", "Prepared by", RmsFieldType.Person, true, true, true));
			fields.Add(F("prepared_position", "Position/title", RmsFieldType.ShortText, false, true));
			fields.Add(F("prepared_on", "Date/time prepared", RmsFieldType.DateTime, true));
			return new RecordSectionSchema { Key = "header", Label = "Incident", Fields = fields };
		}

		private static RecordSectionSchema Approval(string label)
			=> Section("approval", label,
				F("approved_by", "Approved by", RmsFieldType.Person, false, true, true),
				F("approved_position", "Position/title", RmsFieldType.ShortText),
				F("approved_on", "Approved on", RmsFieldType.DateTime),
				F("approval_signature", "Signature", RmsFieldType.Signature, true));

		private const string SubjectsPeriod = "call,incidentcommand,incidentoperationalperiod";
		private const string SubjectsResource = "call,incidentcommand,incidentresource,incidentresourcerequest,unit";
		private const string SubjectsParticipant = "call,incidentcommand,incidentparticipant,person,unit";
		private const string SubjectsBusiness = "call,incidentcommand,incidentparticipant,person,unit,vendor";
		private const string SubjectsFacility = "call,incidentcommand,incidentfacility,vendor";
		private const string SubjectsCommand = "call,incidentcommand,vendor";

		private static RecordTemplatePack IncidentSupportPack()
		{
			var pack = new RecordTemplatePack
			{
				Key = IncidentSupportPackKey,
				Name = "Incident Support (ICS Logistics, Finance and Administration)",
				Category = RecordDefinitionCategories.IncidentSupport,
				Description = "ICS planning, resource, communications, safety, incident-business, support-operations and business-administration records for a large incident. Web-authored with incident-scoped numbering; the owning ledger stays authoritative for time, equipment, expense and cost arithmetic.",
				IsPreview = true,
				ArtifactStatus = RmsArtifactStatus.Compatible,
				SupportedProfiles = new List<string> { "generic", "us-nwcg", "us-nims", "us-calif", "ca" },
				SupportedLocales = new List<string> { "en-US", "en-CA", "fr-CA" },
				ReleaseNotes = "Initial release (Back Office plan E5). Preview: compatible with the published ICS/NWCG artifact set, not an exact named form, and no claim of agency acceptance.",
				Sources = new List<RmsSourceProvenance>
				{
					Source("NIMS/ICS forms", "FEMA National Incident Management System ICS forms", "FEMA", "2023", "https://www.fema.gov/emergency-managers/nims/components", "named-form"),
					Source("NWCG PMS 902", "Interagency Incident Business Management Handbook", "NWCG", "2025", "https://www.nwcg.gov/publications/902", "named-form"),
					Source("NWCG PMS 310-1", "NWCG Standards for Wildland Fire Position Qualifications", "NWCG", "2025", "https://www.nwcg.gov/publications/pms310-1"),
					Source("FIRESCOPE FOG", "FIRESCOPE Field Operations Guide ICS 420-1", "FIRESCOPE", "2024", "https://firescope.caloes.ca.gov/ics-documents"),
					Source("ICS Canada forms", "ICS Canada incident forms", "ICS Canada", "2024", "https://www.icscanada.ca/")
				}
			};

			foreach (var definition in IapCore().Concat(StatusReporting()).Concat(Communications()).Concat(MedicalAndSafety())
				.Concat(ResourceForms()).Concat(ActivityAndPlanning()).Concat(IncidentBusinessForms()).Concat(SupportOperations()).Concat(BusinessAdministration()))
				pack.Definitions.Add(definition);

			foreach (var key in OnePerOperationalPeriod())
			{
				var definition = pack.Definitions.SingleOrDefault(d => d.Key == IncidentSupportPackKey + "." + key)
					?? throw new InvalidOperationException($"'{key}' is not in the Incident Support pack.");
				definition.Cardinality = RmsRecordCardinality.OnePerSubjectPerCall;
			}

			return pack;
		}

		// ---- IAP core -------------------------------------------------------------------------------------------

		private static IEnumerable<RecordTemplateDefinition> IapCore()
		{
			yield return Ics("ics-202-objectives", "ICS 202 Incident Objectives", RecordDefinitionCategories.IncidentSupport,
				"Objectives, command emphasis and the plan attachments for one operational period.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "ICS202", SubjectsPeriod,
				Header(),
				Rows("objectives", "Objectives", 1, 50,
					Count("priority", "Priority"), F("objective", "Objective", RmsFieldType.LongText, true),
					Select("status", "Status", false, true, "Not started", "In progress", "Met", "Carried forward")),
				Section("emphasis", "Command emphasis",
					F("command_emphasis", "Command emphasis", RmsFieldType.LongText), F("situational_awareness", "General situational awareness", RmsFieldType.LongText),
					F("site_safety_plan_required", "Site safety plan required", RmsFieldType.Boolean, false, false, true), F("site_safety_plan_location", "Site safety plan location", RmsFieldType.ShortText)),
				Section("attachments", "Plan attachments",
					Multi("included_documents", "Documents included", "ICS 203", "ICS 204", "ICS 205", "ICS 205A", "ICS 206", "ICS 207", "ICS 208", "ICS 215A", "ICS 220", "Incident map", "Weather forecast", "Traffic plan"),
					F("attachment", "Attachment", RmsFieldType.Attachment)),
				Approval("Approved by Incident Commander"));

			yield return Ics("ics-203-organization", "ICS 203 Organization Assignment List", RecordDefinitionCategories.IncidentSupport,
				"Command and general staff assignments by position for one operational period.",
				RmsLifecyclePreset.ReviewRequired, "ICS203", SubjectsPeriod,
				Header(),
				Rows("assignments", "Position assignments", 1, 300,
					Select("section", "Section", true, true, "Incident command", "Command staff", "Operations", "Planning", "Logistics", "Finance and administration", "Agency representative"),
					F("position", "Position", RmsFieldType.ShortText, true, true, true), F("member", "Name", RmsFieldType.Person, false, true),
					F("name_text", "Name (not a member)", RmsFieldType.ShortText), F("agency", "Home agency or unit", RmsFieldType.ShortText, false, true),
					F("contact", "Contact", RmsFieldType.ShortText)),
				Section("notes", "Notes", F("notes", "Notes", RmsFieldType.LongText)));

			yield return Ics("ics-204-assignment", "ICS 204 Assignment List", RecordDefinitionCategories.IncidentSupport,
				"Division or group assignment, resources assigned, work assignment, special instructions and communications for one operational period.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "ICS204", SubjectsPeriod,
				Header(),
				Section("branch", "Branch and division",
					F("branch", "Branch", RmsFieldType.ShortText, false, true, true), F("division_group", "Division or group", RmsFieldType.ShortText, true, true, true),
					F("staging_area", "Staging area", RmsFieldType.ShortText), F("operations_chief", "Operations section chief", RmsFieldType.Person, false, true),
					F("branch_director", "Branch director", RmsFieldType.ShortText), F("division_supervisor", "Division or group supervisor", RmsFieldType.ShortText)),
				Rows("resources", "Resources assigned", null, 200,
					F("resource_identifier", "Resource identifier", RmsFieldType.ShortText, true, true), F("unit", "Unit", RmsFieldType.Unit, false, true),
					F("leader", "Leader", RmsFieldType.ShortText), Count("persons", "Persons"), F("contact", "Contact", RmsFieldType.ShortText),
					F("reporting_location", "Reporting location", RmsFieldType.ShortText), F("reporting_time", "Reporting time", RmsFieldType.DateTime),
					Multi("ppe_required", "PPE required", "Structural", "Wildland", "Chemical protective", "Respiratory", "High visibility", "Water rescue", "Fall protection")),
				Section("work", "Work assignment",
					F("work_assignment", "Work assignment", RmsFieldType.LongText, true), F("special_instructions", "Special instructions", RmsFieldType.LongText)),
				Section("communications", "Communications",
					F("command_frequency", "Command", RmsFieldType.ShortText), F("tactical_frequency", "Tactical", RmsFieldType.ShortText),
					F("support_frequency", "Support", RmsFieldType.ShortText), F("emergency_communications", "Emergency communications", RmsFieldType.LongText)),
				Approval("Approved by Planning Section Chief"));

			yield return Ics("ics-207-org-chart", "ICS 207 Incident Organization Chart", RecordDefinitionCategories.IncidentSupport,
				"The wall-chart view of the command organization for one operational period.",
				RmsLifecyclePreset.QuickEntry, "ICS207", SubjectsPeriod,
				Header(),
				Rows("nodes", "Organization", 1, 300,
					F("position", "Position", RmsFieldType.ShortText, true, true), F("reports_to", "Reports to (position)", RmsFieldType.ShortText),
					F("name", "Name", RmsFieldType.ShortText), Count("chart_level", "Chart level")),
				Section("chart", "Chart", F("chart_image", "Chart image", RmsFieldType.Attachment), F("notes", "Notes", RmsFieldType.LongText)));

			yield return Ics("iap-package", "Incident Action Plan package", RecordDefinitionCategories.IncidentSupport,
				"The assembly manifest for one operational period: which records and revisions are in the plan, in what order, and who received it. The pages stay their own Records; this is the cover, the order and the distribution.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "IAP", SubjectsPeriod,
				Header(),
				Rows("contents", "Plan contents", 1, 100,
					Count("page_order", "Order"), F("document", "Document", RmsFieldType.ShortText, true, true),
					Ext("record_reference", "Record", "record"), F("revision_reference", "Revision", RmsFieldType.ShortText),
					Select("inclusion", "Inclusion", false, true, "Included", "Attached separately", "Not applicable")),
				Rows("distribution", "Distribution", null, 200,
					F("recipient", "Recipient", RmsFieldType.ShortText, true), F("position", "Position", RmsFieldType.ShortText),
					Select("method", "Method", false, false, "Printed", "Electronic", "Briefing", "Posted"),
					F("distributed_on", "Distributed on", RmsFieldType.DateTime), Count("copies", "Copies")),
				Section("assembly", "Assembly",
					F("assembled_on", "Assembled on", RmsFieldType.DateTime, true), F("assembly_note", "Assembly note", RmsFieldType.LongText),
					F("packet", "Compiled packet", RmsFieldType.Attachment)),
				Approval("Approved by Incident Commander"));
		}

		// ---- Status reporting -----------------------------------------------------------------------------------

		private static IEnumerable<RecordTemplateDefinition> StatusReporting()
		{
			var summary = Ics("ics-209-status-summary", "ICS 209 Incident Status Summary", RecordDefinitionCategories.IncidentSupport,
				"The reporting-period status summary: size, containment, threats, damage, resources committed, cost to date and the outlook.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "ICS209", SubjectsPeriod,
				Header(),
				Section("report", "Report",
					Select("report_type", "Report type", true, true, "Initial", "Update", "Final"), F("report_number", "Report number", RmsFieldType.ShortText),
					F("reporting_period_from", "Reporting period from", RmsFieldType.DateTime, true), F("reporting_period_to", "Reporting period to", RmsFieldType.DateTime, true),
					Select("incident_kind", "Incident kind", true, true, "Wildfire", "Structure fire", "Flood", "Severe weather", "Earthquake", "HAZMAT", "Search and rescue", "Public health", "Planned event", "Other"),
					F("incident_start", "Incident start", RmsFieldType.DateTime), F("location", "Location", RmsFieldType.Address, false, true),
					F("jurisdiction", "Jurisdiction", RmsFieldType.ShortText, false, true), F("subdivision", "State or province", RmsFieldType.CountrySubdivision)),
				Section("size", "Size and status",
					Quantity("area", "Area affected", "area", "ha"), Count("percent_contained", "Percent contained"),
					Select("containment_trend", "Trend", false, true, "Increasing", "Holding", "Decreasing", "Contained", "Controlled"),
					F("estimated_containment", "Estimated containment", RmsFieldType.DateTime), F("current_situation", "Current situation", RmsFieldType.LongText, true)),
				Section("threats", "Threats and damage",
					F("significant_events", "Significant events", RmsFieldType.LongText), F("primary_threats", "Primary threats over the next period", RmsFieldType.LongText),
					Count("structures_threatened", "Structures threatened"), Count("structures_damaged", "Structures damaged"), Count("structures_destroyed", "Structures destroyed"),
					Count("evacuations", "People evacuated"), Count("injuries", "Injuries this period"), Count("fatalities", "Fatalities this period"),
					F("critical_infrastructure", "Critical infrastructure affected", RmsFieldType.LongText)),
				Rows("committed_resources", "Resources committed", null, 300,
					F("resource_kind", "Kind", RmsFieldType.ShortText, true, true), F("resource_type", "Type", RmsFieldType.ShortText),
					Count("quantity", "Quantity"), Count("personnel", "Personnel"), F("agency", "Agency", RmsFieldType.ShortText, false, true)),
				Section("cost", "Cost and outlook",
					Money("cost_to_date", "Cost to date"), Ext("finance_reference", "Finance posting", RmsExternalReferenceSchemes.FinancePosting),
					F("planned_actions", "Planned actions for the next period", RmsFieldType.LongText), F("resources_needed", "Resources needed", RmsFieldType.LongText),
					F("remarks", "Remarks", RmsFieldType.LongText)),
				Approval("Approved by Incident Commander"));

			// Cost to date is the ledger's number, not the record's: the record carries the reported figure and the
			// posting reference it came from, and the reference is what a rendered total is computed from later.
			yield return Policy(summary, "regulatory", RmsFieldClassification.Restricted,
				"Reported incident cost is an unreleased financial figure until the agency publishes it.", "cost_to_date", "finance_reference");
		}

		// ---- Communications -------------------------------------------------------------------------------------

		private static IEnumerable<RecordTemplateDefinition> Communications()
		{
			yield return Ics("ics-205-comms-plan", "ICS 205 Incident Radio Communications Plan", RecordDefinitionCategories.IncidentSupport,
				"Radio channel assignments for one operational period: function, channel, frequency, mode and assignment.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "ICS205", SubjectsPeriod,
				Header(),
				Rows("channels", "Channel assignments", 1, 200,
					Count("zone_group", "Zone or group"), F("channel_number", "Channel number", RmsFieldType.ShortText),
					Select("function", "Function", true, true, "Command", "Tactical", "Ground to air", "Air to air", "Support", "Logistics", "Medical", "Repeat", "Other"),
					F("channel_name", "Channel name or talkgroup", RmsFieldType.ShortText, true, true), F("assignment", "Assignment", RmsFieldType.ShortText),
					F("rx_frequency", "RX frequency", RmsFieldType.ShortText), F("rx_tone", "RX tone or NAC", RmsFieldType.ShortText),
					F("tx_frequency", "TX frequency", RmsFieldType.ShortText), F("tx_tone", "TX tone or NAC", RmsFieldType.ShortText),
					Select("mode", "Mode", false, false, "Analog", "Digital", "Mixed"), F("remarks", "Remarks", RmsFieldType.LongText)),
				Section("special", "Special instructions",
					F("special_instructions", "Special instructions", RmsFieldType.LongText), F("comms_unit_leader", "Communications unit leader", RmsFieldType.ShortText)),
				Approval("Approved by Communications Unit Leader"));

			yield return Ics("ics-205a-comms-list", "ICS 205A Communications List", RecordDefinitionCategories.IncidentSupport,
				"Who is reachable how: assignment, name and the method that actually reaches them this period.",
				RmsLifecyclePreset.QuickEntry, "I205A", SubjectsPeriod,
				Header(),
				Rows("contacts", "Contacts", 1, 300,
					F("assignment", "Assignment", RmsFieldType.ShortText, true, true), F("name", "Name", RmsFieldType.ShortText, true),
					F("method", "Method of contact", RmsFieldType.ShortText, true), F("notes", "Notes", RmsFieldType.LongText)));

			yield return Ics("ics-217a-frequency-inventory", "ICS 217A Communications Resource Availability Worksheet", RecordDefinitionCategories.IncidentSupport,
				"The frequencies and talkgroups actually available to the incident, and who owns each one.",
				RmsLifecyclePreset.ReviewRequired, "I217A", SubjectsPeriod,
				Header(false),
				Rows("frequencies", "Available frequencies", 1, 300,
					F("channel_name", "Channel name or talkgroup", RmsFieldType.ShortText, true, true), F("owner", "Owner or authority", RmsFieldType.ShortText, false, true),
					F("rx_frequency", "RX frequency", RmsFieldType.ShortText), F("tx_frequency", "TX frequency", RmsFieldType.ShortText),
					Select("mode", "Mode", false, false, "Analog", "Digital", "Mixed"),
					Select("availability", "Availability", true, true, "Available", "Restricted", "On request", "Not available"),
					F("conditions", "Conditions of use", RmsFieldType.LongText)));
		}

		// ---- Medical and safety ---------------------------------------------------------------------------------

		private static IEnumerable<RecordTemplateDefinition> MedicalAndSafety()
		{
			var medical = Ics("ics-206-medical-plan", "ICS 206 Medical Plan", RecordDefinitionCategories.IncidentSupport,
				"Incident medical aid stations, transport, hospitals and the emergency procedure for one operational period.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "ICS206", SubjectsPeriod,
				Header(),
				Rows("aid_stations", "Medical aid stations", null, 50,
					F("station_name", "Name", RmsFieldType.ShortText, true, true), F("station_location", "Location", RmsFieldType.Address),
					F("station_contact", "Contact", RmsFieldType.ShortText), Select("paramedics", "Paramedics on site", false, false, "Yes", "No")),
				Rows("transport", "Transportation", null, 50,
					F("ambulance_service", "Ambulance service", RmsFieldType.ShortText, true), F("transport_location", "Location", RmsFieldType.ShortText),
					F("transport_contact", "Contact", RmsFieldType.ShortText), Select("level_of_service", "Level of service", false, false, "ALS", "BLS")),
				Rows("hospitals", "Hospitals", null, 50,
					F("hospital_name", "Hospital", RmsFieldType.ShortText, true, true), F("hospital_address", "Address", RmsFieldType.Address), F("hospital_contact", "Contact", RmsFieldType.ShortText),
					Quantity("air_travel_time", "Air travel time", "time", "min"), Quantity("ground_travel_time", "Ground travel time", "time", "min"),
					Select("trauma_center", "Trauma centre", false, true, "Yes", "No"), F("trauma_level", "Level", RmsFieldType.ShortText),
					Select("burn_center", "Burn centre", false, false, "Yes", "No"), Select("helipad", "Helipad", false, false, "Yes", "No")),
				Section("procedure", "Emergency procedure",
					F("emergency_procedure", "Emergency medical procedure", RmsFieldType.LongText, true), F("evacuation_signal", "Evacuation signal", RmsFieldType.ShortText),
					F("medical_unit_leader", "Medical unit leader", RmsFieldType.ShortText)),
				Approval("Approved by Safety Officer"));

			yield return Policy(medical, "treatment-casualty", RmsFieldClassification.Restricted,
				"The emergency medical procedure is read alongside casualty handling and is not a page the whole department browses.", "emergency_procedure");

			yield return Ics("ics-208-safety-message", "ICS 208 Safety Message and Plan", RecordDefinitionCategories.IncidentSupport,
				"The safety message, hazards, mitigations and site safety plan reference for one operational period.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "ICS208", SubjectsPeriod,
				Header(),
				Section("message", "Safety message",
					F("safety_message", "Safety message", RmsFieldType.LongText, true),
					F("site_safety_plan_required", "Site safety plan required", RmsFieldType.Boolean, false, false, true),
					F("site_safety_plan_location", "Site safety plan location", RmsFieldType.ShortText)),
				Rows("hazards", "Hazards and mitigations", null, 100,
					F("hazard", "Hazard", RmsFieldType.ShortText, true, true), Select("severity", "Severity", true, true, "Low", "Medium", "High", "Extreme"),
					F("mitigation", "Mitigation", RmsFieldType.LongText, true), F("owner", "Owner", RmsFieldType.ShortText)),
				Section("weather", "Weather and environment",
					F("weather_summary", "Weather summary", RmsFieldType.LongText), Quantity("temperature", "Temperature", "temperature", "C"),
					F("wind", "Wind", RmsFieldType.ShortText), F("humidity", "Relative humidity", RmsFieldType.ShortText),
					F("watch_outs", "Watch-outs in effect", RmsFieldType.LongText)),
				Approval("Approved by Safety Officer"));

			yield return Ics("ics-215a-hazard-analysis", "ICS 215A Incident Safety Analysis", RecordDefinitionCategories.IncidentSupport,
				"Hazard and risk analysis by division or group, with the mitigations the operations plan depends on.",
				RmsLifecyclePreset.ReviewRequired, "I215A", SubjectsPeriod,
				Header(),
				Rows("analysis", "Analysis by division or group", 1, 200,
					F("division_group", "Division or group", RmsFieldType.ShortText, true, true),
					Multi("hazards", "Hazards", "Aviation", "Burnover or entrapment", "Communications failure", "Confined space", "Contamination", "Driving", "Dropped objects", "Electrical", "Environmental", "Fatigue", "Falling material", "Heat or cold", "Hazardous materials", "Heavy equipment", "Slips trips and falls", "Structural collapse", "Traffic", "Violence", "Water", "Wildlife"),
					F("mitigations", "Mitigations", RmsFieldType.LongText, true), Select("residual_risk", "Residual risk", true, true, "Low", "Medium", "High", "Extreme")),
				Section("summary", "Summary",
					F("safety_officer", "Safety officer", RmsFieldType.Person, false, true), F("overall_notes", "Overall notes", RmsFieldType.LongText)));
		}

		// ---- Resources ------------------------------------------------------------------------------------------

		private static IEnumerable<RecordTemplateDefinition> ResourceForms()
		{
			yield return Ics("ics-210-status-change", "ICS 210 Resource Status Change", RecordDefinitionCategories.IncidentSupport,
				"One resource changed state: from what, to what, when, and who was told.",
				RmsLifecyclePreset.QuickEntry, "ICS210", SubjectsResource,
				Header(false),
				Section("change", "Status change",
					F("resource_identifier", "Resource identifier", RmsFieldType.ShortText, true, true, true), F("unit", "Unit", RmsFieldType.Unit, false, true),
					Select("from_status", "From status", true, true, "Assigned", "Available", "Out of service", "En route", "Staged", "Released"),
					Select("to_status", "To status", true, true, "Assigned", "Available", "Out of service", "En route", "Staged", "Released"),
					F("changed_on", "Changed on", RmsFieldType.DateTime, true), F("location", "Location", RmsFieldType.ShortText),
					F("reason", "Reason", RmsFieldType.LongText), F("reported_by", "Reported by", RmsFieldType.Person, false, true),
					F("notified", "Notified", RmsFieldType.ShortText)));

			var checkIn = Ics("ics-211-check-in", "ICS 211 Incident Check-In List", RecordDefinitionCategories.IncidentSupport,
				"Who and what checked in: agency, position, order and request reference, arrival, and the support state each resource is in.",
				RmsLifecyclePreset.ReviewRequired, "ICS211", SubjectsParticipant,
				Header(false),
				Section("station", "Check-in location",
					Select("check_in_location", "Check-in location", true, true, "Incident command post", "Base", "Camp", "Staging area", "Helibase", "Restat", "Other"),
					F("location_detail", "Location detail", RmsFieldType.ShortText), F("recorder", "Recorder", RmsFieldType.Person, false, true)),
				Rows("check_ins", "Check-ins", 1, 500,
					F("resource_identifier", "Resource identifier", RmsFieldType.ShortText, true, true), F("member", "Member", RmsFieldType.Person, false, true),
					F("name_text", "Name (not a member)", RmsFieldType.ShortText), F("agency", "Home agency or unit", RmsFieldType.ShortText, false, true),
					F("position", "ICS position", RmsFieldType.ShortText, false, true), F("unit", "Unit", RmsFieldType.Unit),
					Ext("order_number", "Resource order number", RmsExternalReferenceSchemes.Iroc), Ext("request_number", "Request number", "request"),
					F("arrived_on", "Arrival", RmsFieldType.DateTime, true), F("departure_point", "Departure point", RmsFieldType.ShortText),
					F("method_of_travel", "Method of travel", RmsFieldType.ShortText), F("crew_leader_phone", "Contact", RmsFieldType.ShortText),
					Count("persons", "Persons"), Select("support_state", "Support state", false, true, "Checked in", "Assigned", "Released", "Demobilized"),
					F("home_unit_return", "Actual return to home unit", RmsFieldType.DateTime)),
				Section("notes", "Notes", F("notes", "Notes", RmsFieldType.LongText)));

			yield return Policy(checkIn, "manifest-travel", RmsFieldClassification.Restricted,
				"Personal travel and contact detail on a check-in manifest is not department-wide reading.", "crew_leader_phone", "departure_point", "method_of_travel");

			yield return Ics("ics-213-general-message", "ICS 213 General Message", RecordDefinitionCategories.IncidentSupport,
				"A message sent through the incident: to, from, subject, message and the reply it drew.",
				RmsLifecyclePreset.QuickEntry, "ICS213", SubjectsCommand,
				Header(false),
				Section("message", "Message",
					F("to", "To", RmsFieldType.ShortText, true, true), F("to_position", "Position", RmsFieldType.ShortText),
					F("from", "From", RmsFieldType.ShortText, true, true), F("from_position", "Position", RmsFieldType.ShortText),
					F("subject", "Subject", RmsFieldType.ShortText, true, true, true), F("sent_on", "Date and time", RmsFieldType.DateTime, true),
					F("message", "Message", RmsFieldType.LongText, true)),
				Section("reply", "Reply",
					F("reply", "Reply", RmsFieldType.LongText), F("replied_by", "Replied by", RmsFieldType.Person, false, true),
					F("replied_position", "Position", RmsFieldType.ShortText), F("replied_on", "Replied on", RmsFieldType.DateTime)));

			yield return Ics("ics-213rr-resource-request", "ICS 213RR Resource Request", RecordDefinitionCategories.IncidentSupport,
				"The resource request form. The request header and its fills are the ordering system's rows; this record is the request document, and it references them rather than restating them.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "I213RR", SubjectsResource,
				Header(false),
				Section("request", "Request",
					Select("priority", "Priority", true, true, "Now", "Next operational period", "Routine"),
					F("requested_on", "Date and time", RmsFieldType.DateTime, true), F("requested_by", "Requested by", RmsFieldType.Person, true, true),
					F("requested_position", "Position", RmsFieldType.ShortText), F("section", "Section", RmsFieldType.ShortText, false, true),
					Ext("request_number", "Request number", "request"), Ext("order_number", "Order number", RmsExternalReferenceSchemes.Iroc),
					Ext("webeoc_reference", "WebEOC mission", RmsExternalReferenceSchemes.WebEoc), Ext("emac_reference", "EMAC mission", RmsExternalReferenceSchemes.Emac)),
				Rows("items", "Items requested", 1, 100,
					Count("quantity", "Quantity"), F("kind_and_type", "Kind and type", RmsFieldType.ShortText, true, true),
					F("detailed_description", "Detailed item description", RmsFieldType.LongText, true), F("needed_on", "Needed by", RmsFieldType.DateTime),
					F("deliver_to", "Deliver to", RmsFieldType.ShortText), F("suitable_substitutes", "Suitable substitutes", RmsFieldType.LongText)),
				Section("justification", "Justification and supply",
					F("justification", "Justification", RmsFieldType.LongText, true), F("requested_source", "Requested source of supply", RmsFieldType.ShortText),
					Money("estimated_cost", "Estimated cost"), F("logistics_notes", "Logistics notes", RmsFieldType.LongText),
					Select("fill_status", "Fill status", false, true, "Submitted", "Filled", "Partially filled", "Unable to fill", "Cancelled")),
				Approval("Approved by Section Chief"));

			yield return Ics("ics-218-support-vehicle-inventory", "ICS 218 Support Vehicle and Equipment Inventory", RecordDefinitionCategories.IncidentSupport,
				"Support vehicles and equipment on the incident: kind, type, identifier, operator and where it is.",
				RmsLifecyclePreset.ReviewRequired, "ICS218", SubjectsResource,
				Header(false),
				Rows("vehicles", "Support vehicles and equipment", 1, 500,
					Select("category", "Category", true, true, "Vehicle", "Heavy equipment", "Support trailer", "Generator", "Pump", "Other"),
					F("kind_and_type", "Kind and type", RmsFieldType.ShortText, true, true), F("identifier", "Incident identifier", RmsFieldType.ShortText, true, true),
					F("unit", "Unit", RmsFieldType.Unit), F("make_model", "Make and model", RmsFieldType.ShortText),
					F("agency", "Owning agency or vendor", RmsFieldType.ShortText, false, true), F("operator", "Operator", RmsFieldType.ShortText),
					F("contact", "Contact", RmsFieldType.ShortText), F("location", "Location", RmsFieldType.ShortText),
					Ext("rental_agreement", "Rental agreement", "agreement"), F("released_on", "Released on", RmsFieldType.DateTime)));

			yield return Ics("ics-219-tcard", "ICS 219 Resource Status Card (T-Card)", RecordDefinitionCategories.IncidentSupport,
				"One resource tracked through the incident: what it is, who is on it, and every status it moved through.",
				RmsLifecyclePreset.QuickEntry, "ICS219", SubjectsResource,
				Header(false),
				Section("resource", "Resource",
					Select("card_type", "Card type", true, true, "Header", "Crew or team", "Engine", "Helicopter", "Personnel", "Equipment", "Aircraft", "Dozer", "Miscellaneous"),
					F("resource_identifier", "Resource identifier", RmsFieldType.ShortText, true, true, true), F("kind_and_type", "Kind and type", RmsFieldType.ShortText, false, true),
					F("unit", "Unit", RmsFieldType.Unit, false, true), F("agency", "Home agency or unit", RmsFieldType.ShortText, false, true),
					F("leader", "Leader", RmsFieldType.ShortText), Count("persons", "Persons"),
					Ext("order_number", "Order number", RmsExternalReferenceSchemes.Iroc), Ext("request_number", "Request number", "request"),
					F("checked_in_on", "Checked in", RmsFieldType.DateTime), F("released_on", "Released", RmsFieldType.DateTime)),
				Rows("status_history", "Status history", null, 500,
					F("changed_on", "Date and time", RmsFieldType.DateTime, true),
					Select("status", "Status", true, true, "Checked in", "Available", "Assigned", "Out of service", "En route", "Staged", "Released", "Demobilized"),
					F("assignment", "Assignment", RmsFieldType.ShortText), F("location", "Location", RmsFieldType.ShortText), F("notes", "Notes", RmsFieldType.LongText)));

			var demob = Ics("ics-221-demobilization", "ICS 221 Demobilization Check-Out", RecordDefinitionCategories.IncidentSupport,
				"A resource leaving the incident: every unit that must sign it out, travel arrangements and the actual return to home unit.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "ICS221", SubjectsResource,
				Header(false),
				Section("resource", "Resource",
					F("resource_identifier", "Resource identifier", RmsFieldType.ShortText, true, true, true), F("unit", "Unit", RmsFieldType.Unit, false, true),
					F("agency", "Home agency or unit", RmsFieldType.ShortText, false, true), Count("persons", "Persons"),
					Ext("order_number", "Order number", RmsExternalReferenceSchemes.Iroc), Ext("request_number", "Request number", "request"),
					F("demob_planned_on", "Planned release", RmsFieldType.DateTime), F("released_on", "Actual release", RmsFieldType.DateTime, true)),
				Rows("checkout_units", "Check-out", 1, 30,
					Select("unit_name", "Unit", true, true, "Logistics — supply", "Logistics — communications", "Logistics — facilities", "Logistics — ground support", "Planning — documentation", "Planning — demobilization", "Finance — time", "Finance — equipment time", "Safety", "Security", "Other"),
					Select("outcome", "Outcome", true, true, "Cleared", "Not required", "Outstanding"), F("cleared_by", "Cleared by", RmsFieldType.ShortText),
					F("cleared_on", "Cleared on", RmsFieldType.DateTime), F("remarks", "Remarks", RmsFieldType.LongText)),
				Section("travel", "Travel and return",
					F("destination", "Destination", RmsFieldType.ShortText), F("method_of_travel", "Method of travel", RmsFieldType.ShortText),
					F("estimated_time_of_departure", "Estimated departure", RmsFieldType.DateTime), F("estimated_time_of_arrival", "Estimated arrival", RmsFieldType.DateTime),
					Quantity("travel_distance", "Travel distance", "length", "km"), F("overnight_stops", "Overnight stops", RmsFieldType.LongText),
					F("actual_home_unit_return", "Actual return to home unit", RmsFieldType.DateTime),
					F("rest_overrides", "Rest or work-driving exception", RmsFieldType.LongText)),
				Approval("Approved by Demobilization Unit Leader"));

			yield return Policy(demob, "manifest-travel", RmsFieldClassification.Restricted,
				"Travel routing and overnight stops for a named crew are personal movement detail.", "method_of_travel", "overnight_stops", "destination");
		}

		// ---- Activity and planning ------------------------------------------------------------------------------

		private static IEnumerable<RecordTemplateDefinition> ActivityAndPlanning()
		{
			yield return Ics("ics-214-activity-log", "ICS 214 Activity Log", RecordDefinitionCategories.IncidentSupport,
				"The unit or individual activity log for one operational period: who was assigned, and what happened when.",
				RmsLifecyclePreset.ReviewRequired, "ICS214", SubjectsParticipant,
				Header(),
				Section("unit", "Unit or individual",
					F("unit_name", "Unit name", RmsFieldType.ShortText, true, true, true), F("unit", "Unit", RmsFieldType.Unit, false, true),
					F("leader", "Unit leader", RmsFieldType.Person, false, true), F("leader_position", "Position", RmsFieldType.ShortText)),
				Rows("assigned", "Resources assigned", null, 200,
					F("name", "Name", RmsFieldType.ShortText, true), F("member", "Member", RmsFieldType.Person), F("position", "ICS position", RmsFieldType.ShortText),
					F("home_agency", "Home agency or unit", RmsFieldType.ShortText)),
				Rows("activities", "Activity log", 1, 500,
					F("occurred_on", "Date and time", RmsFieldType.DateTime, true), F("activity", "Notable activity", RmsFieldType.LongText, true),
					Select("kind", "Kind", false, true, "Assignment", "Briefing", "Communication", "Safety", "Significant event", "Resource change", "Administrative", "Other")),
				Section("close", "Close", F("summary", "Summary", RmsFieldType.LongText), F("signature", "Preparer signature", RmsFieldType.Signature)));

			yield return Ics("ics-215-planning-worksheet", "ICS 215 Operational Planning Worksheet", RecordDefinitionCategories.IncidentSupport,
				"The worksheet the tactics meeting produces: work assignment by division or group, resources required against resources available, and the reporting location for each.",
				RmsLifecyclePreset.ReviewRequired, "ICS215", SubjectsPeriod,
				Header(),
				Rows("assignments", "Planned assignments", 1, 200,
					F("division_group", "Division or group", RmsFieldType.ShortText, true, true), F("work_assignment", "Work assignment", RmsFieldType.LongText, true),
					F("resource_kind", "Resource kind and type", RmsFieldType.ShortText, true, true),
					Count("required", "Required"), Count("have", "Have"), Count("need", "Need"),
					F("reporting_location", "Reporting location", RmsFieldType.ShortText), F("reporting_time", "Reporting time", RmsFieldType.DateTime),
					F("special_equipment", "Special equipment and supplies", RmsFieldType.LongText)),
				Section("summary", "Summary",
					F("total_resources_note", "Resource summary", RmsFieldType.LongText), F("operations_chief", "Operations section chief", RmsFieldType.Person, false, true),
					F("planning_chief", "Planning section chief", RmsFieldType.Person, false, true)));

			yield return Ics("ics-220-air-operations", "ICS 220 Air Operations Summary", RecordDefinitionCategories.IncidentSupport,
				"Aircraft assigned, air traffic control, frequencies and the air operations plan for one operational period.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "ICS220", SubjectsPeriod,
				Header(),
				Section("control", "Air traffic control",
					F("air_operations_director", "Air operations branch director", RmsFieldType.ShortText), F("air_tactical_supervisor", "Air tactical group supervisor", RmsFieldType.ShortText),
					F("air_support_supervisor", "Air support group supervisor", RmsFieldType.ShortText), F("helibase", "Helibase", RmsFieldType.ShortText),
					F("helispots", "Helispots", RmsFieldType.LongText), F("air_to_air_frequency", "Air to air frequency", RmsFieldType.ShortText),
					F("air_to_ground_frequency", "Air to ground frequency", RmsFieldType.ShortText), F("command_frequency", "Command frequency", RmsFieldType.ShortText),
					F("deck_coordinator_frequency", "Deck coordinator frequency", RmsFieldType.ShortText), F("temporary_flight_restriction", "Temporary flight restriction", RmsFieldType.LongText)),
				Rows("aircraft", "Aircraft assigned", null, 100,
					F("aircraft_identifier", "Aircraft identifier", RmsFieldType.ShortText, true, true),
					Select("aircraft_category", "Category", true, true, "Rotor wing", "Fixed wing", "Air tanker", "Lead plane", "Air attack", "Unmanned"),
					F("make_model", "Make and model", RmsFieldType.ShortText), F("base", "Base", RmsFieldType.ShortText),
					F("available_from", "Available from", RmsFieldType.DateTime), F("available_to", "Available to", RmsFieldType.DateTime),
					F("assignment", "Assignment", RmsFieldType.LongText), F("vendor", "Vendor or agency", RmsFieldType.ShortText)),
				Section("plan", "Air operations plan",
					F("remarks", "Remarks", RmsFieldType.LongText), F("hazards", "Known aviation hazards", RmsFieldType.LongText)),
				Approval("Approved by Air Operations Branch Director"));

			var rating = Ics("ics-225-performance-rating", "ICS 225 Incident Personnel Performance Rating", RecordDefinitionCategories.IncidentSupport,
				"An individual performance rating for the incident assignment: rating factors, narrative, and the discussion with the individual.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "ICS225", SubjectsParticipant,
				Header(false),
				Section("individual", "Individual rated",
					F("member", "Member", RmsFieldType.Person, false, true), F("name_text", "Name (not a member)", RmsFieldType.ShortText),
					F("home_agency", "Home agency or unit", RmsFieldType.ShortText, false, true), F("position", "Incident position", RmsFieldType.ShortText, true, true),
					F("assignment_from", "Assignment from", RmsFieldType.Date), F("assignment_to", "Assignment to", RmsFieldType.Date),
					Select("incident_complexity", "Incident complexity", false, true, "Type 5", "Type 4", "Type 3", "Type 2", "Type 1")),
				Rows("factors", "Rating factors", 1, 30,
					F("factor", "Factor", RmsFieldType.ShortText, true), Select("rating", "Rating", true, false, "Unacceptable", "Needs improvement", "Met standard", "Exceeded standard", "Not observed"),
					F("comment", "Comment", RmsFieldType.LongText)),
				Section("narrative", "Narrative and discussion",
					F("narrative", "Narrative", RmsFieldType.LongText, true), F("recommendation", "Recommendation", RmsFieldType.LongText),
					F("discussed_with_individual", "Discussed with the individual", RmsFieldType.Boolean), F("individual_comment", "Individual comment", RmsFieldType.LongText),
					F("individual_signature", "Individual signature", RmsFieldType.Signature)),
				Approval("Rated by"));

			yield return Policy(rating, "regulatory", RmsFieldClassification.Restricted,
				"A performance rating is a personnel record about a named individual, not incident documentation the department browses.",
				"narrative", "recommendation", "individual_comment", "factor", "rating", "comment");

			yield return Ics("ics-260-resource-order", "ICS 260 Resource Order", RecordDefinitionCategories.IncidentSupport,
				"The resource order as placed and filled. The order and its fills live in the ordering system; this record carries the document, the identifiers and the artifact it was built from.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "ICS260", SubjectsResource,
				Header(false),
				Section("order", "Order",
					Ext("order_number", "Order number", RmsExternalReferenceSchemes.Iroc, true), F("ordering_point", "Ordering point", RmsFieldType.ShortText, false, true),
					F("ordered_on", "Ordered on", RmsFieldType.DateTime, true), F("ordered_by", "Ordered by", RmsFieldType.Person, false, true),
					Select("order_kind", "Order kind", true, true, "Overhead", "Crew", "Equipment", "Aircraft", "Supply"),
					F("deliver_to", "Deliver to", RmsFieldType.ShortText), F("needed_on", "Needed by", RmsFieldType.DateTime)),
				Rows("lines", "Order lines", 1, 200,
					Ext("request_number", "Request number", "request"), Count("quantity", "Quantity"),
					F("kind_and_type", "Kind and type", RmsFieldType.ShortText, true, true), F("description", "Description", RmsFieldType.LongText),
					Select("line_status", "Status", false, true, "Requested", "Filled", "Partially filled", "Unable to fill", "Cancelled"),
					F("filled_with", "Filled with", RmsFieldType.ShortText), F("filled_on", "Filled on", RmsFieldType.DateTime),
					F("provider", "Providing unit or agency", RmsFieldType.ShortText)),
				Section("source", "Source artifact",
					F("source_artifact", "Source artifact", RmsFieldType.Attachment), F("source_checksum_note", "Provenance note", RmsFieldType.LongText),
					Ext("eisuite_reference", "e-ISuite reference", RmsExternalReferenceSchemes.EIsuite)),
				Approval("Approved by Ordering Point"));
		}

		// ---- Incident business ----------------------------------------------------------------------------------

		private static IEnumerable<RecordTemplateDefinition> IncidentBusinessForms()
		{
			// Every form in this group is a document over a ledger. The rows below carry what the crew wrote down
			// and the reference to the authoritative row; when the Back Office ledger ships, the reference is what a
			// rendered total is computed from, and nothing here is re-keyed into it.
			var crewTime = Ics("sf-261-crew-time", "SF-261 Crew Time Report", RecordDefinitionCategories.IncidentBusiness,
				"Daily crew time for one crew: hours by person and day, with the certifying signatures. The approved time span itself is the ledger's row; this is the certified document over it.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "SF261", SubjectsBusiness,
				Header(false),
				Section("crew", "Crew",
					F("crew_name", "Crew or team name", RmsFieldType.ShortText, true, true, true), F("unit", "Unit", RmsFieldType.Unit, false, true),
					F("home_agency", "Home agency or unit", RmsFieldType.ShortText, false, true), F("crew_leader", "Crew leader", RmsFieldType.Person, false, true),
					F("work_date", "Work date", RmsFieldType.Date, true), Ext("order_number", "Order number", RmsExternalReferenceSchemes.Iroc),
					Ext("request_number", "Request number", "request")),
				Rows("entries", "Time entries", 1, 200,
					F("member", "Member", RmsFieldType.Person), F("name_text", "Name (not a member)", RmsFieldType.ShortText),
					F("position", "ICS position", RmsFieldType.ShortText), F("started_on", "Start", RmsFieldType.DateTime, true),
					F("ended_on", "Stop", RmsFieldType.DateTime, true), Quantity("hours_worked", "Hours worked", "time", "h"),
					Select("hours_kind", "Kind", false, true, "Regular", "Overtime", "Travel", "Standby", "Hazard"),
					F("remarks", "Remarks", RmsFieldType.LongText), Ext("ledger_reference", "Approved time reference", "dtr")),
				Section("certification", "Certification",
					F("crew_certification", "Crew representative certification", RmsFieldType.Signature, true), F("certified_by", "Certified by", RmsFieldType.Person, false, true),
					F("time_officer", "Incident time officer", RmsFieldType.ShortText), F("posted_on", "Posted on", RmsFieldType.DateTime),
					Ext("finance_reference", "Finance posting", RmsExternalReferenceSchemes.FinancePosting)),
				Approval("Approved by Finance Section"));

			yield return Policy(crewTime, "regulatory", RmsFieldClassification.Restricted,
				"Individual hours and pay-relevant classifications are personnel compensation data.", "hours_worked", "hours_kind", "remarks");

			var equipmentInvoice = Ics("of-286-equipment-use-invoice", "OF-286 Emergency Equipment Use Invoice", RecordDefinitionCategories.IncidentBusiness,
				"The use invoice for hired equipment: agreement, rate basis, use by day, deductions and the total claimed. The rate and the arithmetic belong to the ledger and the agreement; this is the invoice document.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "OF286", SubjectsBusiness,
				Header(false),
				Section("agreement", "Agreement and equipment",
					F("contractor", "Contractor or vendor", RmsFieldType.Contact, false, true, true), F("contractor_text", "Contractor (not a contact)", RmsFieldType.ShortText),
					Ext("rental_agreement", "Rental agreement number", "agreement", true), F("equipment_identifier", "Equipment identifier", RmsFieldType.ShortText, true, true),
					F("equipment_description", "Equipment description", RmsFieldType.LongText), F("operator", "Operator furnished by", RmsFieldType.ShortText),
					Select("rate_basis", "Rate basis", true, true, "Daily", "Hourly", "Mileage", "Work rate", "Guarantee"),
					F("period_from", "Use period from", RmsFieldType.Date, true), F("period_to", "Use period to", RmsFieldType.Date, true)),
				Rows("use", "Use by day", 1, 200,
					F("use_date", "Date", RmsFieldType.Date, true), Quantity("hours_used", "Hours used", "time", "h"),
					Quantity("distance_used", "Distance", "length", "km"), F("work_location", "Work location", RmsFieldType.ShortText),
					Select("special_rate", "Special rate", false, false, "None", "Standby", "Guarantee", "Double shift"),
					F("remarks", "Remarks", RmsFieldType.LongText), Ext("ledger_reference", "Equipment use reference", "equipment-time")),
				Rows("deductions", "Deductions", null, 50,
					Select("deduction_kind", "Kind", true, true, "Fuel", "Oil and lubricant", "Repair", "Operator supplied", "Damage", "Other"),
					F("description", "Description", RmsFieldType.ShortText), Money("amount", "Amount"), F("authority", "Authority", RmsFieldType.ShortText)),
				Section("totals", "Claimed total",
					Money("claimed_total", "Total claimed"), Ext("vendor_invoice", "Vendor invoice number", RmsExternalReferenceSchemes.VendorInvoice),
					Ext("finance_reference", "Finance posting", RmsExternalReferenceSchemes.FinancePosting), F("invoice_document", "Invoice document", RmsFieldType.Attachment),
					F("totals_note", "The rate schedule and the arithmetic are the agreement's and the ledger's; this figure is what was claimed.", RmsFieldType.LongText)),
				Approval("Approved by Finance Section"));

			yield return Policy(equipmentInvoice, "regulatory", RmsFieldClassification.Restricted,
				"Vendor rates, claimed amounts and deductions are commercial terms under an agreement.", "claimed_total", "amount", "vendor_invoice", "finance_reference");

			var firefighterTime = Ics("of-288-firefighter-time", "OF-288 Emergency Firefighter Time Report", RecordDefinitionCategories.IncidentBusiness,
				"Individual emergency firefighter time for the assignment: hours by day, travel and the certifying signatures.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "OF288", SubjectsBusiness,
				Header(false),
				Section("individual", "Individual",
					F("member", "Member", RmsFieldType.Person, false, true), F("name_text", "Name (not a member)", RmsFieldType.ShortText),
					F("home_agency", "Home agency or unit", RmsFieldType.ShortText, false, true), F("position", "Incident position", RmsFieldType.ShortText, false, true),
					F("employment_kind", "Employment kind", RmsFieldType.ShortText), Ext("order_number", "Order number", RmsExternalReferenceSchemes.Iroc),
					Ext("request_number", "Request number", "request"), F("assignment_from", "Assignment from", RmsFieldType.Date, true),
					F("assignment_to", "Assignment to", RmsFieldType.Date)),
				Rows("days", "Time by day", 1, 200,
					F("work_date", "Date", RmsFieldType.Date, true), F("started_on", "Start", RmsFieldType.DateTime), F("ended_on", "Stop", RmsFieldType.DateTime),
					Quantity("hours_worked", "Hours worked", "time", "h"), Quantity("travel_hours", "Travel hours", "time", "h"),
					Select("hours_kind", "Kind", false, true, "Regular", "Overtime", "Travel", "Standby", "Hazard"),
					F("remarks", "Remarks", RmsFieldType.LongText), Ext("ledger_reference", "Approved time reference", "dtr")),
				Section("certification", "Certification",
					F("employee_signature", "Employee signature", RmsFieldType.Signature), F("supervisor", "Supervisor", RmsFieldType.Person, false, true),
					F("supervisor_signature", "Supervisor signature", RmsFieldType.Signature, true), F("time_officer", "Incident time officer", RmsFieldType.ShortText),
					Ext("finance_reference", "Finance posting", RmsExternalReferenceSchemes.FinancePosting)),
				Approval("Approved by Finance Section"));

			yield return Policy(firefighterTime, "regulatory", RmsFieldClassification.Restricted,
				"Individual hours, employment kind and pay classification are personnel compensation data.",
				"hours_worked", "travel_hours", "hours_kind", "employment_kind", "remarks");

			var shiftTicket = Ics("of-294-equipment-shift-ticket", "OF-294 Emergency Equipment Shift Ticket", RecordDefinitionCategories.IncidentBusiness,
				"One shift of hired equipment use, signed at the end of the shift. The shift ticket is the source document the use invoice is built from.",
				RmsLifecyclePreset.ReviewRequired, "OF294", SubjectsBusiness,
				Header(false),
				Section("shift", "Shift",
					Ext("rental_agreement", "Rental agreement number", "agreement", true), F("equipment_identifier", "Equipment identifier", RmsFieldType.ShortText, true, true),
					F("operator", "Operator", RmsFieldType.ShortText), F("shift_date", "Shift date", RmsFieldType.Date, true),
					F("started_on", "Start", RmsFieldType.DateTime, true), F("ended_on", "Stop", RmsFieldType.DateTime, true),
					Quantity("hours_used", "Hours used", "time", "h"), Quantity("distance_used", "Distance", "length", "km"),
					F("work_performed", "Work performed", RmsFieldType.LongText), F("work_location", "Work location", RmsFieldType.ShortText)),
				Rows("consumables", "Fuel and consumables furnished by the government", null, 50,
					Select("kind", "Kind", true, true, "Fuel", "Oil", "Lubricant", "Parts", "Other"), Quantity("quantity", "Quantity", "volume", "L"),
					F("furnished_by", "Furnished by", RmsFieldType.ShortText), F("remarks", "Remarks", RmsFieldType.LongText)),
				Section("certification", "Certification",
					F("operator_signature", "Operator signature", RmsFieldType.Signature), F("government_representative", "Government representative", RmsFieldType.ShortText),
					F("representative_signature", "Representative signature", RmsFieldType.Signature, true),
					Ext("ledger_reference", "Equipment use reference", "equipment-time")));

			yield return Policy(shiftTicket, "regulatory", RmsFieldClassification.Restricted,
				"Shift hours and furnished consumables feed a commercial invoice under an agreement.", "hours_used", "distance_used", "quantity");

			yield return Ics("of-296-equipment-inspection", "OF-296 Vehicle and Heavy Equipment Safety Inspection", RecordDefinitionCategories.IncidentBusiness,
				"The pre-use safety inspection a hired vehicle or piece of heavy equipment must pass before it works on the incident.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "OF296", SubjectsBusiness,
				Header(false),
				Section("equipment", "Equipment",
					F("contractor", "Contractor or vendor", RmsFieldType.Contact, false, true), F("contractor_text", "Contractor (not a contact)", RmsFieldType.ShortText),
					Ext("rental_agreement", "Rental agreement number", "agreement"), F("equipment_identifier", "Equipment identifier", RmsFieldType.ShortText, true, true),
					F("make_model", "Make and model", RmsFieldType.ShortText), F("serial_or_vin", "Serial or VIN", RmsFieldType.ShortText),
					F("odometer", "Odometer or hour meter", RmsFieldType.ShortText), F("inspected_on", "Inspected on", RmsFieldType.DateTime, true)),
				Rows("items", "Inspection items", 1, 200,
					Select("system", "System", true, true, "Brakes", "Steering", "Lights", "Tyres and wheels", "Glass and mirrors", "Seat belts", "Fire extinguisher", "Exhaust", "Fuel system", "Hydraulics", "Guards and shields", "Backup alarm", "Rollover protection", "Other"),
					F("item", "Item", RmsFieldType.ShortText, true), Select("result", "Result", true, true, "Pass", "Fail", "Not applicable"),
					F("defect", "Defect", RmsFieldType.LongText), F("corrected_on", "Corrected on", RmsFieldType.DateTime)),
				Section("outcome", "Outcome",
					Select("disposition", "Disposition", true, true, "Accepted", "Accepted with corrections", "Rejected"), F("comments", "Comments", RmsFieldType.LongText),
					F("photo", "Photo", RmsFieldType.Attachment), F("inspector", "Inspector", RmsFieldType.Person, false, true),
					F("inspector_qualification", "Inspector qualification", RmsFieldType.ShortText)),
				Approval("Approved by Ground Support Unit Leader"));

			var rentalEnvelope = Ics("of-297-rental-use-envelope", "OF-297 Emergency Equipment Rental-Use Envelope", RecordDefinitionCategories.IncidentBusiness,
				"The envelope that keeps one piece of hired equipment together: agreement, shift tickets, inspections, invoice and the release.",
				RmsLifecyclePreset.ReviewRequired, "OF297", SubjectsBusiness,
				Header(false),
				Section("equipment", "Equipment and agreement",
					F("contractor", "Contractor or vendor", RmsFieldType.Contact, false, true), F("contractor_text", "Contractor (not a contact)", RmsFieldType.ShortText),
					Ext("rental_agreement", "Rental agreement number", "agreement", true), F("equipment_identifier", "Equipment identifier", RmsFieldType.ShortText, true, true),
					F("hired_on", "Hired on", RmsFieldType.Date), F("released_on", "Released on", RmsFieldType.Date)),
				Rows("contents", "Envelope contents", 1, 200,
					Select("document_kind", "Document", true, true, "Rental agreement", "Shift ticket", "Safety inspection", "Use invoice", "Deduction authority", "Damage report", "Release", "Other"),
					F("reference", "Reference", RmsFieldType.ShortText), F("document_date", "Date", RmsFieldType.Date),
					Select("present", "Present", true, true, "Yes", "No", "Not applicable"), F("document", "Document", RmsFieldType.Attachment)),
				Section("close", "Close-out",
					Money("total_claimed", "Total claimed"), Ext("finance_reference", "Finance posting", RmsExternalReferenceSchemes.FinancePosting),
					F("outstanding_items", "Outstanding items", RmsFieldType.LongText), F("closed_on", "Closed on", RmsFieldType.DateTime),
					F("closed_by", "Closed by", RmsFieldType.Person, false, true)));

			yield return Policy(rentalEnvelope, "regulatory", RmsFieldClassification.Restricted,
				"The claimed total and its finance posting are commercial terms under an agreement.", "total_claimed", "finance_reference");

			var rentalAgreement = Ics("of-315-rental-agreement", "OF-315 Emergency Equipment Rental Agreement", RecordDefinitionCategories.IncidentBusiness,
				"The rental agreement itself: parties, equipment, rate basis, terms and the signatures that bind it.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "OF315", SubjectsBusiness,
				Header(false),
				Section("parties", "Parties",
					F("contractor", "Contractor or vendor", RmsFieldType.Contact, false, true, true), F("contractor_text", "Contractor (not a contact)", RmsFieldType.ShortText),
					F("contractor_address", "Contractor address", RmsFieldType.Address), F("contractor_identifier", "Contractor identifier", RmsFieldType.ShortText),
					F("government_agency", "Government agency", RmsFieldType.ShortText, true, true), F("agency_representative", "Agency representative", RmsFieldType.Person, false, true),
					Ext("agreement_number", "Agreement number", "agreement", true)),
				Rows("equipment", "Equipment covered", 1, 100,
					F("equipment_identifier", "Equipment identifier", RmsFieldType.ShortText, true, true), F("description", "Description", RmsFieldType.LongText),
					F("serial_or_vin", "Serial or VIN", RmsFieldType.ShortText), Select("rate_basis", "Rate basis", true, true, "Daily", "Hourly", "Mileage", "Work rate", "Guarantee"),
					Money("rate", "Rate"), Select("operator_furnished", "Operator furnished by", false, false, "Contractor", "Government", "Not applicable"),
					Select("fuel_furnished", "Fuel furnished by", false, false, "Contractor", "Government")),
				Section("terms", "Terms",
					F("effective_from", "Effective from", RmsFieldType.Date, true), F("effective_to", "Effective to", RmsFieldType.Date),
					F("point_of_hire", "Point of hire", RmsFieldType.ShortText), F("special_terms", "Special terms", RmsFieldType.LongText),
					F("insurance_note", "Insurance and liability", RmsFieldType.LongText), F("agreement_document", "Signed agreement", RmsFieldType.Attachment)),
				Section("signatures", "Signatures",
					F("contractor_signature", "Contractor signature", RmsFieldType.Signature, true), F("agency_signature", "Agency signature", RmsFieldType.Signature, true),
					F("signed_on", "Signed on", RmsFieldType.DateTime, true)),
				Approval("Approved by Procurement Unit Leader"));

			yield return Policy(rentalAgreement, "regulatory", RmsFieldClassification.Restricted,
				"Vendor identifiers, rates and negotiated terms are commercial and, in the identifier case, tax-adjacent.",
				"contractor_identifier", "rate", "special_terms", "insurance_note", "contractor_address");
		}

		// ---- Support operations ---------------------------------------------------------------------------------

		private static IEnumerable<RecordTemplateDefinition> SupportOperations()
		{
			var facility = Ics("facility-inspection", "Incident facility inspection", RecordDefinitionCategories.IncidentSupport,
				"The condition of an incident facility on arrival and on release: what was there, what was damaged, and what the incident is responsible for.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "FACINS", SubjectsFacility,
				Header(false),
				Section("facility", "Facility",
					F("facility_name", "Facility", RmsFieldType.ShortText, true, true, true),
					Select("facility_kind", "Kind", true, true, "Incident command post", "Base", "Camp", "Staging area", "Helibase", "Helispot", "Warehouse", "Office", "Other"),
					F("location", "Location", RmsFieldType.Address), F("owner", "Owner or land manager", RmsFieldType.ShortText, false, true),
					Ext("use_agreement", "Use agreement", "agreement"), Select("inspection_kind", "Inspection", true, true, "On arrival", "Interim", "On release"),
					F("inspected_on", "Inspected on", RmsFieldType.DateTime, true)),
				Rows("areas", "Areas inspected", 1, 200,
					F("area", "Area", RmsFieldType.ShortText, true), Select("condition", "Condition", true, true, "Good", "Fair", "Poor", "Damaged", "Not inspected"),
					F("findings", "Findings", RmsFieldType.LongText), F("photo", "Photo", RmsFieldType.Attachment), F("responsible_party", "Responsible party", RmsFieldType.ShortText)),
				Section("outcome", "Outcome",
					F("damage_summary", "Damage summary", RmsFieldType.LongText), Money("estimated_restoration_cost", "Estimated restoration cost"),
					F("owner_present", "Owner representative present", RmsFieldType.ShortText), F("owner_signature", "Owner signature", RmsFieldType.Signature),
					F("access_controls", "Access and security controls", RmsFieldType.LongText)),
				Approval("Approved by Facilities Unit Leader"));

			yield return Policy(facility, "facility-security", RmsFieldClassification.Restricted,
				"Access and security controls for a facility are not department-wide reading.", "access_controls");

			yield return Ics("facility-use-agreement", "Land or facility use agreement", RecordDefinitionCategories.IncidentBusiness,
				"The agreement under which the incident occupies land or a facility: parties, term, permitted use, restoration obligation and signatures.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "LUA", SubjectsFacility,
				Header(false),
				Section("parties", "Parties and property",
					F("owner", "Owner or land manager", RmsFieldType.Contact, false, true, true), F("owner_text", "Owner (not a contact)", RmsFieldType.ShortText),
					F("property_description", "Property description", RmsFieldType.LongText, true), F("location", "Location", RmsFieldType.Address),
					Quantity("area_used", "Area used", "area", "ha"), Ext("agreement_number", "Agreement number", "agreement", true)),
				Section("terms", "Terms",
					F("effective_from", "Effective from", RmsFieldType.Date, true), F("effective_to", "Effective to", RmsFieldType.Date),
					F("permitted_use", "Permitted use", RmsFieldType.LongText, true), Money("compensation", "Compensation"),
					Select("compensation_basis", "Compensation basis", false, true, "No cost", "Daily", "Flat", "Restoration only", "Other"),
					F("restoration_obligation", "Restoration obligation", RmsFieldType.LongText), F("restrictions", "Restrictions", RmsFieldType.LongText)),
				Section("signatures", "Signatures",
					F("owner_signature", "Owner signature", RmsFieldType.Signature, true), F("agency_signature", "Agency signature", RmsFieldType.Signature, true),
					F("signed_on", "Signed on", RmsFieldType.DateTime, true), F("agreement_document", "Signed agreement", RmsFieldType.Attachment)),
				Approval("Approved by Procurement Unit Leader"));

			yield return Ics("camp-sanitation-inspection", "Camp sanitation inspection", RecordDefinitionCategories.IncidentSupport,
				"Sanitation at an incident camp: water, waste, washing, sleeping and vector control, with the corrective actions raised.",
				RmsLifecyclePreset.ReviewRequired, "SANINS", SubjectsFacility,
				Header(false),
				Section("camp", "Camp",
					F("facility_name", "Camp or facility", RmsFieldType.ShortText, true, true, true), F("location", "Location", RmsFieldType.Address),
					Count("population", "Population served"), F("inspected_on", "Inspected on", RmsFieldType.DateTime, true),
					F("inspector", "Inspector", RmsFieldType.Person, false, true), F("inspector_qualification", "Qualification", RmsFieldType.ShortText)),
				Rows("items", "Inspection items", 1, 200,
					Select("area", "Area", true, true, "Potable water", "Hand washing", "Showers", "Toilets", "Grey water", "Solid waste", "Sleeping area", "Food service", "Vector control", "Lighting", "Other"),
					F("item", "Item", RmsFieldType.ShortText, true), Select("result", "Result", true, true, "Satisfactory", "Marginal", "Unsatisfactory", "Not applicable"),
					F("finding", "Finding", RmsFieldType.LongText), F("corrective_action", "Corrective action", RmsFieldType.LongText),
					F("corrected_on", "Corrected on", RmsFieldType.DateTime), F("photo", "Photo", RmsFieldType.Attachment)),
				Section("outcome", "Outcome",
					Select("overall", "Overall", true, true, "Satisfactory", "Marginal", "Unsatisfactory"), F("summary", "Summary", RmsFieldType.LongText),
					F("reinspection_due", "Reinspection due", RmsFieldType.Date)));

			var foodService = Ics("food-service-inspection", "Food service inspection", RecordDefinitionCategories.IncidentSupport,
				"Food service on the incident: the caterer, temperatures, handling, storage and the findings that must be corrected before the next meal.",
				RmsLifecyclePreset.ReviewRequired, "FDINSP", SubjectsFacility,
				Header(false),
				Section("service", "Food service",
					F("caterer", "Caterer or vendor", RmsFieldType.Contact, false, true), F("caterer_text", "Caterer (not a contact)", RmsFieldType.ShortText),
					Ext("agreement_number", "Agreement number", "agreement"), F("facility_name", "Facility", RmsFieldType.ShortText, true, true),
					Count("meals_served", "Meals served"), F("inspected_on", "Inspected on", RmsFieldType.DateTime, true),
					F("inspector", "Inspector", RmsFieldType.Person, false, true), F("permit_reference", "Health permit reference", RmsFieldType.ShortText)),
				Rows("temperatures", "Temperature checks", null, 100,
					F("food_item", "Item", RmsFieldType.ShortText, true), Select("holding", "Holding", true, true, "Hot", "Cold", "Frozen", "Ambient"),
					Quantity("temperature", "Temperature", "temperature", "C"), F("checked_on", "Checked on", RmsFieldType.DateTime),
					Select("within_range", "Within range", true, true, "Yes", "No")),
				Rows("items", "Inspection items", 1, 200,
					Select("area", "Area", true, true, "Hand washing", "Food handling", "Cold storage", "Hot holding", "Dish washing", "Waste", "Personal hygiene", "Pest control", "Water supply", "Other"),
					F("item", "Item", RmsFieldType.ShortText, true), Select("result", "Result", true, true, "Satisfactory", "Marginal", "Unsatisfactory", "Not applicable"),
					F("finding", "Finding", RmsFieldType.LongText), F("corrective_action", "Corrective action", RmsFieldType.LongText), F("corrected_on", "Corrected on", RmsFieldType.DateTime)),
				Section("outcome", "Outcome",
					Select("overall", "Overall", true, true, "Satisfactory", "Marginal", "Unsatisfactory"), F("summary", "Summary", RmsFieldType.LongText),
					F("embargo_issued", "Embargo or stop-service issued", RmsFieldType.Boolean, false, false, true), F("reinspection_due", "Reinspection due", RmsFieldType.Date)));

			yield return Policy(foodService, "regulatory", RmsFieldClassification.Restricted,
				"A health permit reference and an unsatisfactory finding against a named vendor is regulatory information about that vendor.",
				"permit_reference", "embargo_issued");

			yield return Ics("potable-water-test", "Potable water test", RecordDefinitionCategories.IncidentSupport,
				"A potable-water sample from an incident facility: where it came from, what was measured, and what was done about the result.",
				RmsLifecyclePreset.ReviewRequired, "H2OTST", SubjectsFacility,
				Header(false),
				Section("sample", "Sample",
					F("facility_name", "Facility", RmsFieldType.ShortText, true, true), F("source_description", "Source", RmsFieldType.ShortText, true),
					Select("source_kind", "Source kind", true, true, "Municipal", "Well", "Tanker", "Bottled", "Surface treated", "Other"),
					F("sampled_on", "Sampled on", RmsFieldType.DateTime, true), F("sampled_by", "Sampled by", RmsFieldType.Person, false, true),
					F("sample_identifier", "Sample identifier", RmsFieldType.ShortText, false, true), F("laboratory", "Laboratory", RmsFieldType.ShortText)),
				Rows("results", "Results", 1, 50,
					Select("analyte", "Analyte", true, true, "Free chlorine residual", "Total chlorine", "pH", "Turbidity", "Total coliform", "E. coli", "Temperature", "Other"),
					F("value", "Value", RmsFieldType.ShortText, true), F("unit_label", "Unit", RmsFieldType.ShortText),
					Select("result", "Result", true, true, "Pass", "Fail", "Inconclusive"), F("reported_on", "Reported on", RmsFieldType.DateTime)),
				Section("action", "Action",
					Select("disposition", "Disposition", true, true, "Approved for use", "Approved with treatment", "Not approved", "Retest required"),
					F("action_taken", "Action taken", RmsFieldType.LongText), F("boil_notice_issued", "Boil or do-not-drink notice issued", RmsFieldType.Boolean, false, false, true),
					F("retest_due", "Retest due", RmsFieldType.Date), F("report_document", "Laboratory report", RmsFieldType.Attachment)));

			yield return Ics("shift-ticket", "Support shift ticket", RecordDefinitionCategories.IncidentSupport,
				"A shift of support work that is not covered by an equipment agreement: who, what, where and how long.",
				RmsLifecyclePreset.QuickEntry, "SHFTKT", SubjectsBusiness,
				Header(false),
				Section("shift", "Shift",
					F("unit_name", "Unit or crew", RmsFieldType.ShortText, true, true, true), F("unit", "Unit", RmsFieldType.Unit, false, true),
					F("supervisor", "Supervisor", RmsFieldType.Person, false, true), F("shift_date", "Shift date", RmsFieldType.Date, true),
					F("started_on", "Start", RmsFieldType.DateTime, true), F("ended_on", "Stop", RmsFieldType.DateTime, true),
					Quantity("hours_worked", "Hours worked", "time", "h"), Count("persons", "Persons"),
					F("work_location", "Work location", RmsFieldType.ShortText)),
				Section("work", "Work",
					F("work_performed", "Work performed", RmsFieldType.LongText, true), F("materials_used", "Materials used", RmsFieldType.LongText),
					F("remarks", "Remarks", RmsFieldType.LongText), Ext("ledger_reference", "Approved time reference", "dtr"),
					F("supervisor_signature", "Supervisor signature", RmsFieldType.Signature)));

			yield return Ics("delivery-receiving-ticket", "Delivery and receiving ticket", RecordDefinitionCategories.IncidentSupport,
				"Something arrived at the incident: what, from whom, against which request, in what condition, and who signed for it.",
				RmsLifecyclePreset.QuickEntry, "RCVTKT", SubjectsFacility,
				Header(false),
				Section("delivery", "Delivery",
					F("received_on", "Received on", RmsFieldType.DateTime, true), F("received_at", "Received at", RmsFieldType.ShortText, true, true),
					F("received_by", "Received by", RmsFieldType.Person, true, true), F("carrier", "Carrier or vendor", RmsFieldType.ShortText, false, true),
					F("waybill", "Waybill or tracking", RmsFieldType.ShortText, false, true), Ext("request_number", "Request number", "request"),
					Ext("supply_reference", "Cache or supply request", RmsExternalReferenceSchemes.NfesIclip),
					Ext("lscms_reference", "LSCMS reference", RmsExternalReferenceSchemes.Lscms)),
				Rows("items", "Items received", 1, 200,
					F("item", "Item", RmsFieldType.ShortText, true, true), F("item_identifier", "Item identifier", RmsFieldType.ShortText),
					Count("quantity_expected", "Expected"), Count("quantity_received", "Received"), F("unit_of_issue", "Unit of issue", RmsFieldType.ShortText),
					Select("condition", "Condition", true, true, "Good", "Damaged", "Short", "Over", "Wrong item", "Refused"),
					F("discrepancy_note", "Discrepancy", RmsFieldType.LongText), F("inventory_item", "Inventory item", RmsFieldType.InventoryReference)),
				Section("signature", "Signature",
					F("driver_name", "Driver or courier", RmsFieldType.ShortText), F("receiver_signature", "Receiver signature", RmsFieldType.Signature, true),
					F("photo", "Photo", RmsFieldType.Attachment), Ext("vendor_invoice", "Vendor invoice number", RmsExternalReferenceSchemes.VendorInvoice)));

			yield return Ics("corrective-action", "Corrective action", RecordDefinitionCategories.IncidentSupport,
				"Something needs fixing: what was found, who owns it, when it is due, and the evidence it closed.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "CORACT", SubjectsCommand,
				Header(false),
				Section("finding", "Finding",
					F("title", "Title", RmsFieldType.ShortText, true, true, true), Select("source", "Source", true, true, "Safety inspection", "Sanitation inspection", "Food service inspection", "Equipment inspection", "Accident", "Near miss", "Hotwash", "After-action review", "Audit", "Other"),
					Ext("source_reference", "Source record", "record"), F("identified_on", "Identified on", RmsFieldType.DateTime, true),
					F("identified_by", "Identified by", RmsFieldType.Person, false, true), F("description", "Description", RmsFieldType.LongText, true),
					Select("severity", "Severity", true, true, "Low", "Medium", "High", "Critical")),
				Section("action", "Action",
					F("action_required", "Action required", RmsFieldType.LongText, true), F("owner", "Owner", RmsFieldType.Person, false, true),
					F("owner_position", "Owner position", RmsFieldType.ShortText), F("due_on", "Due on", RmsFieldType.Date, true),
					Select("status", "Status", true, true, "Open", "In progress", "Blocked", "Closed", "Cancelled"),
					F("work_order", "Work order", RmsFieldType.ChecklistWorkOrderReference)),
				Section("closure", "Closure",
					F("closure_note", "Closure note", RmsFieldType.LongText), F("closed_on", "Closed on", RmsFieldType.DateTime),
					F("closed_by", "Closed by", RmsFieldType.Person, false, true), F("evidence", "Closure evidence", RmsFieldType.Attachment),
					F("verified_by", "Verified by", RmsFieldType.Person, false, true)));

			var accident = Ics("incident-accident-report", "Incident accident report", RecordDefinitionCategories.IncidentSupport,
				"An accident, injury or near miss that happened on the incident: what occurred, who was involved, what was damaged and what was reported onward. Retained permanently, like every casualty and exposure record.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "ACCRPT", SubjectsParticipant,
				Header(false),
				Section("event", "Event",
					Select("event_kind", "Kind", true, true, "Injury", "Illness", "Near miss", "Vehicle accident", "Equipment damage", "Property damage", "Exposure", "Other"),
					F("occurred_on", "Occurred on", RmsFieldType.DateTime, true), F("location", "Location", RmsFieldType.Address, true),
					F("division_group", "Division or group", RmsFieldType.ShortText, false, true), F("activity_at_time", "Activity at the time", RmsFieldType.LongText),
					F("description", "Description", RmsFieldType.LongText, true), Select("severity", "Severity", true, true, "No injury", "First aid", "Medical treatment", "Lost time", "Hospitalization", "Fatality")),
				Rows("involved", "People involved", null, 50,
					F("member", "Member", RmsFieldType.Person), F("name_text", "Name (not a member)", RmsFieldType.ShortText),
					Select("involvement", "Involvement", true, false, "Injured", "Exposed", "Operator", "Witness", "Supervisor", "Other"),
					F("home_agency", "Home agency or unit", RmsFieldType.ShortText), F("injury_description", "Injury or illness", RmsFieldType.LongText),
					F("treatment", "Treatment", RmsFieldType.LongText), F("transported_to", "Transported to", RmsFieldType.ShortText),
					F("returned_to_duty", "Returned to duty", RmsFieldType.Boolean)),
				Rows("property", "Property and equipment", null, 50,
					F("item", "Item", RmsFieldType.ShortText, true), F("owner", "Owner", RmsFieldType.ShortText), F("damage", "Damage", RmsFieldType.LongText),
					Money("estimated_cost", "Estimated cost"), F("out_of_service", "Out of service", RmsFieldType.Boolean)),
				Section("cause", "Cause and prevention",
					F("contributing_factors", "Contributing factors", RmsFieldType.LongText), F("immediate_actions", "Immediate actions taken", RmsFieldType.LongText),
					F("prevention", "Prevention recommendations", RmsFieldType.LongText), Ext("corrective_action_reference", "Corrective action", "record")),
				Section("reporting", "Onward reporting",
					Multi("reported_to", "Reported to", "Safety officer", "Incident commander", "Home agency", "Compensation and claims", "Regulator", "Law enforcement", "Vendor"),
					F("reported_on", "Reported on", RmsFieldType.DateTime), F("regulator_reference", "Regulator reference", RmsFieldType.ShortText),
					F("claim_reference", "Compensation or claim reference", RmsFieldType.ShortText), F("photo", "Photo", RmsFieldType.Attachment)),
				Approval("Approved by Safety Officer"));

			accident.RetentionYears = 0;
			yield return Policy(
				Policy(accident, "treatment-casualty", RmsFieldClassification.Restricted,
					"Injury, illness and treatment detail about a named person is restricted whoever recorded it.",
					"injury_description", "treatment", "transported_to", "returned_to_duty", "involvement", "name_text"),
				"regulatory", RmsFieldClassification.Restricted,
				"Regulator and compensation references identify an open matter about a named person or agency.",
				"regulator_reference", "claim_reference");
		}

		// ---- Business administration ----------------------------------------------------------------------------

		private static IEnumerable<RecordTemplateDefinition> BusinessAdministration()
		{
			yield return Ics("delegation-of-authority", "Delegation of authority", RecordDefinitionCategories.IncidentBusiness,
				"The signed delegation that puts an incident commander in charge: from whom, to whom, for what, with which constraints and until when.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "DELEG", SubjectsCommand,
				Header(false),
				Section("delegation", "Delegation",
					F("delegating_official", "Delegating official", RmsFieldType.ShortText, true, true), F("delegating_position", "Position", RmsFieldType.ShortText),
					F("delegating_agency", "Agency", RmsFieldType.ShortText, true, true), F("incident_commander", "Incident commander", RmsFieldType.Person, false, true),
					F("incident_commander_text", "Incident commander (not a member)", RmsFieldType.ShortText), F("effective_from", "Effective from", RmsFieldType.DateTime, true),
					F("effective_to", "Effective to", RmsFieldType.DateTime), Select("incident_complexity", "Incident complexity", false, true, "Type 5", "Type 4", "Type 3", "Type 2", "Type 1")),
				Rows("objectives", "Agency objectives and priorities", 1, 50,
					Count("priority", "Priority"), F("objective", "Objective", RmsFieldType.LongText, true), F("measure", "How success is measured", RmsFieldType.LongText)),
				Section("constraints", "Authorities and constraints",
					F("authorities_granted", "Authorities granted", RmsFieldType.LongText, true), F("financial_limit_note", "Financial limitations", RmsFieldType.LongText),
					F("constraints", "Constraints and limitations", RmsFieldType.LongText), F("agency_administrator_contact", "Agency administrator contact", RmsFieldType.ShortText),
					F("reporting_expectations", "Reporting expectations", RmsFieldType.LongText), F("delegation_document", "Signed delegation", RmsFieldType.Attachment)),
				Section("signatures", "Signatures",
					F("delegating_signature", "Delegating official signature", RmsFieldType.Signature, true), F("commander_signature", "Incident commander signature", RmsFieldType.Signature, true),
					F("signed_on", "Signed on", RmsFieldType.DateTime, true)),
				Approval("Accepted by Incident Commander"));

			var funding = Ics("funding-authorization", "Funding authorization", RecordDefinitionCategories.IncidentBusiness,
				"The authority to spend on this incident: which account, up to what ceiling, for what, and who authorized it.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "FUNDAU", SubjectsCommand,
				Header(false),
				Section("authorization", "Authorization",
					F("authorizing_official", "Authorizing official", RmsFieldType.ShortText, true, true), F("authorizing_agency", "Agency", RmsFieldType.ShortText, true, true),
					F("authorized_on", "Authorized on", RmsFieldType.DateTime, true), F("effective_from", "Effective from", RmsFieldType.Date),
					F("effective_to", "Effective to", RmsFieldType.Date), Select("authorization_kind", "Kind", true, true, "Initial", "Increase", "Decrease", "Extension", "Closure")),
				Rows("accounts", "Accounts and ceilings", 1, 50,
					F("account_code", "Account or job code", RmsFieldType.ShortText, true), F("description", "Description", RmsFieldType.ShortText),
					Money("ceiling", "Ceiling"), F("purpose", "Purpose", RmsFieldType.LongText), Ext("finance_reference", "Finance posting", RmsExternalReferenceSchemes.FinancePosting)),
				Section("terms", "Terms",
					F("permitted_expenditures", "Permitted expenditures", RmsFieldType.LongText), F("restrictions", "Restrictions", RmsFieldType.LongText),
					F("reporting_requirements", "Reporting requirements", RmsFieldType.LongText), F("authorization_document", "Signed authorization", RmsFieldType.Attachment)),
				Approval("Approved by Agency Administrator"));

			yield return Policy(funding, "regulatory", RmsFieldClassification.Restricted,
				"Account codes and spending ceilings are financial control data.", "account_code", "ceiling", "finance_reference");

			var costShare = Ics("cost-share-agreement", "Cost-share agreement", RecordDefinitionCategories.IncidentBusiness,
				"How the incident's cost is divided between jurisdictions: the parties, the basis, the split and the signatures.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "COSTSH", SubjectsCommand,
				Header(false),
				Section("agreement", "Agreement",
					Ext("agreement_number", "Agreement number", "agreement", true), F("effective_from", "Effective from", RmsFieldType.Date, true),
					F("effective_to", "Effective to", RmsFieldType.Date), Select("basis", "Cost-share basis", true, true, "Acreage", "Resource use", "Fixed percentage", "Actual cost by jurisdiction", "Daily negotiated", "Other"),
					F("basis_description", "Basis description", RmsFieldType.LongText, true)),
				Rows("parties", "Parties and shares", 2, 20,
					F("party", "Party", RmsFieldType.ShortText, true, true), F("jurisdiction", "Jurisdiction", RmsFieldType.ShortText),
					Count("share_percent", "Share percent"), Money("estimated_share", "Estimated share"),
					F("account_code", "Account or job code", RmsFieldType.ShortText), F("representative", "Representative", RmsFieldType.ShortText),
					F("signature", "Signature", RmsFieldType.Signature)),
				Section("terms", "Terms",
					F("included_costs", "Costs included", RmsFieldType.LongText), F("excluded_costs", "Costs excluded", RmsFieldType.LongText),
					F("reconciliation", "Reconciliation and billing", RmsFieldType.LongText), F("dispute_resolution", "Dispute resolution", RmsFieldType.LongText),
					F("agreement_document", "Signed agreement", RmsFieldType.Attachment)),
				Approval("Approved by Agency Administrators"));

			yield return Policy(costShare, "regulatory", RmsFieldClassification.Restricted,
				"Negotiated shares and account codes between jurisdictions are financial terms.", "share_percent", "estimated_share", "account_code");

			var businessSummary = Ics("incident-business-summary", "Incident business summary", RecordDefinitionCategories.IncidentBusiness,
				"The finance section's close-out summary: cost by category as of a stated cursor, the packages produced, and what is still open. Every figure names the ledger cursor it was rendered from.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "IBSUM", SubjectsCommand,
				Header(false),
				Section("summary", "Summary",
					F("as_of", "As of", RmsFieldType.DateTime, true), F("cursor_note", "Ledger cursor", RmsFieldType.ShortText),
					F("finance_chief", "Finance section chief", RmsFieldType.Person, false, true), Money("total_cost", "Total cost"),
					Ext("finance_reference", "Finance posting", RmsExternalReferenceSchemes.FinancePosting), Ext("eisuite_reference", "e-ISuite reference", RmsExternalReferenceSchemes.EIsuite)),
				Rows("by_category", "Cost by category", 1, 100,
					Select("category", "Category", true, true, "Personnel", "Overtime", "Travel", "Equipment", "Aircraft", "Supplies", "Facilities", "Food service", "Contracts", "Claims", "Other"),
					Money("estimated", "Estimated"), Money("committed", "Committed"), Money("accrued", "Accrued"), Money("actual", "Actual"), Money("invoiced", "Invoiced"),
					F("notes", "Notes", RmsFieldType.LongText)),
				Rows("packages", "Packages produced", null, 50,
					Select("package_kind", "Package", true, true, "Time package", "Equipment package", "Claims package", "Cost-share package", "Reimbursement package", "Property package", "Other"),
					F("reference", "Reference", RmsFieldType.ShortText), F("produced_on", "Produced on", RmsFieldType.Date),
					Select("status", "Status", false, true, "In progress", "Complete", "Transmitted", "Reconciled"), F("document", "Document", RmsFieldType.Attachment)),
				Section("open", "Open items",
					F("open_items", "Open items", RmsFieldType.LongText), F("claims_note", "Claims outstanding", RmsFieldType.LongText),
					Ext("emac_reference", "EMAC reimbursement", RmsExternalReferenceSchemes.Emac)),
				Approval("Approved by Finance Section Chief"));

			yield return Policy(businessSummary, "regulatory", RmsFieldClassification.Restricted,
				"Incident cost by category is unreleased financial data until the agency publishes it.",
				"total_cost", "estimated", "committed", "accrued", "actual", "invoiced", "finance_reference", "eisuite_reference", "emac_reference");

			var purchase = Ics("purchase-justification", "Purchase justification", RecordDefinitionCategories.IncidentBusiness,
				"Why the incident bought something outside the normal supply channel, and who approved it.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "PURJUS", SubjectsBusiness,
				Header(false),
				Section("purchase", "Purchase",
					F("requested_by", "Requested by", RmsFieldType.Person, true, true), F("requested_position", "Position", RmsFieldType.ShortText),
					F("requested_on", "Requested on", RmsFieldType.DateTime, true), F("vendor", "Vendor", RmsFieldType.Contact, false, true),
					F("vendor_text", "Vendor (not a contact)", RmsFieldType.ShortText), Money("estimated_amount", "Estimated amount"),
					Select("method", "Procurement method", true, true, "Purchase card", "Purchase order", "Blanket agreement", "Emergency purchase", "Cash", "Other"),
					Ext("request_number", "Request number", "request")),
				Rows("items", "Items", 1, 100,
					F("item", "Item", RmsFieldType.ShortText, true), Count("quantity", "Quantity"), F("unit_of_issue", "Unit of issue", RmsFieldType.ShortText),
					Money("unit_price", "Unit price"), F("specification", "Specification", RmsFieldType.LongText)),
				Section("justification", "Justification",
					F("justification", "Justification", RmsFieldType.LongText, true), F("why_not_cache", "Why the cache or normal channel was not used", RmsFieldType.LongText, true),
					F("alternatives_considered", "Alternatives considered", RmsFieldType.LongText), F("urgency", "Urgency", RmsFieldType.LongText)),
				Section("approval_detail", "Approval detail",
					F("account_code", "Account or job code", RmsFieldType.ShortText), Ext("finance_reference", "Finance posting", RmsExternalReferenceSchemes.FinancePosting),
					Ext("vendor_invoice", "Vendor invoice number", RmsExternalReferenceSchemes.VendorInvoice), F("receipt", "Receipt", RmsFieldType.Attachment)),
				Approval("Approved by Procurement Unit Leader"));

			yield return Policy(purchase, "regulatory", RmsFieldClassification.Restricted,
				"Prices, account codes and invoice references are commercial and financial control data.",
				"estimated_amount", "unit_price", "account_code", "finance_reference", "vendor_invoice");

			var conflict = Ics("conflict-of-interest-attestation", "Conflict-of-interest attestation", RecordDefinitionCategories.IncidentBusiness,
				"An attestation from someone in a procurement or contracting role on the incident: what interests they hold, and what they recused themselves from.",
				RmsLifecyclePreset.ApprovalAcknowledgement, "COIATT", SubjectsBusiness,
				Header(false),
				Section("attestor", "Attestor",
					F("member", "Member", RmsFieldType.Person, false, true), F("name_text", "Name (not a member)", RmsFieldType.ShortText),
					F("position", "Incident position", RmsFieldType.ShortText, true, true), F("home_agency", "Home agency or unit", RmsFieldType.ShortText, false, true),
					F("attested_on", "Attested on", RmsFieldType.DateTime, true), Select("scope", "Scope", true, true, "Procurement", "Contracting", "Equipment inspection", "Payment approval", "Cost share", "Other")),
				Rows("disclosures", "Disclosures", null, 50,
					Select("interest_kind", "Interest", true, false, "Financial", "Employment", "Family or household", "Ownership", "Gift or hospitality", "Prior relationship", "Other"),
					F("party", "Party", RmsFieldType.ShortText, true), F("description", "Description", RmsFieldType.LongText, true),
					F("recusal", "Recusal or mitigation", RmsFieldType.LongText)),
				Section("statement", "Statement",
					F("no_conflicts", "No conflicts to disclose", RmsFieldType.Boolean), F("statement", "Statement", RmsFieldType.LongText),
					F("attestation_signature", "Attestation signature", RmsFieldType.Signature, true)),
				Approval("Reviewed by Finance Section Chief"));

			yield return Policy(conflict, "regulatory", RmsFieldClassification.Restricted,
				"A disclosed personal or financial interest is about a named individual and their relationships.",
				"interest_kind", "party", "description", "recusal", "statement");
		}
	}
}
