using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A department-authored report export (RMS plan sections 4.7 "customer-owned analytics egress", 4.10 and
	/// 5.6 "Actions and safety boundary"): which records, which columns, which file format, and optionally a
	/// schedule. The template renders through <c>IRecordsExportService</c> into an <see cref="RmsExportRun"/>
	/// that a Workflow step attaches to an email or uploads to FTP/SFTP/S3/Blob/Box/Dropbox — the delivery path
	/// for the many state, provincial and local agencies that accept files but expose no API.
	/// <para>
	/// Columns come from <c>RecordsExportFieldCatalog</c>, never from free text. Narrative and restricted
	/// sections are opt-in behind an egress acknowledgement, and are still withheld when Advanced Data
	/// Protection is enforced, because Workflow egress can never relax ADP (ADP plan, egress policy).
	/// </para>
	/// </summary>
	[Table("RmsExportTemplates")]
	public class RmsExportTemplate : IEntity
	{
		public string RmsExportTemplateId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		/// <summary>Stable key a workflow condition can test (<c>export.template_key</c>); lower-case slug of the name at creation, never renamed.</summary>
		public string TemplateKey { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		/// <summary><see cref="RmsExportFormat"/>.</summary>
		public int Format { get; set; }

		/// <summary><see cref="RmsExportScope"/>.</summary>
		public int Scope { get; set; }

		/// <summary>Comma-separated definition keys; null = every locked definition plus incident reports.</summary>
		public string DefinitionKeysCsv { get; set; }

		/// <summary>Ordered JSON array of field keys from RecordsExportFieldCatalog.</summary>
		public string ColumnsJson { get; set; }

		/// <summary>Include narrative-class fields (Tier 2 candidates). Requires the egress acknowledgement.</summary>
		public bool IncludeNarrative { get; set; }

		/// <summary>Include restricted-section fields (Tier 1). Requires the egress acknowledgement and RecordRestricted_View at authoring.</summary>
		public bool IncludeRestricted { get; set; }

		public DateTime? EgressAcknowledgedOn { get; set; }

		public string EgressAcknowledgedByUserId { get; set; }

		/// <summary>Scriban file-name template; <c>{{ template.key }}-{{ window.end | date.to_string "%Y%m%d" }}</c> by default.</summary>
		public string FileNameTemplate { get; set; }

		public bool IncludeHeader { get; set; }

		/// <summary>CSV delimiter; "," by default.</summary>
		public string Delimiter { get; set; }

		/// <summary><see cref="RmsExportScheduleKind"/>; None for record-triggered templates.</summary>
		public int ScheduleKind { get; set; }

		/// <summary>Hour of day, department-local, the scheduled export renders.</summary>
		public int ScheduleHourLocal { get; set; }

		/// <summary>0 = Sunday; weekly schedules only.</summary>
		public int ScheduleDayOfWeek { get; set; }

		/// <summary>1-28; monthly schedules only.</summary>
		public int ScheduleDayOfMonth { get; set; }

		/// <summary>Records finalized in the previous N days for a window export (Window scope); the schedule period when 0.</summary>
		public int WindowDays { get; set; }

		public DateTime? NextRunOn { get; set; }

		public DateTime? LastRunOn { get; set; }

		public bool IsEnabled { get; set; }

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
			get { return RmsExportTemplateId; }
			set { RmsExportTemplateId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RmsExportTemplates";

		[NotMapped]
		public string IdName => "RmsExportTemplateId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public enum RmsExportFormat
	{
		Csv = 1,
		Json = 2,
		Pdf = 3
	}

	public enum RmsExportScope
	{
		/// <summary>One file per triggering record; rendered when a Records workflow step references the template.</summary>
		TriggeringRecord = 1,

		/// <summary>Every finalized record in a time window; rendered by the schedule (worker 45) or on demand.</summary>
		Window = 2
	}

	public enum RmsExportScheduleKind
	{
		None = 0,
		Daily = 1,
		Weekly = 2,
		Monthly = 3
	}

	/// <summary>
	/// One rendered export (RMS plan section 4.10): the bytes, their checksum and what produced them, retained
	/// so the delivery step can fetch it after the render and so an auditor can see exactly what left. The
	/// artifact inherits the highest classification of its source (ADP catalog v10 binary field).
	/// </summary>
	[Table("RmsExportRuns")]
	public class RmsExportRun : IEntity
	{
		public string RmsExportRunId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		public string TemplateId { get; set; }

		public string TemplateKey { get; set; }

		/// <summary><see cref="RmsExportTrigger"/>.</summary>
		public int Trigger { get; set; }

		/// <summary>The record that triggered a TriggeringRecord render, when any.</summary>
		public string RecordId { get; set; }

		public DateTime? WindowStart { get; set; }

		public DateTime? WindowEnd { get; set; }

		public int RecordCount { get; set; }

		public string FileName { get; set; }

		public string ContentType { get; set; }

		public long ByteSize { get; set; }

		public string Checksum { get; set; }

		public byte[] Data { get; set; }

		/// <summary>True when protected or restricted fields were withheld from this render.</summary>
		public bool Redacted { get; set; }

		public string RedactedFieldsJson { get; set; }

		public DateTime GeneratedOn { get; set; }

		/// <summary>Null for a worker render.</summary>
		public string GeneratedByUserId { get; set; }

		public string WorkflowRunId { get; set; }

		public DateTime ExpiresOn { get; set; }

		public bool IsProtected { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public DateTime? DeletedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsExportRunId; }
			set { RmsExportRunId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RmsExportRuns";

		[NotMapped]
		public string IdName => "RmsExportRunId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public enum RmsExportTrigger
	{
		Scheduled = 1,
		Record = 2,
		Manual = 3
	}
}
