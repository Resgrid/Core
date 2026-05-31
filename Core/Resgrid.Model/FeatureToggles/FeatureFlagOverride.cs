using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// A per-department override of a feature flag's value. An explicit, non-expired override takes
	/// precedence over rollout and targeting rules.
	/// </summary>
	[Table("FeatureFlagOverrides")]
	[ProtoContract]
	public class FeatureFlagOverride : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int FeatureFlagOverrideId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int FeatureFlagId { get; set; }

		[Required]
		[ProtoMember(3)]
		public int DepartmentId { get; set; }

		[Required]
		[ProtoMember(4)]
		public bool IsEnabled { get; set; }

		/// <summary>Override variant value for multivariate flags.</summary>
		[ProtoMember(5)]
		public string FlagValue { get; set; }

		[ProtoMember(6)]
		public string Reason { get; set; }

		/// <summary>Optional expiry after which the override is ignored.</summary>
		[ProtoMember(7)]
		public DateTime? ExpiresOn { get; set; }

		[Required]
		[ProtoMember(8)]
		public DateTime CreatedOn { get; set; }

		[ProtoMember(9)]
		public string CreatedByUserId { get; set; }

		[ProtoMember(10)]
		public DateTime? UpdatedOn { get; set; }

		[ProtoMember(11)]
		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return FeatureFlagOverrideId; }
			set { FeatureFlagOverrideId = (int)value; }
		}

		[NotMapped]
		public string TableName => "FeatureFlagOverrides";

		[NotMapped]
		public string IdName => "FeatureFlagOverrideId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
