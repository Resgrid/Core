using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	public enum RmsRecordPrintLayoutScope
	{
		/// <summary>One per department: the branding block, letterhead lines, footer, watermark, page size, date format.</summary>
		DepartmentDefault = 1,
		/// <summary>Per definition version (RMS-1B designer).</summary>
		Definition = 2
	}

	/// <summary>
	/// The DepartmentDefault print layout content (RMS plan section 4.10.1). Identity and logo come from the
	/// Department Profile; this holds only how they render plus the letterhead extras. Serialized as JSON on
	/// <see cref="RmsRecordPrintLayout.ConfigJson"/>.
	/// </summary>
	public class RecordsPrintLayoutConfig
	{
		public static readonly string[] PageSizes = { "Letter", "A4" };

		public bool ShowLogo { get; set; } = true;
		public bool UseShortName { get; set; }
		public bool ShowAddress { get; set; } = true;
		public bool ShowPhone { get; set; } = true;
		public bool ShowWebsite { get; set; } = true;
		public string LetterheadLine1 { get; set; }
		public string LetterheadLine2 { get; set; }
		public string FooterText { get; set; }
		public string WatermarkLabel { get; set; }
		public string PageSize { get; set; } = "Letter";
		/// <summary>.NET date/time format applied in the department time zone; null keeps the department default.</summary>
		public string DateTimeFormat { get; set; }

		public static RecordsPrintLayoutConfig Default() => new RecordsPrintLayoutConfig();

		public static string NormalizePageSize(string value)
		{
			return string.Equals(value, "A4", StringComparison.OrdinalIgnoreCase) ? "A4" : "Letter";
		}
	}

	/// <summary>Versioned print layout row (migration M0160). Only the DepartmentDefault scope is written in RMS-1.</summary>
	[Table("RmsRecordPrintLayouts")]
	/// <summary>
	/// The Definition-scope print layout (RMS plan section 4.10.1): presentation over a department definition's approved
	/// sections and fields — order, visibility, headings, page breaks, signature-block placement, attachment-list style
	/// and an optional branding-block override. Presentation only: a hidden-by-layout field is still exported by data
	/// exports per its flags, and no layout bypasses restricted/protected/group rules or the provenance footer.
	/// </summary>
	public class RecordsDefinitionLayoutConfig
	{
		public const string SignatureAtEnd = "end";
		public const string SignatureInline = "inline";
		public const string SignatureNone = "none";
		public static readonly string[] SignaturePlacements = { SignatureAtEnd, SignatureInline, SignatureNone };

		public const string AttachmentsTable = "table";
		public const string AttachmentsList = "list";
		public const string AttachmentsNone = "none";
		public static readonly string[] AttachmentStyles = { AttachmentsTable, AttachmentsList, AttachmentsNone };

		/// <summary>Null applies to every version of the definition; otherwise only Records pinned to this version use it.</summary>
		public int? AppliesToVersion { get; set; }
		public List<string> SectionOrder { get; set; } = new List<string>();
		public List<string> HiddenSectionKeys { get; set; } = new List<string>();
		public List<string> HiddenFieldKeys { get; set; } = new List<string>();
		public Dictionary<string, string> SectionHeadings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public List<string> PageBreakBeforeSectionKeys { get; set; } = new List<string>();
		public string SignatureBlockPlacement { get; set; } = SignatureAtEnd;
		public string AttachmentListStyle { get; set; } = AttachmentsTable;
		/// <summary>Null keeps the department default branding block; otherwise these values replace it for this definition.</summary>
		public RecordsPrintLayoutConfig BrandingOverrides { get; set; }

		public static RecordsDefinitionLayoutConfig Default() => new RecordsDefinitionLayoutConfig();

		public bool AppliesTo(int definitionVersion) => !AppliesToVersion.HasValue || AppliesToVersion.Value == definitionVersion;
		public bool IsSectionVisible(string sectionKey) => !HiddenSectionKeys.Contains(sectionKey ?? string.Empty, StringComparer.OrdinalIgnoreCase);
		public bool IsFieldVisible(string fieldKey) => !HiddenFieldKeys.Contains(fieldKey ?? string.Empty, StringComparer.OrdinalIgnoreCase);
		public bool PageBreakBefore(string sectionKey) => PageBreakBeforeSectionKeys.Contains(sectionKey ?? string.Empty, StringComparer.OrdinalIgnoreCase);
		public string HeadingFor(string sectionKey, string fallback) => SectionHeadings.TryGetValue(sectionKey ?? string.Empty, out var heading) && !string.IsNullOrWhiteSpace(heading) ? heading : fallback;

		/// <summary>Section keys in layout order: listed keys first in their order, then any the layout does not mention, hidden ones dropped.</summary>
		public List<string> OrderedSectionKeys(IEnumerable<string> schemaSectionKeys)
		{
			var all = (schemaSectionKeys ?? Enumerable.Empty<string>()).ToList();
			var ordered = SectionOrder.Where(k => all.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
			ordered.AddRange(all.Where(k => !ordered.Contains(k, StringComparer.OrdinalIgnoreCase)));
			return ordered.Where(IsSectionVisible).ToList();
		}
	}

	/// <summary>What print resolves for one Record: the branding block, the definition layout (if any) and the composite layout version stamped on the footer.</summary>
	public class RecordsResolvedPrintLayout
	{
		public RecordsPrintLayoutConfig Branding { get; set; } = RecordsPrintLayoutConfig.Default();
		public string BrandingLayoutVersion { get; set; } = RmsRecordPrintLayout.GeneratedLayoutVersion;
		public RecordsDefinitionLayoutConfig Definition { get; set; }
		public string DefinitionLayoutVersion { get; set; }
		public string LayoutVersion => Definition == null ? BrandingLayoutVersion : DefinitionLayoutVersion + "+" + BrandingLayoutVersion;
	}

	public class RmsRecordPrintLayout : IEntity
	{
		public const string GeneratedLayoutVersion = "system-default/1";

		[Key]
		[Required]
		public string RmsRecordPrintLayoutId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		/// <summary><see cref="RmsRecordPrintLayoutScope"/>.</summary>
		public int Scope { get; set; }

		/// <summary>Empty string for the DepartmentDefault scope so the (DepartmentId, Scope, DefinitionKey) unique index works in both dialects.</summary>
		public string DefinitionKey { get; set; } = string.Empty;

		public int Version { get; set; }

		public string ConfigJson { get; set; }

		public string ModifiedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		/// <summary>The value the provenance footer prints; the generated default when no row has been saved yet.</summary>
		[NotMapped]
		public string LayoutVersion => Version <= 0
			? GeneratedLayoutVersion
			: (Scope == (int)RmsRecordPrintLayoutScope.DepartmentDefault ? "department-default/" + Version : DefinitionKey + "/" + Version);

		[NotMapped]
		public RecordsPrintLayoutConfig Config { get; set; }

		/// <summary>Parsed Definition-scope config; null on a DepartmentDefault row.</summary>
		[NotMapped]
		public RecordsDefinitionLayoutConfig DefinitionConfig { get; set; }

		[NotMapped]
		public object IdValue
		{
			get { return RmsRecordPrintLayoutId; }
			set { RmsRecordPrintLayoutId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RmsRecordPrintLayouts";

		[NotMapped]
		public string IdName => "RmsRecordPrintLayoutId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "LayoutVersion", "Config", "DefinitionConfig" };
	}
}
