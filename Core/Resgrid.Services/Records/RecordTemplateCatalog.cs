using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Services.Records
{
	/// <summary>A product pack as shipped in code: metadata plus the definitions it carries.</summary>
	public sealed class RecordTemplatePack
	{
		public string Key { get; set; }
		public int Version { get; set; } = 1;
		public string Name { get; set; }
		public string Category { get; set; }
		public string Description { get; set; }
		public bool IsPreview { get; set; }
		public RmsArtifactStatus ArtifactStatus { get; set; } = RmsArtifactStatus.Compatible;
		public List<string> SupportedProfiles { get; set; } = new List<string> { "generic", "us", "ca" };
		public List<string> SupportedLocales { get; set; } = new List<string> { "en-US", "en-CA", "fr-CA" };
		public DateTime? ReviewedOn { get; set; } = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);
		public string ReleaseNotes { get; set; }
		public List<RmsSourceProvenance> Sources { get; set; } = new List<RmsSourceProvenance>();
		public List<RecordTemplateDefinition> Definitions { get; set; } = new List<RecordTemplateDefinition>();
	}

	/// <summary>
	/// Product-managed template packs (RMS plan section 4.1 table; RMS-1B launch templates and RMS-1C operational
	/// packs) and the locked jurisdiction profiles. Content lives here, in code, so a pack update ships as a product
	/// release with a diff; it never mutates a department clone. Nothing here is labeled an exact named form.
	/// </summary>
	public static class RecordTemplateCatalog
	{
		public const int CatalogVersion = 1;

		// ---- profiles ---------------------------------------------------------------------------------

		public static readonly IReadOnlyList<RmsJurisdictionProfileVersion> Profiles = new List<RmsJurisdictionProfileVersion>
		{
			Profile("generic", "Generic (no jurisdiction)", "XX", null, "en-US", "en-US,en-CA,fr-CA", "metric", null, RmsArtifactStatus.DepartmentLocal,
				new Dictionary<string, Dictionary<string, string>>()),
			Profile("us", "United States", "US", null, "en-US", "en-US", "customary", "USD", RmsArtifactStatus.Compatible,
				new Dictionary<string, Dictionary<string, string>>
				{
					["en-US"] = new Dictionary<string, string> { ["subdivision"] = "State", ["postal_code"] = "ZIP code", ["municipality"] = "City", ["province"] = "State", ["mileage"] = "Mileage (mi)", ["fuel"] = "Fuel (gal)", ["temperature"] = "Temperature (°F)", ["area"] = "Area (acres)" }
				},
				Source("NIMS/ICS forms", "FEMA National Incident Management System ICS forms", "FEMA", "2023", "https://www.fema.gov/emergency-managers/nims/components"),
				Source("NIFC mobilization guide", "NIFC mobilization and resource-order guidance", "NIFC", "2026", "https://www.nifc.gov/nicc/logistics/reference-documents"),
				Source("NWCG PMS 310-1", "NWCG Standards for Wildland Fire Position Qualifications", "NWCG", "2025", "https://www.nwcg.gov/publications/pms310-1")),
			Profile("ca", "Canada", "CA", null, "en-CA", "en-CA,fr-CA", "metric", "CAD", RmsArtifactStatus.Compatible,
				new Dictionary<string, Dictionary<string, string>>
				{
					["en-CA"] = new Dictionary<string, string> { ["subdivision"] = "Province/territory", ["postal_code"] = "Postal code", ["municipality"] = "Municipality", ["province"] = "Province/territory", ["mileage"] = "Distance (km)", ["fuel"] = "Fuel (L)", ["temperature"] = "Temperature (°C)", ["area"] = "Area (ha)" },
					["fr-CA"] = new Dictionary<string, string> { ["subdivision"] = "Province/territoire", ["postal_code"] = "Code postal", ["municipality"] = "Municipalité", ["province"] = "Province/territoire", ["mileage"] = "Distance (km)", ["fuel"] = "Carburant (L)", ["temperature"] = "Température (°C)", ["area"] = "Superficie (ha)" }
				},
				Source("ICS Canada forms", "ICS Canada incident forms (204, 209, 214, 221)", "ICS Canada", "2024", "https://www.icscanada.ca/"),
				Source("CIFFC MARS", "Mutual Aid and Resource Sharing agreement and guidelines", "CIFFC", "2025", "https://dev.ciffc.ca/download/mutual-aid-and-resource-sharing/")),
			Profile("us-ca", "U.S.–Canada cross-border", "US-CA", null, "en-US", "en-US,en-CA,fr-CA", "metric", null, RmsArtifactStatus.Compatible,
				new Dictionary<string, Dictionary<string, string>>(),
				Source("International Mobilization Guide", "NIFC International Mobilization Guide", "NIFC", "2026", "https://www.nifc.gov/sites/default/files/NICC/3-Logistics/Reference%20Documents/2026_International_Mobilization_Guide_FINAL.pdf"))
		};

		// ---- packs ------------------------------------------------------------------------------------

		public static readonly IReadOnlyList<RecordTemplatePack> Packs = new List<RecordTemplatePack>
		{
			LaunchPack(),
			CertPack(),
			SarPack(),
			DisasterPack(),
			EocPack(),
			HazmatPack(),
			IndustrialPack(),
			ExercisePack(),
			MutualAidPack()
		};

		public static IEnumerable<RecordTemplateDefinition> AllTemplates => Packs.SelectMany(p => p.Definitions);

		public static RecordTemplateDefinition Find(string key) => AllTemplates.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

		public static RecordTemplatePack PackOf(string templateKey) => Packs.FirstOrDefault(p => p.Definitions.Any(d => string.Equals(d.Key, templateKey, StringComparison.OrdinalIgnoreCase)));

		public static RmsJurisdictionProfileVersion FindProfile(string key) => Profiles.FirstOrDefault(p => string.Equals(p.ProfileKey, key, StringComparison.OrdinalIgnoreCase));

		// ---- builders ---------------------------------------------------------------------------------

		private static RmsJurisdictionProfileVersion Profile(string key, string name, string country, string subdivision, string locale, string locales, string measurement, string currency, RmsArtifactStatus status, Dictionary<string, Dictionary<string, string>> terminology, params RmsSourceProvenance[] sources)
		{
			return new RmsJurisdictionProfileVersion
			{
				RmsJurisdictionProfileVersionId = "profile:" + key + ":1", DepartmentId = RmsTemplatePackVersion.ProductDepartmentId, ProtectionId = "profile:" + key,
				ProfileKey = key, Version = 1, Name = name, Country = country, Subdivision = subdivision, DefaultLocale = locale, SupportedLocales = locales,
				MeasurementSystem = measurement, CurrencyCode = currency, ArtifactStatus = (int)status, ReviewedOn = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc),
				TerminologyJson = Newtonsoft.Json.JsonConvert.SerializeObject(terminology), StandardsJson = Newtonsoft.Json.JsonConvert.SerializeObject(sources.ToList()),
				ClassificationDefault = (int)RmsFieldClassification.Standard, CreatedOn = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc), ModifiedOn = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc), RowVersion = 1
			};
		}

		private static RmsSourceProvenance Source(string identifier, string title, string publisher, string version, string url, string kind = "operational-aid")
			=> new RmsSourceProvenance { Identifier = identifier, Title = title, Publisher = publisher, Version = version, Url = url, ReviewedOn = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc), Kind = kind };

		private static RecordSectionSchema Section(string key, string label, params RecordFieldSchema[] fields) => new RecordSectionSchema { Key = key, Label = label, Fields = fields.ToList() };
		private static RecordSectionSchema Rows(string key, string label, int? min, int? max, params RecordFieldSchema[] fields) => new RecordSectionSchema { Key = key, Label = label, Repeating = true, MinRows = min, MaxRows = max, Fields = fields.ToList() };

		private static RecordFieldSchema F(string key, string label, RmsFieldType type, bool required = false, bool searchable = false, bool workflow = false, RmsFieldClassification classification = RmsFieldClassification.Standard)
		{
			var filterable = type != RmsFieldType.LongText && type != RmsFieldType.Attachment && type != RmsFieldType.Signature;
			return new RecordFieldSchema
			{
				Key = key, Label = label, Type = type, RequiredToFinalize = required, Classification = classification,
				Searchable = searchable && classification == RmsFieldClassification.Standard && type != RmsFieldType.Attachment && type != RmsFieldType.Signature,
				Filterable = filterable, Sortable = filterable, Groupable = RecordDefinitionsService.IsGroupable(type) && classification == RmsFieldClassification.Standard,
				Aggregatable = RecordDefinitionsService.IsNumeric(type) && classification == RmsFieldClassification.Standard,
				WorkflowExposed = workflow && classification == RmsFieldClassification.Standard, Exportable = true
			};
		}

		private static RecordFieldSchema Select(string key, string label, bool required, bool workflow, params string[] options)
		{
			var field = F(key, label, RmsFieldType.SingleSelect, required, true, workflow);
			field.Options = options.Select(o => new RecordOptionSchema { Key = o.ToLowerInvariant().Replace(' ', '-'), Label = o }).ToList();
			return field;
		}

		private static RecordFieldSchema Multi(string key, string label, params string[] options)
		{
			var field = F(key, label, RmsFieldType.MultiSelect, false, true, false);
			field.Options = options.Select(o => new RecordOptionSchema { Key = o.ToLowerInvariant().Replace(' ', '-'), Label = o }).ToList();
			return field;
		}

		private static RecordFieldSchema Quantity(string key, string label, string family, string unit, bool required = false)
		{
			var field = F(key, label, RmsFieldType.Quantity, required, false, true);
			field.UnitFamily = family; field.DefaultUnit = unit;
			return field;
		}

		private static RecordFieldSchema Money(string key, string label, string currency = "USD")
		{
			var field = F(key, label, RmsFieldType.Currency, false, false, true);
			field.DefaultCurrency = currency;
			return field;
		}

		private static RecordFieldSchema Ext(string key, string label, string scheme, bool required = false)
		{
			var field = F(key, label, RmsFieldType.ExternalReference, required, true, true);
			field.ReferenceType = scheme;
			return field;
		}

		private static RecordFieldSchema Decimal(string key, string label, string unitLabel, bool workflow = true)
		{
			var field = F(key, label, RmsFieldType.Decimal, false, false, workflow);
			field.FixedUnitLabel = unitLabel; field.Min = 0;
			return field;
		}

		private static RecordFieldSchema Count(string key, string label, bool workflow = true)
		{
			var field = F(key, label, RmsFieldType.Integer, false, false, workflow);
			field.Min = 0;
			return field;
		}

		/// <summary>A pack protected-data policy: the fields of one category carry a classification floor in every rendering and clone (RMS-1C).</summary>
		private static RecordTemplateDefinition Policy(RecordTemplateDefinition template, string category, RmsFieldClassification floor, string rationale, params string[] fieldKeys)
		{
			template.ProtectedDataPolicies.Add(new RecordTemplateFieldPolicy { Category = category, Floor = floor, Rationale = rationale, FieldKeys = fieldKeys.ToList() });
			return template;
		}

		private static RecordRuleSchema ShowWhen(string fieldKey, string value) => new RecordRuleSchema { Effect = RmsRuleEffect.Show, Condition = new RecordConditionSchema { Operator = RmsRuleOperator.Equals, FieldKey = fieldKey, Value = value } };
		private static RecordRuleSchema ShowWhenIn(string fieldKey, params string[] values) => new RecordRuleSchema { Effect = RmsRuleEffect.Show, Condition = new RecordConditionSchema { Operator = RmsRuleOperator.InSet, FieldKey = fieldKey, Values = values.ToList() } };
		private static RecordRuleSchema RequireWhen(string fieldKey, string value) => new RecordRuleSchema { Effect = RmsRuleEffect.Require, Condition = new RecordConditionSchema { Operator = RmsRuleOperator.Equals, FieldKey = fieldKey, Value = value } };

		private static RecordTemplateDefinition Template(string key, string packKey, string name, string category, string description, RmsLifecyclePreset preset, string prefix, string subjects, params RecordSectionSchema[] sections)
			=> new RecordTemplateDefinition { Key = key, PackKey = packKey, Name = name, Category = category, Description = description, LifecyclePreset = preset, NumberPrefix = prefix, PermittedSubjectTypes = subjects, Schema = new RecordDefinitionSchema { Sections = sections.ToList() } };

		private static RecordTemplateDefinition WithOverlays(RecordTemplateDefinition template, Dictionary<string, string> frCa = null, Dictionary<string, string> usUnits = null, Dictionary<string, string> caUnits = null, params string[] lockedClassification)
		{
			template.Overlays["generic"] = new RecordTemplateOverlay { ProfileKey = "generic", ArtifactStatus = RmsArtifactStatus.DepartmentLocal };
			// Sources come from the owning pack at render time (the pack list is still being built here).
			template.Overlays["us"] = new RecordTemplateOverlay { ProfileKey = "us", CurrencyCode = "USD", DefaultUnits = usUnits ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), ArtifactStatus = RmsArtifactStatus.Compatible };
			template.Overlays["ca"] = new RecordTemplateOverlay { ProfileKey = "ca", CurrencyCode = "CAD", DefaultUnits = caUnits ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), ArtifactStatus = RmsArtifactStatus.Compatible, Labels = frCa == null ? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase) { ["fr-CA"] = frCa } };
			template.Overlays["us-ca"] = new RecordTemplateOverlay { ProfileKey = "us-ca", DefaultUnits = caUnits ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), ArtifactStatus = RmsArtifactStatus.Compatible, Labels = frCa == null ? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase) { ["fr-CA"] = frCa } };
			template.LockedClassificationFieldKeys = lockedClassification.ToList();
			return template;
		}

		private static Dictionary<string, string> Fr(params string[] pairs)
		{
			var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			for (var i = 0; i + 1 < pairs.Length; i += 2) d[pairs[i]] = pairs[i + 1];
			return d;
		}

		// ---- RMS-1B launch templates ----------------------------------------------------------------

		private static RecordTemplatePack LaunchPack()
		{
			var pack = new RecordTemplatePack
			{
				Key = "template.launch", Name = "Operational report templates", Category = "Operations", Description = "Cross-vertical starting points: security patrol, security incident, delivery run, bus/route end-of-day, shift summary and job completion.",
				SupportedProfiles = new List<string> { "generic", "us", "ca" }, ArtifactStatus = RmsArtifactStatus.DepartmentLocal, ReleaseNotes = "Initial RMS-1B release."
			};

			var patrol = Template("template.security-patrol", pack.Key, "Security Patrol Log", "Security", "Site patrol with checkpoint rows, observations, exceptions and supervisor handoff.", RmsLifecyclePreset.QuickEntry, "PAT", "contact,unit",
				Section("assignment", "Assignment",
					F("client_site", "Client / site", RmsFieldType.Contact, true, true, true), F("shift_start", "Shift start", RmsFieldType.DateTime, true), F("shift_end", "Shift end", RmsFieldType.DateTime),
					F("officer", "Officer", RmsFieldType.Person, true, true, true), F("unit", "Unit / vehicle", RmsFieldType.Unit, false, true, true), F("post", "Post / route", RmsFieldType.ShortText, false, true, true)),
				Rows("checkpoints", "Patrol checkpoints", null, 200,
					F("checkpoint", "Checkpoint", RmsFieldType.ShortText, true, true), F("time", "Time", RmsFieldType.DateTime, true), Select("status", "Status", true, true, "Clear", "Exception"), F("notes", "Notes", RmsFieldType.LongText)),
				Section("observations", "Observations",
					F("observations", "Observations", RmsFieldType.LongText), F("exception_reported", "Exception reported", RmsFieldType.Boolean, false, false, true)),
				Rows("exceptions", "Exceptions", null, 50,
					Select("type", "Type", true, true, "Trespass", "Damage", "Alarm", "Safety hazard", "Access control", "Other"), Select("severity", "Severity", true, true, "Low", "Medium", "High"),
					F("description", "Description", RmsFieldType.LongText, true), F("action_taken", "Action taken", RmsFieldType.LongText), F("photo", "Photo", RmsFieldType.Attachment)),
				Section("handoff", "Supervisor handoff",
					F("supervisor", "Supervisor", RmsFieldType.Person, false, true), F("handoff_notes", "Handoff notes", RmsFieldType.LongText), F("acknowledgement", "Officer acknowledgement", RmsFieldType.Signature, true)));
			patrol.Schema.FindSection("exceptions").Rules.Add(ShowWhen("exception_reported", "true"));
			pack.Definitions.Add(WithOverlays(patrol, Fr("client_site", "Client / site", "shift_start", "Début du quart", "shift_end", "Fin du quart", "officer", "Agent", "observations", "Observations")));

			var incident = Template("template.security-incident", pack.Key, "Security Incident Report", "Security", "Incident class and severity, time and place, involved persons, narrative, actions, evidence, notifications and review.", RmsLifecyclePreset.ReviewRequired, "SIR", "contact,call",
				Section("classification", "Classification",
					Select("incident_class", "Incident class", true, true, "Theft", "Assault", "Trespass", "Vandalism", "Medical", "Fire alarm", "Suspicious activity", "Policy violation", "Other"),
					Select("severity", "Severity", true, true, "Low", "Medium", "High", "Critical"), F("occurred_at", "Occurred at", RmsFieldType.DateTime, true), F("location", "Location", RmsFieldType.Address, true, true), F("site", "Site", RmsFieldType.Contact, false, true, true)),
				Rows("involved", "Involved persons", null, 50,
					F("name", "Name", RmsFieldType.ShortText, true, false, false, RmsFieldClassification.Restricted), Select("role", "Role", true, false, "Complainant", "Suspect", "Witness", "Employee", "Visitor"), F("contact", "Contact details", RmsFieldType.ShortText, false, false, false, RmsFieldClassification.Restricted)),
				Section("narrative", "Narrative", F("narrative", "Narrative", RmsFieldType.LongText, true)),
				Section("actions", "Actions and escalation",
					F("actions_taken", "Actions taken", RmsFieldType.LongText, true), F("escalated", "Escalated", RmsFieldType.Boolean, false, false, true), F("escalated_to", "Escalated to", RmsFieldType.ShortText)),
				Rows("evidence", "Evidence references", null, 50, Ext("reference", "Reference", "evidence"), F("description", "Description", RmsFieldType.ShortText), F("file", "File", RmsFieldType.Attachment)),
				Section("notifications", "Notifications",
					F("police_notified", "Police notified", RmsFieldType.Boolean, false, false, true), F("police_reference", "Police reference", RmsFieldType.ShortText), F("client_notified", "Client notified", RmsFieldType.Boolean, false, false, true)),
				Section("review", "Review", F("reviewer", "Reviewer", RmsFieldType.Person), F("review_notes", "Review notes", RmsFieldType.LongText)));
			incident.Schema.FindField("escalated_to").Rules.Add(ShowWhen("escalated", "true"));
			incident.Schema.FindField("escalated_to").Rules.Add(RequireWhen("escalated", "true"));
			incident.Schema.FindField("police_reference").Rules.Add(ShowWhen("police_notified", "true"));
			Policy(incident, "subject-clue-recovery", RmsFieldClassification.Restricted, "Involved persons are identifiable subjects; the report may be disclosed without them.", "name", "contact");
			pack.Definitions.Add(WithOverlays(incident, Fr("incident_class", "Catégorie d'incident", "severity", "Gravité", "occurred_at", "Survenu le", "location", "Lieu", "narrative", "Narratif"), null, null, "name", "contact"));

			var delivery = Template("template.delivery-run", pack.Key, "Delivery Run Report", "Delivery", "Route/run, driver, vehicle, service window, stops, completed and failed deliveries, mileage, exceptions and acknowledgement.", RmsLifecyclePreset.QuickEntry, "DLV", "unit,contact",
				Section("route", "Route",
					Ext("route_id", "Route / run", "route", true), F("driver", "Driver", RmsFieldType.Person, true, true, true), F("vehicle", "Vehicle", RmsFieldType.Unit, true, true, true),
					F("window_start", "Service window start", RmsFieldType.DateTime, true), F("window_end", "Service window end", RmsFieldType.DateTime)),
				Rows("stops", "Stops", null, 300,
					Count("stop_number", "Stop", false), F("customer", "Customer", RmsFieldType.Contact, false, true), F("arrived", "Arrived", RmsFieldType.DateTime), F("delivered", "Delivered", RmsFieldType.Boolean, true, false, true),
					Select("failure_reason", "Failure reason", false, true, "Not home", "Refused", "Address not found", "Damaged", "Other"), Ext("proof_of_delivery", "Proof of delivery", "pod"), F("notes", "Notes", RmsFieldType.ShortText)),
				Section("totals", "Totals",
					Count("stops_planned", "Stops planned"), Count("stops_completed", "Stops completed"), Count("stops_failed", "Stops failed"), Decimal("mileage", "Mileage", "mi")),
				Section("exceptions", "Exceptions and damage",
					F("damage_reported", "Damage reported", RmsFieldType.Boolean, false, false, true), F("damage_description", "Damage description", RmsFieldType.LongText), F("photos", "Photos", RmsFieldType.Attachment)),
				Section("acknowledgement", "Acknowledgement", F("dispatcher", "Dispatcher", RmsFieldType.Person), F("acknowledgement", "Driver acknowledgement", RmsFieldType.Signature, true)));
			delivery.Schema.FindField("failure_reason").Rules.Add(ShowWhen("delivered", "false")); // per-row: the stop's own delivered flag
			delivery.Schema.FindField("damage_description").Rules.Add(ShowWhen("damage_reported", "true"));
			delivery.Schema.FindField("damage_description").Rules.Add(RequireWhen("damage_reported", "true"));
			pack.Definitions.Add(WithOverlays(delivery, Fr("driver", "Chauffeur", "vehicle", "Véhicule", "mileage", "Distance (km)", "stops_completed", "Arrêts complétés")));

			var bus = Template("template.bus-route-eod", pack.Key, "Bus/Route End-of-Day Summary", "Transit", "Route, operator, vehicle, service window, mileage and ridership, delays, incidents, defects, lost property and handoff.", RmsLifecyclePreset.QuickEntry, "EOD", "unit",
				Section("route", "Route",
					Ext("route_id", "Route", "route", true), F("operator", "Operator", RmsFieldType.Person, true, true, true), F("vehicle", "Vehicle", RmsFieldType.Unit, true, true, true),
					F("service_start", "Service start", RmsFieldType.DateTime, true), F("service_end", "Service end", RmsFieldType.DateTime, true)),
				Section("counts", "Mileage and ridership", Decimal("mileage", "Mileage", "mi"), Count("ridership", "Ridership"), Count("trips_completed", "Trips completed"), Count("trips_missed", "Trips missed")),
				Rows("delays", "Delays", null, 100, F("time", "Time", RmsFieldType.DateTime, true), F("duration", "Duration", RmsFieldType.Duration, true), Select("cause", "Cause", true, true, "Traffic", "Mechanical", "Passenger", "Weather", "Detour", "Other"), F("notes", "Notes", RmsFieldType.ShortText)),
				Rows("incidents", "Incidents", null, 50, F("incident_time", "Time", RmsFieldType.DateTime, true), Select("type", "Type", true, true, "Passenger", "Collision", "Medical", "Fare", "Other"), F("incident_description", "Description", RmsFieldType.LongText, true), F("report_reference", "Report reference", RmsFieldType.ShortText)),
				Rows("defects", "Vehicle defects", null, 50, F("component", "Component", RmsFieldType.ShortText, true, true), Select("severity", "Severity", true, true, "Minor", "Major", "Out of service"), F("defect_description", "Description", RmsFieldType.LongText)),
				Rows("lost_property", "Lost property", null, 50, F("item", "Item", RmsFieldType.ShortText, true), F("found_at", "Found at", RmsFieldType.ShortText), F("turned_in_to", "Turned in to", RmsFieldType.ShortText)),
				Section("handoff", "Relief / maintenance handoff", F("relief_operator", "Relief operator", RmsFieldType.Person), F("maintenance_notes", "Maintenance notes", RmsFieldType.LongText), F("acknowledgement", "Operator acknowledgement", RmsFieldType.Signature, true)));
			pack.Definitions.Add(WithOverlays(bus, Fr("operator", "Opérateur", "vehicle", "Véhicule", "mileage", "Distance (km)", "ridership", "Achalandage")));

			var shift = Template("template.shift-summary", pack.Key, "General Shift/Activity Summary", "Operations", "Team/site, shift window, activities, outcomes and counts, safety observations, unresolved items and handoff.", RmsLifecyclePreset.QuickEntry, "SFT", "contact",
				Section("shift", "Shift", F("team", "Team / group", RmsFieldType.Group, false, true, true), F("site", "Site", RmsFieldType.Contact, false, true, true), F("shift_start", "Shift start", RmsFieldType.DateTime, true), F("shift_end", "Shift end", RmsFieldType.DateTime), F("lead", "Shift lead", RmsFieldType.Person, true, true, true)),
				Rows("activities", "Activities", null, 200, F("time", "Time", RmsFieldType.DateTime), F("activity", "Activity", RmsFieldType.ShortText, true, true), F("outcome", "Outcome", RmsFieldType.ShortText)),
				Section("counts", "Counts", Count("calls_handled", "Calls handled"), Count("tasks_completed", "Tasks completed"), Count("tasks_open", "Tasks open")),
				Section("safety", "Safety observations", F("safety_observations", "Safety observations", RmsFieldType.LongText), F("injury_reported", "Injury reported", RmsFieldType.Boolean, false, false, true)),
				Rows("unresolved", "Unresolved items", null, 50, F("item", "Item", RmsFieldType.ShortText, true), F("owner", "Owner", RmsFieldType.Person), F("due", "Due", RmsFieldType.Date)),
				Section("handoff", "Handoff", F("handoff_to", "Handoff to", RmsFieldType.Person), F("handoff_notes", "Handoff notes", RmsFieldType.LongText), F("acknowledgement", "Acknowledgement", RmsFieldType.Signature)));
			pack.Definitions.Add(WithOverlays(shift, Fr("team", "Équipe", "site", "Site", "shift_start", "Début du quart", "shift_end", "Fin du quart")));

			var job = Template("template.job-completion", pack.Key, "Job/Service Completion", "Field service", "Customer/site, job or work-order link, work performed, labor and duration, materials, exceptions, photos and customer acknowledgement.", RmsLifecyclePreset.ApprovalAcknowledgement, "JOB", "contact",
				Section("customer", "Customer / site", F("customer", "Customer", RmsFieldType.Contact, true, true, true), F("site_address", "Site address", RmsFieldType.Address, false, true), Ext("work_order", "Work order / job", "workorder", true)),
				Section("work", "Work performed", F("work_performed", "Work performed", RmsFieldType.LongText, true), F("completed_on", "Completed on", RmsFieldType.DateTime, true), Select("outcome", "Outcome", true, true, "Complete", "Partial", "Return visit required")),
				Rows("labor", "Labor", 1, 50, F("technician", "Technician", RmsFieldType.Person, true, true), F("hours", "Time on site", RmsFieldType.Duration, true)),
				Rows("materials", "Materials", null, 100, F("item", "Item", RmsFieldType.ShortText, true, true), Count("quantity", "Quantity", false), F("reference", "Reference", RmsFieldType.ShortText)),
				Section("exceptions", "Exceptions", F("exceptions", "Exceptions", RmsFieldType.LongText), F("photos", "Photos", RmsFieldType.Attachment)),
				Section("acknowledgement", "Customer acknowledgement", F("customer_name", "Customer representative", RmsFieldType.ShortText, true), F("acknowledgement", "Acknowledgement", RmsFieldType.Signature, true)));
			pack.Definitions.Add(WithOverlays(job, Fr("customer", "Client", "work_performed", "Travaux effectués", "completed_on", "Terminé le")));
			return pack;
		}

		// ---- RMS-1C operational packs -----------------------------------------------------------------

		private static RecordTemplatePack CertPack()
		{
			var pack = new RecordTemplatePack { Key = "pack.cert", Name = "CERT Operations Pack", Category = "Emergency management", IsPreview = true, Description = "Damage assessment, assignment tracking and activity log for Community Emergency Response Teams. Personnel check-in and equipment summaries compose from their owning modules.",
				Sources = { Source("CERT Basic Training", "FEMA CERT Basic Training participant manual", "FEMA", "2019", "https://www.fema.gov/emergency-managers/individuals-communities/preparedness-activities-webinars/community-emergency-response-team") } };
			var damage = Template("pack.cert.damage-assessment", pack.Key, "CERT Damage Assessment", "CERT", "Windshield/rapid damage assessment by team and area.", RmsLifecyclePreset.ReviewRequired, "CDA", "call,group",
				Section("assessment", "Assessment", F("team", "Team", RmsFieldType.Group, true, true, true), F("area", "Area / sector", RmsFieldType.ShortText, true, true, true), F("assessed_at", "Assessed at", RmsFieldType.DateTime, true), F("subdivision", "State / province", RmsFieldType.CountrySubdivision)),
				Rows("structures", "Structures", null, 300, F("address", "Address", RmsFieldType.Address, true), Select("damage", "Damage level", true, true, "None", "Affected", "Minor", "Major", "Destroyed"), Select("occupancy", "Occupancy", false, true, "Residential", "Commercial", "Public", "Other"), F("hazards", "Hazards", RmsFieldType.ShortText), F("photo", "Photo", RmsFieldType.Attachment)),
				Section("summary", "Summary", Count("structures_affected", "Structures affected"), Count("structures_destroyed", "Structures destroyed"), F("utilities_down", "Utilities down", RmsFieldType.Boolean, false, false, true), F("notes", "Notes", RmsFieldType.LongText)));
			Policy(damage, "facility-security", RmsFieldClassification.Restricted, "The address of a damaged structure identifies its residents; occupancy type stays a groupable code.", "address");
			pack.Definitions.Add(WithOverlays(damage, Fr("team", "Équipe", "area", "Secteur", "assessed_at", "Évalué le", "subdivision", "Province/territoire")));
			var assignment = Template("pack.cert.assignment-tracking", pack.Key, "CERT Assignment Tracking", "CERT", "Team assignments with briefing, status and completion.", RmsLifecyclePreset.QuickEntry, "CAT", "call,group",
				Section("briefing", "Team briefing", F("team", "Team", RmsFieldType.Group, true, true, true), F("leader", "Team leader", RmsFieldType.Person, true, true), F("briefed_at", "Briefed at", RmsFieldType.DateTime, true), F("objectives", "Objectives", RmsFieldType.LongText, true), F("safety_message", "Safety message", RmsFieldType.LongText)),
				Rows("assignments", "Assignments", 1, 100, F("assignment", "Assignment", RmsFieldType.ShortText, true, true), F("assigned_to", "Assigned to", RmsFieldType.Person), Select("status", "Status", true, true, "Assigned", "In progress", "Complete", "Cancelled"), F("completed_at", "Completed at", RmsFieldType.DateTime)),
				Section("demobilization", "Demobilization", F("released_at", "Team released at", RmsFieldType.DateTime), F("notes", "Notes", RmsFieldType.LongText)));
			pack.Definitions.Add(WithOverlays(assignment));
			var activity = Template("pack.cert.activity-log", pack.Key, "CERT Activity / Communications Log", "CERT", "Chronological activity and communications log (ICS 214 compatible).", RmsLifecyclePreset.QuickEntry, "CAL", "call,group",
				Section("header", "Log", F("team", "Team", RmsFieldType.Group, true, true, true), F("operational_period_start", "Operational period start", RmsFieldType.DateTime, true), F("operational_period_end", "Operational period end", RmsFieldType.DateTime)),
				Rows("entries", "Entries", 1, 500, F("time", "Time", RmsFieldType.DateTime, true), F("entry", "Notable activity", RmsFieldType.LongText, true), F("from_to", "From / to", RmsFieldType.ShortText)),
				Section("prepared", "Prepared by", F("prepared_by", "Prepared by", RmsFieldType.Person, true), F("signature", "Signature", RmsFieldType.Signature)));
			pack.Definitions.Add(WithOverlays(activity));
			return pack;
		}

		private static RecordTemplatePack SarPack()
		{
			var pack = new RecordTemplatePack { Key = "pack.sar", Name = "SAR Mission Pack", Category = "Search and rescue", Description = "Mission summary, segment debrief with coverage/POD, and clue reports. Subject and clue details are restricted.",
				Sources = { Source("NASAR SAR forms", "NASAR search and rescue mission documentation", "NASAR", "2024", "https://nasar.org/") } };
			var mission = Template("pack.sar.mission-summary", pack.Key, "SAR Mission Summary", "SAR", "Mission identity, subject profile (restricted), resources and outcome.", RmsLifecyclePreset.ReviewRequired, "SAR", "call,group",
				Section("mission", "Mission", Ext("mission_number", "Mission number", "sar-mission", true), F("incident_commander", "Incident commander", RmsFieldType.Person, true, true), F("started_at", "Started", RmsFieldType.DateTime, true), F("ended_at", "Ended", RmsFieldType.DateTime), F("base_location", "Base location", RmsFieldType.Address), F("subdivision", "State / province", RmsFieldType.CountrySubdivision)),
				Section("subject", "Subject profile", F("subject_name", "Subject name", RmsFieldType.ShortText, false, false, false, RmsFieldClassification.Restricted), F("subject_age", "Age", RmsFieldType.Integer, false, false, false, RmsFieldClassification.Restricted), F("subject_description", "Description", RmsFieldType.LongText, false, false, false, RmsFieldClassification.Restricted), F("medical_concerns", "Medical concerns", RmsFieldType.LongText, false, false, false, RmsFieldClassification.Restricted), F("point_last_seen", "Point last seen", RmsFieldType.Address, false, false, false, RmsFieldClassification.Restricted)),
				Section("resources", "Resources", Count("searchers", "Searchers"), Count("teams", "Teams"), F("k9", "K9 support", RmsFieldType.Boolean, false, false, true), F("uas", "UAS support", RmsFieldType.Boolean, false, false, true), Quantity("area_searched", "Area searched", "area", "ha")),
				Section("outcome", "Outcome", Select("outcome", "Outcome", true, true, "Found alive", "Found deceased", "Not found", "Suspended", "Stood down"), F("found_at", "Found at", RmsFieldType.DateTime), F("outcome_notes", "Notes", RmsFieldType.LongText)));
			Policy(mission, "subject-clue-recovery", RmsFieldClassification.Restricted, "The subject's identity and last-known position are disclosed only under the restricted grant.", "subject_name", "subject_age", "subject_description", "point_last_seen");
			Policy(mission, "treatment-casualty", RmsFieldClassification.Protected, "Medical concerns are health information; sealed under ADP where enrolled.", "medical_concerns");
			pack.Definitions.Add(WithOverlays(mission, Fr("mission_number", "Numéro de mission", "incident_commander", "Commandant d'intervention", "outcome", "Résultat", "area_searched", "Superficie fouillée"), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["area_searched"] = "ac" }, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["area_searched"] = "ha" }, "subject_name", "subject_age", "subject_description", "medical_concerns", "point_last_seen"));
			var debrief = Template("pack.sar.segment-debrief", pack.Key, "SAR Segment Debrief", "SAR", "Team briefing/debrief with coverage and probability of detection.", RmsLifecyclePreset.QuickEntry, "SEG", "call,group",
				Section("segment", "Segment", Ext("mission_number", "Mission number", "sar-mission", true), F("segment", "Segment", RmsFieldType.ShortText, true, true, true), F("team", "Team", RmsFieldType.Group, true, true), F("team_leader", "Team leader", RmsFieldType.Person, true)),
				Section("times", "Times", F("briefed_at", "Briefed", RmsFieldType.DateTime), F("departed_at", "Departed", RmsFieldType.DateTime), F("returned_at", "Returned", RmsFieldType.DateTime), F("time_searching", "Time searching", RmsFieldType.Duration)),
				Section("coverage", "Coverage", Select("search_type", "Search type", true, true, "Hasty", "Grid", "Sweep", "Track", "Containment"), Quantity("track_spacing", "Track spacing", "length", "m"), Count("pod_percent", "Probability of detection (%)"), F("coverage_notes", "Coverage notes", RmsFieldType.LongText)),
				Section("debrief", "Debrief", F("clues_found", "Clues found", RmsFieldType.Boolean, false, false, true), F("hazards", "Hazards encountered", RmsFieldType.LongText), F("recommendations", "Recommendations", RmsFieldType.LongText)));
			pack.Definitions.Add(WithOverlays(debrief, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["track_spacing"] = "ft" }, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["track_spacing"] = "m" }));
			var clue = Template("pack.sar.clue-report", pack.Key, "SAR Clue Report", "SAR", "A found clue, its location and disposition (restricted).", RmsLifecyclePreset.QuickEntry, "CLU", "call,group",
				Section("clue", "Clue", Ext("mission_number", "Mission number", "sar-mission", true), Count("clue_number", "Clue number", false), F("found_at", "Found at", RmsFieldType.DateTime, true), F("found_by", "Found by", RmsFieldType.Person, true), F("location", "Location", RmsFieldType.Address, true, false, false, RmsFieldClassification.Restricted), F("description", "Description", RmsFieldType.LongText, true, false, false, RmsFieldClassification.Restricted), F("photo", "Photo", RmsFieldType.Attachment)),
				Section("disposition", "Disposition", Select("disposition", "Disposition", true, false, "Left in place", "Collected", "Turned over to law enforcement", "Discounted"), F("notes", "Notes", RmsFieldType.LongText)));
			Policy(clue, "subject-clue-recovery", RmsFieldClassification.Restricted, "Clue location and description can identify a subject or a recovery site.", "location", "description", "found_by");
			pack.Definitions.Add(WithOverlays(clue, null, null, null, "location", "description"));
			return pack;
		}

		private static RecordTemplatePack DisasterPack()
		{
			var pack = new RecordTemplatePack { Key = "pack.disaster-assessment", Name = "Disaster Field Assessment and Mass Care Pack", Category = "Emergency management", Description = "Rapid needs / initial damage assessment with infrastructure impact and Community Lifeline status.",
				Sources = { Source("FEMA Community Lifelines", "FEMA Community Lifelines toolkit", "FEMA", "2023", "https://www.fema.gov/emergency-managers/practitioners/lifelines") } };
			var rapid = Template("pack.disaster.rapid-needs-assessment", pack.Key, "Rapid Needs / Initial Damage Assessment", "Disaster", "Field-team rapid needs and initial damage assessment for one area.", RmsLifecyclePreset.ReviewRequired, "RNA", "call,group",
				Section("area", "Area", F("team", "Team", RmsFieldType.Group, true, true, true), F("area", "Area / jurisdiction", RmsFieldType.ShortText, true, true, true), F("subdivision", "State / province", RmsFieldType.CountrySubdivision, true), F("assessed_at", "Assessed at", RmsFieldType.DateTime, true), Count("population_affected", "Population affected")),
				Section("lifelines", "Community Lifelines", Select("safety_security", "Safety and security", true, true, "Green", "Yellow", "Red", "Unknown"), Select("food_water_shelter", "Food, water, shelter", true, true, "Green", "Yellow", "Red", "Unknown"), Select("health_medical", "Health and medical", true, true, "Green", "Yellow", "Red", "Unknown"), Select("energy", "Energy", true, true, "Green", "Yellow", "Red", "Unknown"), Select("communications", "Communications", true, true, "Green", "Yellow", "Red", "Unknown"), Select("transportation", "Transportation", true, true, "Green", "Yellow", "Red", "Unknown"), Select("hazardous_materials", "Hazardous materials", true, true, "Green", "Yellow", "Red", "Unknown"), Select("water_systems", "Water systems", true, true, "Green", "Yellow", "Red", "Unknown")),
				Rows("routes", "Infrastructure / route impact", null, 100, F("route", "Route / facility", RmsFieldType.ShortText, true, true), Select("status", "Status", true, true, "Open", "Restricted", "Closed"), F("notes", "Notes", RmsFieldType.ShortText)),
				Section("needs", "Needs", Count("shelter_needed", "Shelter needed (persons)"), Quantity("water_needed", "Water needed", "volume", "L"), Count("meals_needed", "Meals needed (per day)"), F("medical_needs", "Medical needs", RmsFieldType.LongText), F("priority_needs", "Priority needs", RmsFieldType.LongText, true)),
				Section("summary", "Summary", Count("structures_affected", "Structures affected"), Count("structures_destroyed", "Structures destroyed"), Money("estimated_damage", "Estimated damage"), F("photos", "Photos", RmsFieldType.Attachment)));
			Policy(rapid, "treatment-casualty", RmsFieldClassification.Protected, "Medical needs are health information.", "medical_needs");
			pack.Definitions.Add(WithOverlays(rapid, Fr("team", "Équipe", "area", "Secteur", "subdivision", "Province/territoire", "priority_needs", "Besoins prioritaires", "water_needed", "Eau requise"), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["water_needed"] = "gal" }, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["water_needed"] = "L" }));
			return pack;
		}

		private static RecordTemplatePack EocPack()
		{
			var pack = new RecordTemplatePack { Key = "pack.eoc", Name = "EOC Coordination Pack", Category = "Emergency management", Description = "Duty-officer / EOC shift log and agency (ESF) status reporting.",
				Sources = { Source("NIMS EOC guidance", "FEMA NIMS Emergency Operations Center How-To Quick Reference Guide", "FEMA", "2020", "https://www.fema.gov/sites/default/files/2020-07/fema_nims_eoc-how-to-guide.pdf") } };
			var duty = Template("pack.eoc.duty-shift-log", pack.Key, "EOC Duty / Shift Log", "EOC", "Duty officer or EOC position log with significant events, decisions and handoff.", RmsLifecyclePreset.QuickEntry, "EOC", "call,group",
				Section("shift", "Shift", Select("activation_level", "Activation level", true, true, "Monitoring", "Partial", "Full"), F("position", "Position", RmsFieldType.ShortText, true, true, true), F("officer", "Officer", RmsFieldType.Person, true, true, true), F("shift_start", "Shift start", RmsFieldType.DateTime, true), F("shift_end", "Shift end", RmsFieldType.DateTime)),
				Rows("events", "Significant events / decisions", null, 500, F("time", "Time", RmsFieldType.DateTime, true), Select("kind", "Kind", true, true, "Event", "Decision", "Request", "Message"), F("entry", "Entry", RmsFieldType.LongText, true), F("action_owner", "Action owner", RmsFieldType.Person)),
				Rows("open_actions", "Open action tracker", null, 100, F("action", "Action", RmsFieldType.ShortText, true), F("owner", "Owner", RmsFieldType.Person), F("due", "Due", RmsFieldType.DateTime), Select("status", "Status", true, true, "Open", "In progress", "Closed")),
				Section("handoff", "Handoff", F("handoff_to", "Handoff to", RmsFieldType.Person), F("handoff_summary", "Handoff summary", RmsFieldType.LongText), F("signature", "Signature", RmsFieldType.Signature)));
			pack.Definitions.Add(WithOverlays(duty, Fr("activation_level", "Niveau d'activation", "position", "Poste", "officer", "Officier")));
			var agency = Template("pack.eoc.agency-status", pack.Key, "Agency / ESF Status Report", "EOC", "Periodic agency or emergency support function status for a situation report.", RmsLifecyclePreset.ReviewRequired, "ESF", "group",
				Section("report", "Report", F("agency", "Agency / ESF", RmsFieldType.ShortText, true, true, true), F("reporting_period_start", "Period start", RmsFieldType.DateTime, true), F("reporting_period_end", "Period end", RmsFieldType.DateTime, true), F("liaison", "Liaison", RmsFieldType.Person, true), Select("status", "Overall status", true, true, "Normal", "Stressed", "Degraded", "Failed")),
				Section("situation", "Situation", F("current_situation", "Current situation", RmsFieldType.LongText, true), F("actions_taken", "Actions taken", RmsFieldType.LongText), F("planned_actions", "Planned actions", RmsFieldType.LongText), F("unmet_needs", "Unmet needs", RmsFieldType.LongText)),
				Rows("resource_requests", "Resource requests", null, 50, F("resource", "Resource", RmsFieldType.ShortText, true), Count("quantity", "Quantity", false), F("needed_by", "Needed by", RmsFieldType.DateTime), Ext("request_number", "Request number", "resource-request")));
			pack.Definitions.Add(WithOverlays(agency));
			return pack;
		}

		private static RecordTemplatePack HazmatPack()
		{
			var pack = new RecordTemplatePack { Key = "pack.hazmat", Name = "HAZMAT Response Pack", Category = "Hazardous materials", Description = "Non-NERIS spill/release response: size-up, product, monitoring, decontamination, notifications and critique. Exposure details are restricted.",
				Sources = { Source("EPA release reporting", "EPA emergency release notification requirements (CERCLA/EPCRA)", "EPA", "2024", "https://www.epa.gov/epcra"), Source("ICS 208 HM", "ICS 208 HM Site Safety and Control Plan", "FEMA", "2023", "https://training.fema.gov/icsresource/icsforms.aspx") } };
			var release = Template("pack.hazmat.release-response", pack.Key, "HAZMAT Release / Response", "HAZMAT", "A hazardous-materials release and the response to it, outside the NERIS incident record.", RmsLifecyclePreset.ApprovalAcknowledgement, "HZM", "call,contact",
				Section("sizeup", "Initial size-up", F("call", "Related call", RmsFieldType.CallReference, false, true, true), F("location", "Location", RmsFieldType.Address, true, true), F("discovered_at", "Discovered at", RmsFieldType.DateTime, true), F("facility", "Facility / responsible party", RmsFieldType.Contact, false, true, true), Select("release_type", "Release type", true, true, "Spill", "Leak", "Vapor", "Fire", "Explosion", "Unknown")),
				Section("product", "Product / container", F("product_name", "Product name", RmsFieldType.ShortText, true, true, true), Ext("un_number", "UN/NA number", "un"), F("container_type", "Container type", RmsFieldType.ShortText), Quantity("quantity_released", "Quantity released", "volume", "L"), Quantity("area_affected", "Area affected", "area", "ha")),
				Rows("monitoring", "Atmospheric monitoring", null, 200, F("time", "Time", RmsFieldType.DateTime, true), F("monitoring_location", "Location", RmsFieldType.ShortText, true), F("instrument", "Instrument", RmsFieldType.ShortText), F("reading", "Reading", RmsFieldType.ShortText, true), Select("zone", "Zone", false, true, "Hot", "Warm", "Cold")),
				Rows("entries", "Entry control", null, 50, F("entrant", "Entrant", RmsFieldType.Person, true), F("entry_at", "Entry", RmsFieldType.DateTime, true), F("exit_at", "Exit", RmsFieldType.DateTime), Select("ppe_level", "PPE level", true, true, "A", "B", "C", "D"), F("air_start_psi", "Air at entry", RmsFieldType.Integer)),
				Section("decon", "Decontamination", Select("decon_type", "Decontamination type", false, true, "Emergency", "Technical", "Mass", "None"), Count("persons_deconned", "Persons decontaminated"), F("decon_notes", "Notes", RmsFieldType.LongText)),
				Section("exposure", "Responder exposure", F("exposure_reported", "Exposure reported", RmsFieldType.Boolean, false, false, true), F("exposure_details", "Exposure details", RmsFieldType.LongText, false, false, false, RmsFieldClassification.Restricted)),
				Rows("notifications", "Notification log", null, 50, F("agency", "Agency", RmsFieldType.ShortText, true), F("notified_at", "Notified at", RmsFieldType.DateTime, true), Ext("reference", "Reference", "notification"), F("contact", "Contact", RmsFieldType.ShortText)),
				Section("critique", "Critique", F("critique", "Critique", RmsFieldType.LongText), F("approved_by", "Approved by", RmsFieldType.Person), F("signature", "Signature", RmsFieldType.Signature)));
			release.Schema.FindField("exposure_details").Rules.Add(ShowWhen("exposure_reported", "true"));
			Policy(release, "exposure-health", RmsFieldClassification.Protected, "Exposure details are health information.", "exposure_details");
			Policy(release, "exposure-health", RmsFieldClassification.Restricted, "Entrants are identifiable persons; decontamination counts stay Workflow-exposed.", "entrant");
			Policy(release, "facility-security", RmsFieldClassification.Restricted, "A notification contact identifies a person at the site; the facility party and reference numbers stay searchable.", "contact");
			pack.Definitions.Add(WithOverlays(release, Fr("product_name", "Nom du produit", "release_type", "Type de déversement", "quantity_released", "Quantité déversée"), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["quantity_released"] = "gal", ["area_affected"] = "ac" }, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["quantity_released"] = "L", ["area_affected"] = "ha" }, "exposure_details"));
			return pack;
		}

		private static RecordTemplatePack IndustrialPack()
		{
			var pack = new RecordTemplatePack { Key = "pack.industrial", Name = "Industrial Operations and Process Safety Pack", Category = "Industrial", Description = "Operator/control-room shift handover and incident/near-miss reporting with process-safety and regulatory fields.",
				Sources = { Source("OSHA 29 CFR 1910.119", "Process Safety Management of Highly Hazardous Chemicals", "OSHA", "2024", "https://www.osha.gov/process-safety-management") } };
			var handover = Template("pack.industrial.shift-handover", pack.Key, "Operator / Control-Room Shift Handover", "Industrial", "Unit status, abnormal conditions, permits and outstanding items at shift change.", RmsLifecyclePreset.QuickEntry, "SHO", "unit,group",
				Section("shift", "Shift", F("unit_area", "Unit / area", RmsFieldType.ShortText, true, true, true), F("outgoing", "Outgoing operator", RmsFieldType.Person, true, true), F("incoming", "Incoming operator", RmsFieldType.Person, true, true), F("handover_at", "Handover at", RmsFieldType.DateTime, true)),
				Rows("status", "Equipment / unit status", null, 100, F("equipment", "Equipment", RmsFieldType.ShortText, true, true), Select("status", "Status", true, true, "Normal", "Abnormal", "Down", "Maintenance"), F("notes", "Notes", RmsFieldType.ShortText)),
				Rows("permits", "Active permits / isolations", null, 100, Ext("permit", "Permit", "permit"), Select("type", "Type", true, true, "Hot work", "Confined space", "Isolation", "Excavation", "Other"), F("expires", "Expires", RmsFieldType.DateTime)),
				Section("readings", "Key readings", Quantity("temperature", "Temperature", "temperature", "C"), Quantity("throughput", "Throughput", "volume", "L"), F("abnormal_conditions", "Abnormal conditions", RmsFieldType.LongText)),
				Section("handoff", "Handoff", F("outstanding", "Outstanding items", RmsFieldType.LongText), F("incoming_acknowledgement", "Incoming acknowledgement", RmsFieldType.Signature, true)));
			pack.Definitions.Add(WithOverlays(handover, Fr("unit_area", "Unité / zone", "handover_at", "Relève à", "temperature", "Température"), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["temperature"] = "F", ["throughput"] = "gal" }, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["temperature"] = "C", ["throughput"] = "L" }));
			var nearMiss = Template("pack.industrial.incident-near-miss", pack.Key, "Incident / Near Miss", "Industrial", "Incident, near miss or unsafe condition with classification, causes, actions and regulatory notification.", RmsLifecyclePreset.ApprovalAcknowledgement, "INM", "unit,group,contact",
				Section("event", "Event", Select("event_type", "Event type", true, true, "Injury", "Near miss", "Unsafe condition", "Process upset", "Environmental release", "Property damage"), F("occurred_at", "Occurred at", RmsFieldType.DateTime, true), F("location", "Location", RmsFieldType.Address, true), F("unit_area", "Unit / area", RmsFieldType.ShortText, true, true, true), F("reported_by", "Reported by", RmsFieldType.Person, true), F("contractor", "Contractor involved", RmsFieldType.Contact, false, true)),
				Section("description", "Description", F("description", "Description", RmsFieldType.LongText, true), Select("potential_severity", "Potential severity", true, true, "Low", "Medium", "High", "Catastrophic"), F("injury", "Injury occurred", RmsFieldType.Boolean, false, false, true), F("injury_details", "Injury details", RmsFieldType.LongText, false, false, false, RmsFieldClassification.Restricted)),
				Rows("causes", "Causes", null, 20, Select("category", "Category", true, true, "Procedure", "Equipment", "Human factors", "Environment", "Management system"), F("cause", "Cause", RmsFieldType.ShortText, true)),
				Rows("actions", "Corrective actions", null, 50, F("action", "Action", RmsFieldType.ShortText, true), F("owner", "Owner", RmsFieldType.Person), F("due", "Due", RmsFieldType.Date), F("work_order", "Work order", RmsFieldType.ChecklistWorkOrderReference)),
				Section("regulatory", "Regulatory", F("reportable", "Regulator-reportable", RmsFieldType.Boolean, false, false, true), Ext("regulator_reference", "Regulator reference", "regulator"), Money("estimated_cost", "Estimated cost")),
				Section("approval", "Approval", F("investigated_by", "Investigated by", RmsFieldType.Person), F("approver_signature", "Approver signature", RmsFieldType.Signature, true)));
			nearMiss.Schema.FindField("injury_details").Rules.Add(ShowWhen("injury", "true"));
			nearMiss.Schema.FindField("regulator_reference").Rules.Add(ShowWhen("reportable", "true"));
			foreach (var field in nearMiss.Schema.FindSection("actions").Fields.Where(f => f.Key == "work_order")) field.ReferenceType = "workorder";
			Policy(nearMiss, "exposure-health", RmsFieldClassification.Protected, "Injury details are health information.", "injury_details");
			Policy(nearMiss, "subject-clue-recovery", RmsFieldClassification.Restricted, "The reporter is an identifiable person; the contractor party stays searchable.", "reported_by");
			pack.Definitions.Add(WithOverlays(nearMiss, Fr("event_type", "Type d'événement", "occurred_at", "Survenu le", "description", "Description"), null, null, "injury_details"));
			return pack;
		}

		private static RecordTemplatePack ExercisePack()
		{
			var pack = new RecordTemplatePack { Key = "pack.exercise", Name = "Exercise, Drill and AAR/IP Pack", Category = "Preparedness", Description = "Exercise/drill record, evaluator observations, hotwash and after-action report with improvement plan linked to Work Orders, Checklists and training.",
				Sources = { Source("HSEEP", "Homeland Security Exercise and Evaluation Program", "FEMA", "2020", "https://www.fema.gov/emergency-managers/national-preparedness/exercises/hseep") } };
			var aar = Template("pack.exercise.aar-improvement-plan", pack.Key, "After-Action Report / Improvement Plan", "Exercise", "Exercise record, observations, strengths and areas for improvement, and the corrective-action plan.", RmsLifecyclePreset.ApprovalAcknowledgement, "AAR", "group",
				Section("exercise", "Exercise", F("exercise_name", "Exercise name", RmsFieldType.ShortText, true, true, true), Select("exercise_type", "Type", true, true, "Drill", "Tabletop", "Functional", "Full-scale", "Real-world"), F("conducted_on", "Conducted on", RmsFieldType.Date, true), F("lead_evaluator", "Lead evaluator", RmsFieldType.Person, true), Multi("capabilities", "Core capabilities", "Planning", "Operational communications", "Situational assessment", "On-scene security", "Mass care", "Public health", "Fire management", "Search and rescue")),
				Rows("observations", "Evaluator observations", null, 200, F("evaluator", "Evaluator", RmsFieldType.Person), F("capability", "Capability", RmsFieldType.ShortText, true), Select("rating", "Rating", true, true, "Performed without challenges", "Performed with some challenges", "Performed with major challenges", "Unable to be performed"), F("observation", "Observation", RmsFieldType.LongText, true)),
				Section("hotwash", "Hotwash", F("strengths", "Strengths", RmsFieldType.LongText), F("areas_for_improvement", "Areas for improvement", RmsFieldType.LongText, true)),
				Rows("corrective_actions", "Corrective actions", null, 100, F("action", "Corrective action", RmsFieldType.ShortText, true), F("owner", "Owner", RmsFieldType.Person), F("due", "Due", RmsFieldType.Date), F("tracked_in", "Tracked in", RmsFieldType.ChecklistWorkOrderReference), Select("status", "Status", true, true, "Open", "In progress", "Complete")),
				Section("approval", "Approval", F("approved_by", "Approved by", RmsFieldType.Person), F("approval_signature", "Approval signature", RmsFieldType.Signature, true)));
			pack.Definitions.Add(WithOverlays(aar, Fr("exercise_name", "Nom de l'exercice", "exercise_type", "Type", "conducted_on", "Tenu le", "areas_for_improvement", "Points à améliorer")));
			return pack;
		}

		private static RecordTemplatePack MutualAidPack()
		{
			var pack = new RecordTemplatePack { Key = "pack.mutual-aid", Name = "Mutual Aid and Deployment Pack", Category = "Mutual aid", IsPreview = true, Description = "Create Deployment from External Order: the supplied resources, mobilization, daily activity, release and return-to-home-unit closeout. Preview: no claim of NWCG, CIFFC or member-agency acceptance until a real order has been filled and reconciled.",
				SupportedProfiles = new List<string> { "generic", "us", "ca", "us-ca" },
				Sources = { Source("NIFC mobilization guide", "NIFC mobilization and resource-order guidance", "NIFC", "2026", "https://www.nifc.gov/nicc/logistics/reference-documents"), Source("CIFFC MARS", "Mutual Aid and Resource Sharing agreement and guidelines", "CIFFC", "2025", "https://dev.ciffc.ca/download/mutual-aid-and-resource-sharing/"), Source("International Mobilization Guide", "NIFC International Mobilization Guide", "NIFC", "2026", "https://www.nifc.gov/sites/default/files/NICC/3-Logistics/Reference%20Documents/2026_International_Mobilization_Guide_FINAL.pdf") } };
			var deployment = Template("pack.mutual-aid.deployment", pack.Key, "Deployment (External Order)", "Mutual aid", "One deployment filling one or more external requests: order facts, mobilization briefing, roster/manifest, daily activity, release and closeout.", RmsLifecyclePreset.ApprovalAcknowledgement, "DEP", "call,unit,group",
				Section("order", "External order",
					Select("profile", "Ordering profile", true, true, "Generic", "US wildland", "CA wildland", "US-CA cross-border", "EMAC compact", "Local mutual aid"), Ext("order_number", "Order number", "order", true), Ext("incident_number", "Incident number", "incident"),
					F("incident_name", "Incident name", RmsFieldType.ShortText, true, true, true), F("incident_subdivision", "Incident state / province", RmsFieldType.CountrySubdivision), F("ordering_office", "Ordering office", RmsFieldType.ShortText, false, true, true), F("requesting_agency", "Requesting agency", RmsFieldType.ShortText, false, true, true), F("sending_agency", "Sending agency", RmsFieldType.ShortText, false, true), Ext("agreement", "Agreement / contract", "agreement"), Ext("cost_code", "Cost / fire / project code", "cost-code")),
				Section("mobilization", "Mobilization briefing", F("coordinator", "Coordinator", RmsFieldType.Person, true, true, true), F("briefed_at", "Briefed at", RmsFieldType.DateTime), F("etd", "Estimated departure", RmsFieldType.DateTime), F("eta", "Estimated arrival", RmsFieldType.DateTime), F("travel_instructions", "Travel instructions", RmsFieldType.LongText), F("point_of_hire", "Point of hire", RmsFieldType.Address)),
				Rows("roster", "Roster / manifest", null, 200, F("member", "Member", RmsFieldType.Person, true), F("position", "Position", RmsFieldType.ShortText, true, true), F("trainee", "Trainee", RmsFieldType.Boolean), Ext("request_number", "Request number", "request", true), F("unit", "Unit / equipment", RmsFieldType.Unit)),
				Rows("activity", "Daily activity and resource status", null, 200, F("date", "Date", RmsFieldType.Date, true), Select("status", "Resource status", true, true, "Mobilizing", "Assigned", "Available", "Out of service", "Released", "Returning"), F("summary", "Summary", RmsFieldType.LongText), Quantity("hours", "Hours worked", "time", "h")),
				Section("evidence", "Time, equipment and expense evidence", Ext("time_evidence", "Personnel time evidence", "dtr"), Ext("equipment_evidence", "Equipment time evidence", "equipment-time"), Money("expenses", "Expenses"), F("receipts", "Receipts", RmsFieldType.Attachment)),
				Section("release", "Release / demobilization", F("released_at", "Released", RmsFieldType.DateTime), Ext("release_order", "Release order", "release"), F("return_travel", "Return travel", RmsFieldType.LongText), F("equipment_reconciled", "Equipment / property reconciled", RmsFieldType.Boolean, false, false, true), F("returned_at", "Actual return to home unit", RmsFieldType.DateTime), F("outstanding_finance", "Outstanding finance / documents", RmsFieldType.LongText)),
				Section("closeout", "Closeout", F("performance_notes", "Performance / AAR notes", RmsFieldType.LongText), F("closeout_approver", "Closeout approver", RmsFieldType.Person), F("closeout_signature", "Closeout signature", RmsFieldType.Signature, true)));
			Policy(deployment, "manifest-travel", RmsFieldClassification.Restricted, "Travel instructions, point of hire and receipts are minimum-necessary for finance; expense totals stay aggregatable.", "travel_instructions", "point_of_hire", "receipts");
			pack.Definitions.Add(WithOverlays(deployment,
				Fr("order_number", "Numéro de commande", "incident_number", "Numéro d'incident", "incident_name", "Nom de l'incident", "incident_subdivision", "Province/territoire de l'incident", "ordering_office", "Bureau de commande", "requesting_agency", "Agence demanderesse", "sending_agency", "Agence expéditrice", "coordinator", "Coordonnateur", "roster", "Liste / manifeste", "released_at", "Libéré le", "returned_at", "Retour réel à l'unité d'attache", "expenses", "Dépenses"),
				new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["hours"] = "h" }, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["hours"] = "h" }));
			return pack;
		}
	}
}
