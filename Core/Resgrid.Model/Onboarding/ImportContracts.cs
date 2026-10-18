using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Onboarding
{
	/// <summary>
	/// The typed boundary of what a file may write into one Resgrid entity (wizard plan section 8.3). Contracts are
	/// embedded, versioned JSON in Resgrid.Providers.Import/Catalog and validated at load and by ImportCatalogTests.
	/// </summary>
	public sealed class ImportEntityContract
	{
		/// <summary>Stable, public key ("personnel", "units", ...).</summary>
		public string Key { get; set; }
		public string Version { get; set; }
		/// <summary>DepartmentImport resx key of the label ("Entity.personnel").</summary>
		public string LabelKey { get; set; }
		public string DescriptionKey { get; set; }
		/// <summary>Font Awesome icon for domain cards.</summary>
		public string Icon { get; set; }
		/// <summary>Domain group for the Choose step ("organization", "people", "units", "dispatch", "geography").</summary>
		public string Area { get; set; }
		/// <summary>Delivery wave ("2", "2B", "2C", "3").</summary>
		public string Wave { get; set; }
		/// <summary>Order within the dependency graph (lower imports first; section 10.3).</summary>
		public int Order { get; set; }
		public List<string> DependsOn { get; set; } = new List<string>();
		public ImportContractGate Gate { get; set; } = new ImportContractGate();
		/// <summary>Field keys that identify a row in its source ("source_id"), or empty when a natural key applies.</summary>
		public List<string> IdentityFields { get; set; } = new List<string>();
		/// <summary>The documented deterministic key used when a source has no stable id (section 8.5), as field keys.</summary>
		public List<string> NaturalKey { get; set; } = new List<string>();
		/// <summary>Whether a "keep as custom field" column creates a UDF on this entity.</summary>
		public bool SupportsCustomFields { get; set; }
		/// <summary>How existing department records match (name, email, external id; section 12.2).</summary>
		public string MatchPolicy { get; set; }
		/// <summary>Maximum rows per dataset for this entity (0 = the plan limit).</summary>
		public int MaxRows { get; set; }
		public List<ImportFieldContract> Fields { get; set; } = new List<ImportFieldContract>();
		public List<ImportRowRule> RowRules { get; set; } = new List<ImportRowRule>();

		public ImportFieldContract Field(string key) =>
			Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>Flags, module, add-on and permission a contract needs (plan section 8.3 Gate).</summary>
	public sealed class ImportContractGate
	{
		public List<string> Flags { get; set; } = new List<string>();
		/// <summary>DepartmentModuleSettings switch that must be enabled, e.g. "Mapping" (null = none).</summary>
		public string Module { get; set; }
		/// <summary>Plan add-on type name required, e.g. "BusinessOperations" (null = none).</summary>
		public string Addon { get; set; }
		/// <summary>PermissionTypes names the approver also needs (each target module's own write permission).</summary>
		public List<string> Permissions { get; set; } = new List<string>();
	}

	/// <summary>One importable data point (plan section 8.3 ImportFieldContract).</summary>
	public sealed class ImportFieldContract
	{
		public string Key { get; set; }
		public string LabelKey { get; set; }
		public string DescriptionKey { get; set; }
		/// <summary>An example value shown next to the field and in templates (never personal data).</summary>
		public string Example { get; set; }
		/// <summary><see cref="ImportFieldTypes"/> value.</summary>
		public string Type { get; set; }
		public int? MaxLength { get; set; }
		public decimal? Min { get; set; }
		public decimal? Max { get; set; }
		/// <summary>Choice codes for Type "choice".</summary>
		public List<ImportChoiceCode> Choices { get; set; } = new List<ImportChoiceCode>();
		/// <summary>The referenced entity for Type "reference" (or a multi-value of references).</summary>
		public string References { get; set; }
		/// <summary>How a reference matches: "source-id", "name", "email" or "existing-record".</summary>
		public string ReferenceMatch { get; set; }
		/// <summary>The inner type of a "multi" field.</summary>
		public string ItemType { get; set; }
		public string Separator { get; set; }
		public bool Required { get; set; }
		/// <summary>A row-rule expression from the fixed rule catalog that makes the field required (e.g. "present:latitude").</summary>
		public string RequiredWhen { get; set; }
		/// <summary>"none", "in-file" or "in-department".</summary>
		public string Unique { get; set; } = "none";
		/// <summary>ProtectedFieldCatalog binding ("departmentmembersensitivedata.identificationnumber"); null when not protected.</summary>
		public string Protected { get; set; }
		/// <summary><see cref="ImportFieldSensitivity"/> name: Internal, Personal or Restricted.</summary>
		public string Sensitivity { get; set; } = "Internal";
		/// <summary>Transform keys from the fixed catalog this field allows (empty = the type's defaults).</summary>
		public List<string> Transforms { get; set; } = new List<string>();
		/// <summary>The writer property the value lands on ("UserProfile.MobileNumber"); asserted by ImportContractTests.</summary>
		public string WriterPath { get; set; }
		/// <summary>Header aliases used by automatic column matching (lower-case, spaces and punctuation ignored).</summary>
		public List<string> Aliases { get; set; } = new List<string>();
		/// <summary>True for fields only relevant to a reviewed update (never required on create).</summary>
		public bool UpdateOnly { get; set; }

		[System.Text.Json.Serialization.JsonIgnore, Newtonsoft.Json.JsonIgnore]
		public ImportFieldSensitivity SensitivityLevel =>
			Enum.TryParse<ImportFieldSensitivity>(Sensitivity, true, out var s) ? s : ImportFieldSensitivity.Internal;

		/// <summary>
		/// Whether "the same value for every row" may fill this field: never the Source ID or a unique field (every row would
		/// collide).
		/// </summary>
		[System.Text.Json.Serialization.JsonIgnore, Newtonsoft.Json.JsonIgnore]
		public bool AllowsConstant =>
			!string.Equals(Key, "source_id", StringComparison.OrdinalIgnoreCase) && (string.IsNullOrEmpty(Unique) || string.Equals(Unique, "none", StringComparison.OrdinalIgnoreCase));
	}

	public sealed class ImportChoiceCode
	{
		public string Code { get; set; }
		public string LabelKey { get; set; }
		/// <summary>Accepted source spellings (case- and punctuation-insensitive).</summary>
		public List<string> Aliases { get; set; } = new List<string>();
	}

	/// <summary>A cross-field rule from the fixed rule catalog (plan section 8.3 RowRules).</summary>
	public sealed class ImportRowRule
	{
		/// <summary>"after-or-equal" (Field ≥ Other), "both-or-neither", "one-of-required".</summary>
		public string Kind { get; set; }
		public string Field { get; set; }
		public string Other { get; set; }
		public List<string> Fields { get; set; } = new List<string>();
		/// <summary>Issue code raised when the rule fails.</summary>
		public string Code { get; set; }
	}

	/// <summary>The field type vocabulary (plan section 8.3).</summary>
	public static class ImportFieldTypes
	{
		public const string Text = "text";
		public const string Integer = "integer";
		public const string Decimal = "decimal";
		public const string Boolean = "boolean";
		public const string Date = "date";
		public const string DateTime = "datetime";
		public const string Time = "time";
		public const string Email = "email";
		public const string Phone = "phone";
		public const string Url = "url";
		public const string Choice = "choice";
		public const string Reference = "reference";
		public const string Latitude = "latitude";
		public const string Longitude = "longitude";
		public const string Colour = "colour";
		public const string Multi = "multi";
		public const string TimeZone = "timezone";
		public const string Language = "language";

		public static readonly IReadOnlyCollection<string> All = new[]
		{
			Text, Integer, Decimal, Boolean, Date, DateTime, Time, Email, Phone, Url, Choice, Reference, Latitude, Longitude, Colour, Multi,
			TimeZone, Language
		};
	}

	/// <summary>
	/// A product pack (plan sections 7.3–7.6): detection signatures and saved column maps for a documented source format,
	/// plus the guide, crosswalk badges and terminology the UI shows.
	/// </summary>
	public sealed class ImportProductPack
	{
		public string Key { get; set; }
		public string Version { get; set; }
		public string ProductName { get; set; }
		/// <summary>"file-documented", "api-extraction", "file-sample-validated" or "none".</summary>
		public string Kind { get; set; }
		/// <summary>"A", "B", "C" or "D" (section 7.6).</summary>
		public string Grade { get; set; }
		/// <summary>Delivery phase ("2", "2B", "3B").</summary>
		public string Phase { get; set; }
		/// <summary>Published only when the pack passed its fixtures; unpublished packs show the guide and generic mapping.</summary>
		public bool Published { get; set; }
		public string SourceUrl { get; set; }
		public string VerifiedOn { get; set; }
		public string GuideKey { get; set; }
		public List<ImportPackDataset> Datasets { get; set; } = new List<ImportPackDataset>();
		public List<ImportCrosswalkEntry> Crosswalk { get; set; } = new List<ImportCrosswalkEntry>();
		/// <summary>The product's own words and where each lands in Resgrid, for the glossary drawer (section 6.4).</summary>
		public List<ImportGlossaryEntry> Glossary { get; set; } = new List<ImportGlossaryEntry>();
		/// <summary>Column headers (normalized) that are always dropped (section 8.9).</summary>
		public List<string> DroppedColumns { get; set; } = new List<string>();
	}

	/// <summary>
	/// One word of a source product's vocabulary ("Members", "Apparatus") and where that kind of record lands in Resgrid. A
	/// word that is the product's name for a Resgrid feature can also name it: the Configure step then offers the word as the
	/// department's own name for that feature (wizard plan section 7.5, terminology).
	/// </summary>
	public sealed class ImportGlossaryEntry
	{
		/// <summary>The word as the product shows it.</summary>
		public string Term { get; set; }
		/// <summary>The language the product shows it in ("en", "de").</summary>
		public string Language { get; set; }
		/// <summary>
		/// "entity:{contract key}" (imported as that data set), "term:{terminology key}" (a Resgrid feature the import does not
		/// fill) or "none" (Resgrid keeps no such record).
		/// </summary>
		public string Target { get; set; }
		/// <summary>Optional terminology key (<see cref="DepartmentTerms"/>) the word names; the word is then a terminology suggestion.</summary>
		public string Names { get; set; }
	}

	/// <summary>One recognizable export of a pack, with its detection signature and column map.</summary>
	public sealed class ImportPackDataset
	{
		public string Key { get; set; }
		public string EntityKey { get; set; }
		/// <summary>Normalized headers that must all be present for a match.</summary>
		public List<string> RequiredHeaders { get; set; } = new List<string>();
		/// <summary>Normalized headers that raise confidence when present.</summary>
		public List<string> OptionalHeaders { get; set; } = new List<string>();
		/// <summary>Sheet-name patterns (lower-case substrings) that raise confidence.</summary>
		public List<string> SheetNames { get; set; } = new List<string>();
		/// <summary>Source column → contract field key.</summary>
		public Dictionary<string, string> Columns { get; set; } = new Dictionary<string, string>();
		public string SourceIdentityColumn { get; set; }
		/// <summary>Fixed value crosswalks: field key → (source value → choice code).</summary>
		public Dictionary<string, Dictionary<string, string>> Values { get; set; } = new Dictionary<string, Dictionary<string, string>>();
	}

	/// <summary>A capability crosswalk cell with its honest support badge (section 7.1).</summary>
	public sealed class ImportCrosswalkEntry
	{
		public string Capability { get; set; }
		/// <summary>native, configurable, partial, archive-only, external, unsupported.</summary>
		public string Support { get; set; }
		public string NoteKey { get; set; }
	}

	/// <summary>The loaded, validated import catalog (contracts, packs and transforms) pinned by version (section 7.4).</summary>
	public interface IImportCatalog
	{
		/// <summary>The current catalog version ("2026.11.1").</summary>
		string Version { get; }

		IReadOnlyList<ImportEntityContract> Contracts { get; }

		IReadOnlyList<ImportProductPack> Packs { get; }

		ImportEntityContract GetContract(string key);

		ImportProductPack GetPack(string key);

		/// <summary>Contracts in dependency order (topological, then by Order).</summary>
		IReadOnlyList<ImportEntityContract> InDependencyOrder();

		/// <summary>Validation problems found at load (empty for a healthy catalog).</summary>
		IReadOnlyList<string> Validate();

		/// <summary>Every loadable catalog version: the current one and the archived ones kept for pinned workspaces.</summary>
		IReadOnlyList<string> Versions { get; }

		/// <summary>The catalog as it was at <paramref name="version"/> (section 7.4 pinning); null when not loadable.</summary>
		IImportCatalog GetVersion(string version);
	}
}
