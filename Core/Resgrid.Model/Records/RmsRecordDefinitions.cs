using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Who owns a Record definition (RMS plan section 4.1): locked product content or a department clone/draft.</summary>
	public enum RmsDefinitionOwner
	{
		System = 1,
		Department = 2
	}

	/// <summary>Definition version lifecycle: Draft -> Published -> Retired. Only an unused draft can be deleted.</summary>
	public enum RmsDefinitionVersionState
	{
		Draft = 1,
		Published = 2,
		Retired = 3
	}

	/// <summary>
	/// The controlled field catalog (RMS plan section 4.1). 1-18 ship in RMS-1B (the launch templates need them);
	/// 19-24 are the RMS-1C additions the operational packs need. Values are stable and pinned by the client contract.
	/// </summary>
	public enum RmsFieldType
	{
		ShortText = 1,
		LongText = 2,
		Integer = 3,
		Decimal = 4,
		Boolean = 5,
		Date = 6,
		DateTime = 7,
		Duration = 8,
		SingleSelect = 9,
		MultiSelect = 10,
		Address = 11,
		Person = 12,
		Unit = 13,
		Group = 14,
		Contact = 15,
		Attachment = 16,
		Signature = 17,
		ExternalReference = 18,
		// RMS-1C
		Currency = 19,
		Quantity = 20,
		CountrySubdivision = 21,
		CallReference = 22,
		InventoryReference = 23,
		ChecklistWorkOrderReference = 24
	}

	/// <summary>Field classification (RMS plan section 5.9.2). Restricted gates on RecordRestricted_View; Protected is sealed under ADP once cataloged.</summary>
	public enum RmsFieldClassification
	{
		Standard = 0,
		Restricted = 1,
		Protected = 2
	}

	/// <summary>Bounded rule operators (RMS plan section 4.1). Nothing here produces a value; rules only control visibility and requiredness.</summary>
	public enum RmsRuleOperator
	{
		Equals = 1,
		NotEquals = 2,
		InSet = 3,
		NotInSet = 4,
		IsEmpty = 5,
		IsNotEmpty = 6,
		InRange = 7,
		And = 20,
		Or = 21
	}

	public enum RmsRuleEffect
	{
		/// <summary>The section/field is shown only while the condition holds.</summary>
		Show = 1,
		/// <summary>The field is required (at finalize) while the condition holds.</summary>
		Require = 2
	}

	/// <summary>Stable definition identity (RMS plan section 5.2, registry M0158). One row per department definition key.</summary>
	public class RmsRecordDefinition : IEntity
	{
		public string RmsRecordDefinitionId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		/// <summary>Stable key; department keys never use the reserved "system." prefix.</summary>
		public string DefinitionKey { get; set; }
		public int Owner { get; set; }
		public string Name { get; set; }
		public string Category { get; set; }
		public string Description { get; set; }
		/// <summary>Product template this definition was cloned from, when any (template lineage never mutates the clone).</summary>
		public string TemplateKey { get; set; }
		public int? TemplatePackVersion { get; set; }
		/// <summary>Locked jurisdiction overlay applied at clone time (RMS-1C); null for the generic base.</summary>
		public string JurisdictionProfileKey { get; set; }
		/// <summary>Comma-separated subject/reference types a Record on this definition may link (call, unit, contact, person, checklist, workorder).</summary>
		public string PermittedSubjectTypes { get; set; }
		/// <summary>The version new Records start on; null until first publish.</summary>
		public int? CurrentPublishedVersion { get; set; }
		public int LatestVersion { get; set; }
		public bool IsRetired { get; set; }
		public DateTime? RetiredOn { get; set; }
		public string RetiredByUserId { get; set; }
		public string RetiredReason { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime ModifiedOn { get; set; }
		public string ModifiedByUserId { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsRecordDefinitionId; }
			set { RmsRecordDefinitionId = value?.ToString(); }
		}

		[NotMapped] public string TableName => "RmsRecordDefinitions";
		[NotMapped] public string IdName => "RmsRecordDefinitionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// One immutable-once-published schema version (RMS plan section 5.2). The authored document lives in SchemaJson;
	/// publishing freezes it, computes its checksum and capability floor, and materializes the section/field rows.
	/// </summary>
	public class RmsRecordDefinitionVersion : IEntity
	{
		public string RmsRecordDefinitionVersionId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsRecordDefinitionId { get; set; }
		public string DefinitionKey { get; set; }
		public int Version { get; set; }
		public int State { get; set; }
		public int LifecyclePreset { get; set; }
		/// <summary>
		/// <see cref="RmsRecordCardinality"/> — how many Records of this definition may exist on one Call
		/// (plan section 5.2.1). Immutable once published, like everything else on a version: a Record created
		/// under the old rule is never retrospectively a duplicate.
		/// </summary>
		public int Cardinality { get; set; } = (int)RmsRecordCardinality.MultiplePerCall;
		/// <summary>Comma-separated PersonnelRole ids that narrow Record_Review for this definition; empty = anyone holding it.</summary>
		public string ReviewerRoleIds { get; set; }
		/// <summary>Comma-separated PersonnelRole ids that narrow Record_Approve; empty = anyone holding it.</summary>
		public string ApproverRoleIds { get; set; }
		public int? ReviewDueHours { get; set; }
		public int? ApproveDueHours { get; set; }
		public bool RequireAuthorAttestation { get; set; }
		/// <summary>Serialized <see cref="RecordDefinitionNumbering"/>.</summary>
		public string NumberingJson { get; set; }
		/// <summary>Retention years for Records on this version; null = class default; 0 = permanent.</summary>
		public int? RetentionYears { get; set; }
		/// <summary>Whole-definition classification floor (RmsFieldClassification); a field can be stricter, never looser.</summary>
		public int Classification { get; set; }
		/// <summary>Serialized <see cref="RecordDefinitionSchema"/>.</summary>
		public string SchemaJson { get; set; }
		public string SchemaChecksum { get; set; }
		/// <summary>Derived at publish from the field and rule types actually used (RMS plan section 5.4).</summary>
		public string MinimumClientCapability { get; set; }
		/// <summary>Serialized <see cref="RecordDefinitionClientSurface"/>: which field apps may author on this version.</summary>
		public string ClientSurfaceJson { get; set; }
		/// <summary>Explicit field mapping from the previous published version (RecordDefinitionFieldMapping list), for draft migration.</summary>
		public string MigrationMapJson { get; set; }
		public string ChangeNotes { get; set; }
		public DateTime? PublishedOn { get; set; }
		public string PublishedByUserId { get; set; }
		public DateTime? RetiredOn { get; set; }
		public string RetiredByUserId { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime ModifiedOn { get; set; }
		public string ModifiedByUserId { get; set; }
		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsRecordDefinitionVersionId; }
			set { RmsRecordDefinitionVersionId = value?.ToString(); }
		}

		[NotMapped] public string TableName => "RmsRecordDefinitionVersions";
		[NotMapped] public string IdName => "RmsRecordDefinitionVersionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "Schema", "Numbering", "ClientSurface" };

		private RecordDefinitionSchema _schema;
		[NotMapped]
		[JsonIgnore]
		public RecordDefinitionSchema Schema
		{
			get { return _schema ??= RecordDefinitionSchema.Parse(SchemaJson); }
			set { _schema = value; SchemaJson = value == null ? null : RecordDefinitionSchema.Serialize(value); }
		}

		[NotMapped]
		[JsonIgnore]
		public RecordDefinitionNumbering Numbering
		{
			get { return RecordDefinitionNumbering.Parse(NumberingJson); }
			set { NumberingJson = value == null ? null : JsonConvert.SerializeObject(value); }
		}

		[NotMapped]
		[JsonIgnore]
		public RecordDefinitionClientSurface ClientSurface
		{
			get { return RecordDefinitionClientSurface.Parse(ClientSurfaceJson); }
			set { ClientSurfaceJson = value == null ? null : JsonConvert.SerializeObject(value); }
		}

		public bool IsPublished => State == (int)RmsDefinitionVersionState.Published;
		public bool IsDraft => State == (int)RmsDefinitionVersionState.Draft;
	}

	/// <summary>Materialized at publish (RMS plan section 5.2): ordered section identity for one published version.</summary>
	public class RmsRecordSectionDefinition : IEntity
	{
		public string RmsRecordSectionDefinitionId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsRecordDefinitionVersionId { get; set; }
		public string DefinitionKey { get; set; }
		public int DefinitionVersion { get; set; }
		public string SectionKey { get; set; }
		public string Label { get; set; }
		public string Help { get; set; }
		public int Ordinal { get; set; }
		public bool IsRepeating { get; set; }
		public int? MinRows { get; set; }
		public int? MaxRows { get; set; }
		public string RulesJson { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsRecordSectionDefinitionId; }
			set { RmsRecordSectionDefinitionId = value?.ToString(); }
		}

		[NotMapped] public string TableName => "RmsRecordSectionDefinitions";
		[NotMapped] public string IdName => "RmsRecordSectionDefinitionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Materialized at publish: stable field identity, type, classification and capability flags for one published version.</summary>
	public class RmsRecordFieldDefinition : IEntity
	{
		public string RmsRecordFieldDefinitionId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsRecordDefinitionVersionId { get; set; }
		public string DefinitionKey { get; set; }
		public int DefinitionVersion { get; set; }
		public string SectionKey { get; set; }
		public string FieldKey { get; set; }
		public string Label { get; set; }
		public int DataType { get; set; }
		public int Ordinal { get; set; }
		public bool Required { get; set; }
		public bool RequiredToFinalize { get; set; }
		public int Classification { get; set; }
		public string ReferenceType { get; set; }
		public bool Searchable { get; set; }
		public bool Filterable { get; set; }
		public bool Sortable { get; set; }
		public bool Groupable { get; set; }
		public bool Aggregatable { get; set; }
		public bool WorkflowExposed { get; set; }
		public bool Exportable { get; set; }
		public string ConstraintsJson { get; set; }
		public string RulesJson { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsRecordFieldDefinitionId; }
			set { RmsRecordFieldDefinitionId = value?.ToString(); }
		}

		[NotMapped] public string TableName => "RmsRecordFieldDefinitions";
		[NotMapped] public string IdName => "RmsRecordFieldDefinitionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	// ------------------------------------------------------------------------------------------------------
	// Authored schema document (what the designer edits and what a published version freezes)
	// ------------------------------------------------------------------------------------------------------

	public class RecordDefinitionSchema
	{
		public const int CurrentSchemaVersion = 1;
		public const int MaxSections = 40;
		public const int MaxFieldsPerSection = 60;
		public const int MaxOptions = 200;
		public const int MaxRuleDepth = 6;

		public int SchemaVersion { get; set; } = CurrentSchemaVersion;
		public List<RecordSectionSchema> Sections { get; set; } = new List<RecordSectionSchema>();

		private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.None };

		public static RecordDefinitionSchema Parse(string json) => string.IsNullOrWhiteSpace(json) ? new RecordDefinitionSchema() : JsonConvert.DeserializeObject<RecordDefinitionSchema>(json, Settings) ?? new RecordDefinitionSchema();
		public static string Serialize(RecordDefinitionSchema schema) => JsonConvert.SerializeObject(schema ?? new RecordDefinitionSchema(), Settings);

		/// <summary>Canonical form: sections and fields ordered, keys trimmed and lower-cased. Checksums are taken over this.</summary>
		public string Canonical()
		{
			var clone = JsonConvert.DeserializeObject<RecordDefinitionSchema>(Serialize(this), Settings);
			foreach (var section in clone.Sections)
			{
				section.Key = RecordDefinitionKeys.NormalizeKey(section.Key);
				foreach (var field in section.Fields)
					field.Key = RecordDefinitionKeys.NormalizeKey(field.Key);
			}
			return JsonConvert.SerializeObject(clone, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.None, ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver { NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy() } });
		}

		public IEnumerable<RecordFieldSchema> AllFields() => Sections.SelectMany(s => s.Fields);

		public RecordFieldSchema FindField(string fieldKey) => AllFields().FirstOrDefault(f => string.Equals(f.Key, fieldKey, StringComparison.OrdinalIgnoreCase));

		public RecordSectionSchema FindSection(string sectionKey) => Sections.FirstOrDefault(s => string.Equals(s.Key, sectionKey, StringComparison.OrdinalIgnoreCase));

		public RecordSectionSchema SectionOf(string fieldKey) => Sections.FirstOrDefault(s => s.Fields.Any(f => string.Equals(f.Key, fieldKey, StringComparison.OrdinalIgnoreCase)));
	}

	public class RecordSectionSchema
	{
		public string Key { get; set; }
		public string Label { get; set; }
		public string Help { get; set; }
		public bool Repeating { get; set; }
		public int? MinRows { get; set; }
		public int? MaxRows { get; set; }
		/// <summary>Visibility rules (Show) over fields of the same version.</summary>
		public List<RecordRuleSchema> Rules { get; set; } = new List<RecordRuleSchema>();
		public List<RecordFieldSchema> Fields { get; set; } = new List<RecordFieldSchema>();
	}

	public class RecordFieldSchema
	{
		public string Key { get; set; }
		public string Label { get; set; }
		public string Help { get; set; }
		public RmsFieldType Type { get; set; }
		/// <summary>Required whenever visible (checked at finalize; drafts may be incomplete).</summary>
		public bool Required { get; set; }
		public bool RequiredToFinalize { get; set; }
		public RmsFieldClassification Classification { get; set; }
		public List<RecordOptionSchema> Options { get; set; } = new List<RecordOptionSchema>();
		public decimal? Min { get; set; }
		public decimal? Max { get; set; }
		public int? MaxLength { get; set; }
		/// <summary>Reference/attachment target (user, unit, group, contact, attachment, call, inventory-item, checklist, workorder) or the external scheme.</summary>
		public string ReferenceType { get; set; }
		/// <summary>Quantity fields: the unit family (length, volume, area, temperature, mass) that bounds the accepted units.</summary>
		public string UnitFamily { get; set; }
		/// <summary>Quantity fields: the unit the value is canonicalized to for comparison; Decimal fields: a display-only unit label.</summary>
		public string DefaultUnit { get; set; }
		public string FixedUnitLabel { get; set; }
		public string DefaultCurrency { get; set; }
		public bool Searchable { get; set; }
		public bool Filterable { get; set; }
		public bool Sortable { get; set; }
		public bool Groupable { get; set; }
		public bool Aggregatable { get; set; }
		public bool WorkflowExposed { get; set; }
		public bool Exportable { get; set; } = true;
		public List<RecordRuleSchema> Rules { get; set; } = new List<RecordRuleSchema>();
	}

	public class RecordOptionSchema
	{
		public string Key { get; set; }
		public string Label { get; set; }
		/// <summary>Locale overrides supplied by a jurisdiction overlay (RMS-1C): "fr-CA" -> label.</summary>
		public Dictionary<string, string> Labels { get; set; }
	}

	public class RecordRuleSchema
	{
		public RmsRuleEffect Effect { get; set; }
		public RecordConditionSchema Condition { get; set; }
	}

	public class RecordConditionSchema
	{
		public RmsRuleOperator Operator { get; set; }
		public string FieldKey { get; set; }
		public string Value { get; set; }
		public List<string> Values { get; set; }
		public decimal? Min { get; set; }
		public decimal? Max { get; set; }
		public DateTime? MinDate { get; set; }
		public DateTime? MaxDate { get; set; }
		public List<RecordConditionSchema> Conditions { get; set; }

		public IEnumerable<string> ReferencedFieldKeys()
		{
			if (!string.IsNullOrWhiteSpace(FieldKey))
				yield return FieldKey;
			foreach (var child in Conditions ?? new List<RecordConditionSchema>())
				foreach (var key in child.ReferencedFieldKeys())
					yield return key;
		}
	}

	/// <summary>Definition-specific numbering (RMS plan section 4.1 "Numbering"). Server-enforced; clients never invent a number.</summary>
	public class RecordDefinitionNumbering
	{
		public string Prefix { get; set; }
		public RmsNumberAssignment Assignment { get; set; } = RmsNumberAssignment.OnFinalize;
		public bool PerGroupSequence { get; set; }

		/// <summary>
		/// Incident-scoped uniqueness and reset (Back Office plan E2), alongside the existing department and group
		/// scopes. An ICS form is numbered per incident by definition — the third ICS 214 on a Call is 003 on that
		/// Call, not 003 for the department this year. Requires the definition to permit the "call" subject; a Record
		/// with no Call falls back to the wider scope rather than colliding.
		/// </summary>
		public bool PerIncidentSequence { get; set; }
		public bool ResetYearly { get; set; } = true;
		public int SequenceWidth { get; set; } = 4;

		public static RecordDefinitionNumbering Parse(string json) => string.IsNullOrWhiteSpace(json) ? new RecordDefinitionNumbering() : JsonConvert.DeserializeObject<RecordDefinitionNumbering>(json) ?? new RecordDefinitionNumbering();
	}

	/// <summary>Per-version eligibility for the field apps (RMS plan section 5.2 RmsDefinitionClientSurface), kept as a document on the version.</summary>
	public class RecordDefinitionClientSurface
	{
		public bool Responder { get; set; }
		public bool Unit { get; set; }
		public bool IncidentCommand { get; set; }
		public bool Dispatch { get; set; }
		/// <summary>Launch contexts: call, unit, contact, checklist, workorder, none.</summary>
		public List<string> LaunchContexts { get; set; } = new List<string>();
		public bool AllowOffline { get; set; }
		public bool AllowAttachments { get; set; } = true;

		/// <summary>
		/// Media capture hygiene (RMS plan RMS-1D): photo EXIF location and device metadata are stripped on upload
		/// by default, because a photo of a protected facility, a SAR subject's location or a security client's site
		/// is a disclosure nobody intended. A definition whose profile genuinely needs the coordinates — a damage
		/// assessment, a clue report — sets this, and the decision is recorded on every attachment either way.
		/// </summary>
		public bool RetainMediaLocation { get; set; }

		public string MinimumAppVersion { get; set; }

		/// <summary>
		/// No field app may author on this version — the definition is Web only (Back Office plan E7). This is the
		/// mechanical expression of a desk-authored pack: the four apps exclude it with SurfaceNotEnabled, and no
		/// bounded sync bundle ever puts it on a device. Web authoring is unaffected, because the Web renderer is
		/// not gated by the client surface.
		/// </summary>
		[JsonIgnore]
		public bool IsWebOnly => !Responder && !Unit && !IncidentCommand && !Dispatch;

		/// <summary>A Web-only surface: no app authoring, and therefore no offline drafts. Attachments stay available on the Web.</summary>
		public static RecordDefinitionClientSurface WebOnly(bool allowAttachments = true)
			=> new RecordDefinitionClientSurface { Responder = false, Unit = false, IncidentCommand = false, Dispatch = false, AllowOffline = false, AllowAttachments = allowAttachments };

		public static RecordDefinitionClientSurface Parse(string json) => string.IsNullOrWhiteSpace(json) ? new RecordDefinitionClientSurface() : JsonConvert.DeserializeObject<RecordDefinitionClientSurface>(json) ?? new RecordDefinitionClientSurface();
	}

	public class RecordDefinitionFieldMapping
	{
		public string FromFieldKey { get; set; }
		public string ToFieldKey { get; set; }
	}

	/// <summary>Key rules shared by the designer, the packs and the value seam.</summary>
	public static class RecordDefinitionKeys
	{
		public const int MaxKeyLength = 64;
		public const string TemplatePrefix = "template.";
		public const string PackPrefix = "pack.";

		public static string NormalizeKey(string key) => (key ?? string.Empty).Trim().ToLowerInvariant();

		/// <summary>Lower-case letters, digits, dots and dashes; must start with a letter; no reserved prefix.</summary>
		public static bool IsValidDefinitionKey(string key)
		{
			if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength) return false;
			if (RmsDefinitionKeys.IsSystemKey(key)) return false;
			if (!char.IsLetter(key[0])) return false;
			return key.All(c => char.IsLetterOrDigit(c) && !char.IsUpper(c) || c == '.' || c == '-' || c == '_');
		}

		/// <summary>Section/field keys: letters, digits, underscore and dash; start with a letter.</summary>
		public static bool IsValidMemberKey(string key)
		{
			if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength) return false;
			if (!char.IsLetter(key[0])) return false;
			return key.All(c => char.IsLetterOrDigit(c) && !char.IsUpper(c) || c == '_' || c == '-');
		}

		public static bool IsTemplateKey(string key) => key != null && (key.StartsWith(TemplatePrefix, StringComparison.Ordinal) || key.StartsWith(PackPrefix, StringComparison.Ordinal));
	}

	/// <summary>Client capability floors (RMS plan section 5.4). Derived from the types a version uses, never hand-entered.</summary>
	public static class RecordsClientCapabilities
	{
		/// <summary>Locked Logs-parity definitions only.</summary>
		public const string Locked = "records.v1";
		/// <summary>RMS-1B controlled catalog: the 18 launch field types, repeating groups and the bounded rules.</summary>
		public const string Configurable = "records.v1b";
		/// <summary>RMS-1C additions: currency, measured quantity, country/subdivision, Call/Inventory/Checklist references.</summary>
		public const string Packs = "records.v1c";

		private static readonly string[] Order = { Locked, Configurable, Packs };

		public static int Rank(string capability)
		{
			var index = Array.IndexOf(Order, capability ?? string.Empty);
			return index < 0 ? -1 : index;
		}

		/// <summary>True when a client reporting <paramref name="clientCapability"/> can author on a version needing <paramref name="required"/>.</summary>
		public static bool Satisfies(string clientCapability, string required) => Rank(clientCapability) >= Rank(required) && Rank(required) >= 0;

		public static bool IsPackType(RmsFieldType type) => (int)type >= (int)RmsFieldType.Currency;

		public static string Derive(RecordDefinitionSchema schema)
		{
			if (schema == null) return Configurable;
			return schema.AllFields().Any(f => IsPackType(f.Type)) ? Packs : Configurable;
		}
	}

	// ------------------------------------------------------------------------------------------------------
	// Service contracts
	// ------------------------------------------------------------------------------------------------------

	public class RecordDefinitionIssue
	{
		public string Severity { get; set; } = "error";
		public string Path { get; set; }
		public string Code { get; set; }
		public string Message { get; set; }

		public static RecordDefinitionIssue Error(string path, string code, string message) => new RecordDefinitionIssue { Severity = "error", Path = path, Code = code, Message = message };
		public static RecordDefinitionIssue Warning(string path, string code, string message) => new RecordDefinitionIssue { Severity = "warning", Path = path, Code = code, Message = message };
	}

	public class RecordDefinitionValidation
	{
		public List<RecordDefinitionIssue> Issues { get; set; } = new List<RecordDefinitionIssue>();
		public string MinimumClientCapability { get; set; }
		public bool IsValid => Issues.All(i => i.Severity != "error");
	}

	public class RecordDefinitionDraftInput
	{
		public string Name { get; set; }
		public string Category { get; set; }
		public string Description { get; set; }
		public string PermittedSubjectTypes { get; set; }
		public RmsLifecyclePreset LifecyclePreset { get; set; } = RmsLifecyclePreset.QuickEntry;
		/// <summary>How many Records of this definition may exist on one Call (plan section 5.2.1).</summary>
		public RmsRecordCardinality Cardinality { get; set; } = RmsRecordCardinality.MultiplePerCall;
		public List<int> ReviewerRoleIds { get; set; } = new List<int>();
		public List<int> ApproverRoleIds { get; set; } = new List<int>();
		public int? ReviewDueHours { get; set; }
		public int? ApproveDueHours { get; set; }
		public bool RequireAuthorAttestation { get; set; }
		public RecordDefinitionNumbering Numbering { get; set; } = new RecordDefinitionNumbering();
		public int? RetentionYears { get; set; }
		public RmsFieldClassification Classification { get; set; }
		public RecordDefinitionSchema Schema { get; set; } = new RecordDefinitionSchema();
		public RecordDefinitionClientSurface ClientSurface { get; set; } = new RecordDefinitionClientSurface();
		public List<RecordDefinitionFieldMapping> MigrationMap { get; set; } = new List<RecordDefinitionFieldMapping>();
		public string ChangeNotes { get; set; }
	}

	public class RecordDefinitionCreateInput
	{
		public string DefinitionKey { get; set; }
		public string Name { get; set; }
		public string Category { get; set; }
		/// <summary>Product template/pack definition key to clone (template.* or pack.*); exclusive with CloneFromDefinitionKey.</summary>
		public string TemplateKey { get; set; }
		/// <summary>Existing department definition to clone as a new definition.</summary>
		public string CloneFromDefinitionKey { get; set; }
		/// <summary>Jurisdiction overlay to apply when cloning a pack (RMS-1C): generic, us, ca.</summary>
		public string JurisdictionProfileKey { get; set; }
		/// <summary>Locale for overlay labels (en-US, en-CA, fr-CA).</summary>
		public string Locale { get; set; }
	}

	/// <summary>One department definition with its versions, for lists and the designer.</summary>
	public class RecordDefinitionAggregate
	{
		public RmsRecordDefinition Definition { get; set; }
		public List<RmsRecordDefinitionVersion> Versions { get; set; } = new List<RmsRecordDefinitionVersion>();
		public RmsRecordDefinitionVersion Published => Versions.FirstOrDefault(v => v.IsPublished && Definition?.CurrentPublishedVersion == v.Version) ?? Versions.Where(v => v.IsPublished).OrderByDescending(v => v.Version).FirstOrDefault();
		public RmsRecordDefinitionVersion Draft => Versions.Where(v => v.IsDraft).OrderByDescending(v => v.Version).FirstOrDefault();
		public RmsRecordDefinitionVersion Latest => Versions.OrderByDescending(v => v.Version).FirstOrDefault();
	}

	public class RecordDefinitionImpactPreview
	{
		public string DefinitionKey { get; set; }
		public int Version { get; set; }
		public string MinimumClientCapability { get; set; }
		public List<string> FieldTypesUsed { get; set; } = new List<string>();
		public bool UsesRepeatingGroups { get; set; }
		public int? CurrentPublishedVersion { get; set; }
		/// <summary>Draft Records on the currently published version that a mapping could migrate.</summary>
		public int OpenDraftsOnCurrentVersion { get; set; }
		/// <summary>Finalized Records on earlier versions; they never migrate and keep rendering against their version.</summary>
		public int FinalizedRecordsOnEarlierVersions { get; set; }
		/// <summary>Field apps enabled for this department that cannot render this version (from the client surface and capability floor).</summary>
		public List<RecordDefinitionClientImpact> Clients { get; set; } = new List<RecordDefinitionClientImpact>();
		public List<RecordDefinitionIssue> Issues { get; set; } = new List<RecordDefinitionIssue>();
		public bool BreakingChange { get; set; }
	}

	public class RecordDefinitionClientImpact
	{
		public string App { get; set; }
		public bool Enabled { get; set; }
		public bool EligibleOnSurface { get; set; }
		public string RequiredCapability { get; set; }
		/// <summary>Active clients of this app below the floor; null when the platform has no telemetry for it.</summary>
		public int? ClientsBelowFloor { get; set; }
		public string Message { get; set; }
	}

	public class RecordDefinitionDiff
	{
		public string DefinitionKey { get; set; }
		public int FromVersion { get; set; }
		public int ToVersion { get; set; }
		public List<RecordDefinitionDiffEntry> Entries { get; set; } = new List<RecordDefinitionDiffEntry>();
		public bool Breaking => Entries.Any(e => e.Breaking);
	}

	public class RecordDefinitionDiffEntry
	{
		public string Kind { get; set; }   // section | field | policy
		public string Change { get; set; } // added | removed | changed
		public string Key { get; set; }
		public string Detail { get; set; }
		public bool Breaking { get; set; }
	}

	public class RecordDefinitionMigrationResult
	{
		public int Migrated { get; set; }
		public int Skipped { get; set; }
		public List<string> SkippedRecordIds { get; set; } = new List<string>();
		public List<string> UnmappedFieldKeys { get; set; } = new List<string>();
	}

	/// <summary>What the client contract advertises for one definition (locked or department), RMS plan section 5.4.</summary>
	public class RecordDefinitionSummary
	{
		public string Key { get; set; }
		public string Name { get; set; }
		public string Category { get; set; }
		public string Owner { get; set; }
		public bool Locked { get; set; }
		public int? PublishedVersion { get; set; }
		public int? DraftVersion { get; set; }
		public bool Retired { get; set; }
		public string LifecyclePreset { get; set; }
		public string Cardinality { get; set; }
		public string MinimumClientCapability { get; set; }
		public string TemplateKey { get; set; }
		public string JurisdictionProfileKey { get; set; }
		public string ArtifactStatus { get; set; }
	}
}
