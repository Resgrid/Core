using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// An attribute/segment targeting rule for a feature flag. Rules are evaluated in ascending
	/// <see cref="Priority"/> order; the first matching rule decides the value (after overrides and
	/// the optional plan gate, but before the global default/rollout).
	/// </summary>
	[Table("FeatureFlagTargetingRules")]
	[ProtoContract]
	public class FeatureFlagTargetingRule : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int FeatureFlagTargetingRuleId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int FeatureFlagId { get; set; }

		/// <summary>Lower numbers are evaluated first.</summary>
		[Required]
		[ProtoMember(3)]
		public int Priority { get; set; }

		/// <summary>Backing int for <see cref="FeatureFlagAttributeTypes"/>.</summary>
		[Required]
		[ProtoMember(4)]
		public int AttributeType { get; set; }

		/// <summary>Backing int for <see cref="FeatureFlagOperatorTypes"/>.</summary>
		[Required]
		[ProtoMember(5)]
		public int OperatorType { get; set; }

		/// <summary>The value (or comma-separated list for In/NotIn) compared against the attribute.
		/// For the Custom attribute the leading "key:" segment selects the context key.</summary>
		[ProtoMember(6)]
		public string ComparisonValue { get; set; }

		/// <summary>Whether a matching department is on or off.</summary>
		[Required]
		[ProtoMember(7)]
		public bool ResultEnabled { get; set; }

		/// <summary>Variant value returned for a matching department on multivariate flags.</summary>
		[ProtoMember(8)]
		public string ResultValue { get; set; }

		/// <summary>Optional 0-100 rollout within the matched segment.</summary>
		[ProtoMember(9)]
		public int? RolloutPercentage { get; set; }

		[Required]
		[ProtoMember(10)]
		public DateTime CreatedOn { get; set; }

		[ProtoMember(11)]
		public string CreatedByUserId { get; set; }

		[ProtoMember(12)]
		public DateTime? UpdatedOn { get; set; }

		[ProtoMember(13)]
		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return FeatureFlagTargetingRuleId; }
			set { FeatureFlagTargetingRuleId = (int)value; }
		}

		[NotMapped]
		public string TableName => "FeatureFlagTargetingRules";

		[NotMapped]
		public string IdName => "FeatureFlagTargetingRuleId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
