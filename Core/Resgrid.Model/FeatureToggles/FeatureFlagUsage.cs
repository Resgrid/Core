using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// Aggregated daily evaluation counts for a feature flag (optionally per department). Written by a
	/// background worker that flushes the service's in-memory counters; never written on the hot path.
	/// Mirrors the WorkflowDailyUsages aggregation pattern.
	/// </summary>
	[Table("FeatureFlagUsages")]
	[ProtoContract]
	public class FeatureFlagUsage : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int FeatureFlagUsageId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int FeatureFlagId { get; set; }

		/// <summary>Null aggregates across all departments for the day.</summary>
		[ProtoMember(3)]
		public int? DepartmentId { get; set; }

		[Required]
		[ProtoMember(4)]
		public DateTime UsageDate { get; set; }

		[Required]
		[ProtoMember(5)]
		public long EvaluationCount { get; set; }

		[Required]
		[ProtoMember(6)]
		public long EnabledCount { get; set; }

		[Required]
		[ProtoMember(7)]
		public long DisabledCount { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return FeatureFlagUsageId; }
			set { FeatureFlagUsageId = (int)value; }
		}

		[NotMapped]
		public string TableName => "FeatureFlagUsages";

		[NotMapped]
		public string IdName => "FeatureFlagUsageId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
