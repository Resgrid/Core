using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// A system-wide feature flag definition with a global default. Per-department behavior is
	/// layered on top via <see cref="FeatureFlagOverride"/>, <see cref="FeatureFlagTargetingRule"/>
	/// and <see cref="FeatureFlagPrerequisite"/>.
	/// </summary>
	[Table("FeatureFlags")]
	[ProtoContract]
	public class FeatureFlag : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int FeatureFlagId { get; set; }

		/// <summary>Stable identifier referenced by code and clients (e.g. "new-dispatch-ui").</summary>
		[Required]
		[ProtoMember(2)]
		public string FlagKey { get; set; }

		[Required]
		[ProtoMember(3)]
		public string Name { get; set; }

		[ProtoMember(4)]
		public string Description { get; set; }

		[ProtoMember(5)]
		public string Category { get; set; }

		/// <summary>Comma-separated free-form tags for grouping/searching.</summary>
		[ProtoMember(6)]
		public string Tags { get; set; }

		/// <summary>Backing int for <see cref="FeatureFlagValueTypes"/>.</summary>
		[Required]
		[ProtoMember(7)]
		public int FlagType { get; set; }

		/// <summary>The global on/off default and application-wide kill switch.</summary>
		[Required]
		[ProtoMember(8)]
		public bool IsEnabledGlobally { get; set; }

		/// <summary>Value returned when the flag resolves "on" for multivariate flags.</summary>
		[ProtoMember(9)]
		public string DefaultValue { get; set; }

		/// <summary>Value returned when the flag resolves "off" for multivariate flags.</summary>
		[ProtoMember(10)]
		public string OffValue { get; set; }

		/// <summary>0-100 gradual rollout across departments when globally on; null = 100%.</summary>
		[ProtoMember(11)]
		public int? RolloutPercentage { get; set; }

		/// <summary>Optional minimum subscription plan id required for the flag to be on.</summary>
		[ProtoMember(12)]
		public int? MinimumPlanType { get; set; }

		/// <summary>Optional environment scope (backing int for SystemEnvironment); null = all.</summary>
		[ProtoMember(13)]
		public int? Environment { get; set; }

		[ProtoMember(14)]
		public DateTime? EnableOn { get; set; }

		[ProtoMember(15)]
		public DateTime? DisableOn { get; set; }

		[Required]
		[ProtoMember(16)]
		public bool IsArchived { get; set; }

		/// <summary>Permanent flags are excluded from stale-flag detection.</summary>
		[Required]
		[ProtoMember(17)]
		public bool IsPermanent { get; set; }

		[ProtoMember(18)]
		public DateTime? LastEvaluatedOn { get; set; }

		[Required]
		[ProtoMember(19)]
		public DateTime CreatedOn { get; set; }

		[ProtoMember(20)]
		public string CreatedByUserId { get; set; }

		[ProtoMember(21)]
		public DateTime? UpdatedOn { get; set; }

		[ProtoMember(22)]
		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return FeatureFlagId; }
			set { FeatureFlagId = (int)value; }
		}

		[NotMapped]
		public string TableName => "FeatureFlags";

		[NotMapped]
		public string IdName => "FeatureFlagId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
