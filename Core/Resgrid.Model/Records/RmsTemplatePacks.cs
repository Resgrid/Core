using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>How an artifact produced from a pack may be described (RMS plan section 4.1). Nothing product-shipped is Exact until validated against the current source profile.</summary>
	public enum RmsArtifactStatus
	{
		DepartmentLocal = 0,
		Compatible = 1,
		Exact = 2
	}

	/// <summary>
	/// Product-managed vertical pack/version (RMS plan section 5.2 RmsTemplatePackVersion, registry M0162). Rows are
	/// product scope (DepartmentId 0) and are upserted from the code catalog so departments see release notes, source
	/// provenance and deprecation without a redeploy. A pack update never mutates a department clone.
	/// </summary>
	public class RmsTemplatePackVersion : IEntity
	{
		public const int ProductDepartmentId = 0;

		public string RmsTemplatePackVersionId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string PackKey { get; set; }
		public int Version { get; set; }
		public string Name { get; set; }
		public string Category { get; set; }
		public string Description { get; set; }
		public bool IsPreview { get; set; }
		/// <summary>Comma-separated definition keys the pack ships (template.* / pack.* keys).</summary>
		public string DefinitionKeys { get; set; }
		/// <summary>Comma-separated jurisdiction profile keys the pack supports (generic, us, ca, us-ca).</summary>
		public string SupportedProfiles { get; set; }
		public string SupportedLocales { get; set; }
		public string ReleaseNotes { get; set; }
		/// <summary>Serialized list of <see cref="RmsSourceProvenance"/>: which published sources shaped the pack and when they were reviewed.</summary>
		public string SourceProvenanceJson { get; set; }
		public DateTime? ReviewedOn { get; set; }
		public int ArtifactStatus { get; set; }
		public string ContentChecksum { get; set; }
		public bool IsDeprecated { get; set; }
		public string DeprecatedByPackKey { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsTemplatePackVersionId; }
			set { RmsTemplatePackVersionId = value?.ToString(); }
		}

		[NotMapped] public string TableName => "RmsTemplatePackVersions";
		[NotMapped] public string IdName => "RmsTemplatePackVersionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// Locked jurisdiction/standards overlay (RMS plan section 5.2 RmsJurisdictionProfileVersion, registry M0162):
	/// country, subdivision/agency scope, terminology, units, currency, locales, classification/retention defaults and
	/// whether artifacts are exact, compatible or department-local. Product scope (DepartmentId 0).
	/// </summary>
	public class RmsJurisdictionProfileVersion : IEntity
	{
		public string RmsJurisdictionProfileVersionId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string ProfileKey { get; set; }
		public int Version { get; set; }
		public string Name { get; set; }
		/// <summary>ISO 3166-1 alpha-2, or "XX" for the generic base and "US-CA" style for a cross-border pair.</summary>
		public string Country { get; set; }
		public string Subdivision { get; set; }
		public string AgencyScope { get; set; }
		public string DefaultLocale { get; set; }
		public string SupportedLocales { get; set; }
		/// <summary>metric | customary</summary>
		public string MeasurementSystem { get; set; }
		public string CurrencyCode { get; set; }
		public string DefaultTimeZone { get; set; }
		/// <summary>Serialized dictionary locale -> (term -> label).</summary>
		public string TerminologyJson { get; set; }
		/// <summary>Serialized list of <see cref="RmsSourceProvenance"/>: form/rule identifiers and source versions.</summary>
		public string StandardsJson { get; set; }
		public int ClassificationDefault { get; set; }
		public int? RetentionYearsDefault { get; set; }
		public string RequiredSections { get; set; }
		public int ArtifactStatus { get; set; }
		public DateTime? ReviewedOn { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsJurisdictionProfileVersionId; }
			set { RmsJurisdictionProfileVersionId = value?.ToString(); }
		}

		[NotMapped] public string TableName => "RmsJurisdictionProfileVersions";
		[NotMapped] public string IdName => "RmsJurisdictionProfileVersionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };

		[NotMapped]
		[JsonIgnore]
		public Dictionary<string, Dictionary<string, string>> Terminology => string.IsNullOrWhiteSpace(TerminologyJson) ? new Dictionary<string, Dictionary<string, string>>() : JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(TerminologyJson) ?? new Dictionary<string, Dictionary<string, string>>();
	}

	public class RmsSourceProvenance
	{
		public string Identifier { get; set; }
		public string Title { get; set; }
		public string Publisher { get; set; }
		public string Version { get; set; }
		public string Url { get; set; }
		public DateTime? ReviewedOn { get; set; }
		/// <summary>operational-aid | named-form</summary>
		public string Kind { get; set; } = "operational-aid";
	}

	/// <summary>A product template or pack definition as shipped in code: the generic base schema plus per-profile overlays.</summary>
	public class RecordTemplateDefinition
	{
		public string Key { get; set; }
		public string PackKey { get; set; }
		public string Name { get; set; }
		public string Category { get; set; }
		public string Description { get; set; }
		public RmsLifecyclePreset LifecyclePreset { get; set; } = RmsLifecyclePreset.QuickEntry;
		public string NumberPrefix { get; set; }
		public string PermittedSubjectTypes { get; set; }
		public RmsFieldClassification Classification { get; set; }
		public int? RetentionYears { get; set; }
		public RecordDefinitionSchema Schema { get; set; } = new RecordDefinitionSchema();
		public RecordDefinitionClientSurface ClientSurface { get; set; } = new RecordDefinitionClientSurface { Responder = true, Unit = true, IncidentCommand = true, Dispatch = true, AllowOffline = true };
		/// <summary>Profile-specific overrides (RMS-1C): profile key -> overlay.</summary>
		public Dictionary<string, RecordTemplateOverlay> Overlays { get; set; } = new Dictionary<string, RecordTemplateOverlay>(StringComparer.OrdinalIgnoreCase);
		/// <summary>Fields whose classification is forced by the pack (subject, treatment, exposure, manifest, release, regulatory); a clone cannot loosen them.</summary>
		public List<string> LockedClassificationFieldKeys { get; set; } = new List<string>();

		/// <summary>
		/// Per-pack protected-data policies (RMS-1C): the classification floor a category of fields carries in every
		/// rendering and every department clone. A floor can be raised by the department, never lowered; search, Workflow,
		/// reports and exports project the field by its classification, so a floor also fixes the safe projection.
		/// </summary>
		public List<RecordTemplateFieldPolicy> ProtectedDataPolicies { get; set; } = new List<RecordTemplateFieldPolicy>();

		/// <summary>The floor for a field: the strictest policy naming it, or the locked-key floor (Restricted), or none.</summary>
		public RmsFieldClassification? FloorFor(string fieldKey)
		{
			RmsFieldClassification? floor = null;
			foreach (var policy in ProtectedDataPolicies.Where(p => p.FieldKeys.Contains(fieldKey, StringComparer.OrdinalIgnoreCase)))
				if (!floor.HasValue || policy.Floor > floor.Value) floor = policy.Floor;
			if (LockedClassificationFieldKeys.Contains(fieldKey, StringComparer.OrdinalIgnoreCase) && (!floor.HasValue || floor.Value < RmsFieldClassification.Restricted))
				floor = RmsFieldClassification.Restricted;
			return floor;
		}
	}

	/// <summary>What a jurisdiction overlay changes on a template: labels by locale, units, currency, added required sections, provenance.</summary>
	/// <summary>One protected-data policy of a template: a category of fields and the classification floor they carry.</summary>
	public class RecordTemplateFieldPolicy
	{
		/// <summary>subject-clue-recovery, treatment-casualty, exposure-health, manifest-travel, facility-security, release, regulatory.</summary>
		public string Category { get; set; }
		public List<string> FieldKeys { get; set; } = new List<string>();
		public RmsFieldClassification Floor { get; set; } = RmsFieldClassification.Restricted;
		public string Rationale { get; set; }
	}

	public class RecordTemplateOverlay
	{
		public string ProfileKey { get; set; }
		/// <summary>locale -> (field or section key -> label)</summary>
		public Dictionary<string, Dictionary<string, string>> Labels { get; set; } = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
		/// <summary>field key -> unit code (quantity fields) in the profile's measurement system.</summary>
		public Dictionary<string, string> DefaultUnits { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public string CurrencyCode { get; set; }
		public List<string> RequiredSectionKeys { get; set; } = new List<string>();
		public List<RmsSourceProvenance> Sources { get; set; } = new List<RmsSourceProvenance>();
		public RmsArtifactStatus ArtifactStatus { get; set; } = RmsArtifactStatus.Compatible;
	}

	/// <summary>Catalog entry for the template browser: pack metadata plus the definitions it ships.</summary>
	public class RecordTemplatePackSummary
	{
		public string PackKey { get; set; }
		public int Version { get; set; }
		public string Name { get; set; }
		public string Category { get; set; }
		public string Description { get; set; }
		public bool IsPreview { get; set; }
		public string ArtifactStatus { get; set; }
		public List<string> SupportedProfiles { get; set; } = new List<string>();
		public List<string> SupportedLocales { get; set; } = new List<string>();
		public DateTime? ReviewedOn { get; set; }
		public List<RecordTemplateSummary> Definitions { get; set; } = new List<RecordTemplateSummary>();
		public List<RmsSourceProvenance> Sources { get; set; } = new List<RmsSourceProvenance>();
	}

	public class RecordTemplateSummary
	{
		public string Key { get; set; }
		public string Name { get; set; }
		public string Category { get; set; }
		public string Description { get; set; }
		public string LifecyclePreset { get; set; }
		public int SectionCount { get; set; }
		public int FieldCount { get; set; }
		public string MinimumClientCapability { get; set; }
		public string ArtifactStatus { get; set; }
		public bool IsPreview { get; set; }
	}

	/// <summary>A published pack definition rendered for one profile and locale (labels, units, currency applied).</summary>
	public class RecordTemplateRendering
	{
		/// <summary>The pack's protected-data policies as applied to this rendering (RMS-1C).</summary>
		public List<RecordTemplateFieldPolicy> Policies { get; set; } = new List<RecordTemplateFieldPolicy>();
		public RecordTemplateDefinition Template { get; set; }
		public string ProfileKey { get; set; }
		public string Locale { get; set; }
		public string MeasurementSystem { get; set; }
		public string CurrencyCode { get; set; }
		public RecordDefinitionSchema Schema { get; set; }
		public RmsArtifactStatus ArtifactStatus { get; set; }
		public List<RmsSourceProvenance> Sources { get; set; } = new List<RmsSourceProvenance>();
		/// <summary>"Compatible with <source>; not an exact named form" style statement every generated artifact displays.</summary>
		public string ProvenanceStatement { get; set; }
	}
}
