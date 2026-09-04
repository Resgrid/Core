using System;
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
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "LayoutVersion", "Config" };
	}
}
