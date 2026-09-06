using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Bounded aggregation the saved-report runner supports (RMS plan section 4.1 "Reporting and presentation").</summary>
	public enum RmsReportAggregate
	{
		Count = 1,
		Sum = 2,
		Average = 3,
		Minimum = 4,
		Maximum = 5
	}

	/// <summary>
	/// Department saved report over one definition (RMS plan section 5.2 RmsSavedReportDefinition, registry M0161):
	/// allowlisted typed fields, bounded filters, one optional group-by, and count/sum/avg/min/max where the pinned
	/// field allows it. No SQL, no cross-definition joins, no unbounded query.
	/// </summary>
	public class RmsSavedReportDefinition : IEntity
	{
		public const int MaxRows = 5000;
		public const int MaxColumns = 40;
		public const int MaxFilters = 12;

		public string RmsSavedReportDefinitionId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string DefinitionKey { get; set; }
		/// <summary>Null = current published version, with explicit cross-version mappings declared in MappingsJson.</summary>
		public int? DefinitionVersion { get; set; }
		/// <summary>Serialized <see cref="RecordReportSpec"/>.</summary>
		public string SpecJson { get; set; }
		public int MaxRowsPerRun { get; set; } = MaxRows;
		public bool IncludeRestricted { get; set; }
		public DateTime? LastRunOn { get; set; }
		public string LastRunByUserId { get; set; }
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
			get { return RmsSavedReportDefinitionId; }
			set { RmsSavedReportDefinitionId = value?.ToString(); }
		}

		[NotMapped] public string TableName => "RmsSavedReportDefinitions";
		[NotMapped] public string IdName => "RmsSavedReportDefinitionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "Spec" };

		[NotMapped]
		[JsonIgnore]
		public RecordReportSpec Spec
		{
			get { return string.IsNullOrWhiteSpace(SpecJson) ? new RecordReportSpec() : JsonConvert.DeserializeObject<RecordReportSpec>(SpecJson) ?? new RecordReportSpec(); }
			set { SpecJson = JsonConvert.SerializeObject(value ?? new RecordReportSpec()); }
		}
	}

	public class RecordReportSpec
	{
		/// <summary>Field keys, plus the built-in record.* columns (record.number, record.state, record.started_on, record.finalized_on, record.author, record.group).</summary>
		public List<string> Columns { get; set; } = new List<string>();
		public List<RecordReportFilter> Filters { get; set; } = new List<RecordReportFilter>();
		public string GroupByFieldKey { get; set; }
		public List<RecordReportAggregateSpec> Aggregates { get; set; } = new List<RecordReportAggregateSpec>();
		public string SortFieldKey { get; set; }
		public bool SortDescending { get; set; }
		/// <summary>Only finalized (and amended/accepted) revisions by default; drafts are working data.</summary>
		public bool IncludeDrafts { get; set; }
		public int? WindowDays { get; set; }
		/// <summary>Explicit per-version field mappings: version -> (reportFieldKey -> fieldKey in that version).</summary>
		public Dictionary<int, Dictionary<string, string>> VersionMappings { get; set; } = new Dictionary<int, Dictionary<string, string>>();
	}

	public class RecordReportFilter
	{
		public string FieldKey { get; set; }
		public RmsRuleOperator Operator { get; set; } = RmsRuleOperator.Equals;
		public string Value { get; set; }
		public List<string> Values { get; set; }
		public decimal? Min { get; set; }
		public decimal? Max { get; set; }
		public DateTime? MinDate { get; set; }
		public DateTime? MaxDate { get; set; }
	}

	public class RecordReportAggregateSpec
	{
		public RmsReportAggregate Aggregate { get; set; }
		/// <summary>Null for Count.</summary>
		public string FieldKey { get; set; }
	}

	public class RecordReportResult
	{
		public string ReportId { get; set; }
		public string Name { get; set; }
		public string DefinitionKey { get; set; }
		public int? DefinitionVersion { get; set; }
		public DateTime RanOn { get; set; }
		public List<string> Columns { get; set; } = new List<string>();
		public List<string> ColumnLabels { get; set; } = new List<string>();
		public List<List<string>> Rows { get; set; } = new List<List<string>>();
		public List<RecordReportGroup> Groups { get; set; } = new List<RecordReportGroup>();
		public int TotalMatched { get; set; }
		public bool Truncated { get; set; }
		/// <summary>Definition versions the run met that had no mapping for a report column; their rows are reported, not coerced.</summary>
		public List<int> UnmappedVersions { get; set; } = new List<int>();
		public List<string> Warnings { get; set; } = new List<string>();
	}

	public class RecordReportGroup
	{
		public string GroupKey { get; set; }
		public string GroupLabel { get; set; }
		public int Count { get; set; }
		public Dictionary<string, decimal?> Aggregates { get; set; } = new Dictionary<string, decimal?>();
	}

	public class RecordReportValidation
	{
		public List<RecordDefinitionIssue> Issues { get; set; } = new List<RecordDefinitionIssue>();
		public bool IsValid => Issues.All(i => i.Severity != "error");
	}
}
