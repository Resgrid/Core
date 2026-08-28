using Newtonsoft.Json;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// Independent per-channel egress modes for protected content for one department (one row per
	/// department). Every channel defaults to ProtectedDataEgressMode.GenericOnly; enabling protected
	/// content on any channel requires an explicit versioned warning acknowledgement. Changing any
	/// mode increments the department PolicyEpoch (on DepartmentDataProtectionPolicy), cancelling
	/// pending protected deliveries where possible. Egress policy can never relax BigBoard or Workflow
	/// restrictions.
	/// </summary>
	[Table("DepartmentProtectedDataEgressPolicies")]
	[ProtoContract]
	public class DepartmentProtectedDataEgressPolicy : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int DepartmentProtectedDataEgressPolicyId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		/// <summary>ProtectedDataEgressMode value (ProtectedAfterPin is not valid for push).</summary>
		[ProtoMember(3)]
		public int PushMode { get; set; }

		/// <summary>ProtectedDataEgressMode value (ProtectedAfterPin is not valid for email).</summary>
		[ProtoMember(4)]
		public int EmailMode { get; set; }

		/// <summary>ProtectedDataEgressMode value.</summary>
		[ProtoMember(5)]
		public int SmsMode { get; set; }

		/// <summary>ProtectedDataEgressMode value.</summary>
		[ProtoMember(6)]
		public int VoiceMode { get; set; }

		/// <summary>PIN-release one-time challenge lifetime in minutes (default 5).</summary>
		[ProtoMember(7)]
		public int PinChallengeExpiryMinutes { get; set; }

		/// <summary>Failed PIN attempts before lockout.</summary>
		[ProtoMember(8)]
		public int PinMaxAttempts { get; set; }

		/// <summary>Lockout duration in minutes after PinMaxAttempts failures.</summary>
		[ProtoMember(9)]
		public int PinLockoutMinutes { get; set; }

		/// <summary>Version identifier of the warning text the administrator acknowledged.</summary>
		[MaxLength(64)]
		[ProtoMember(10)]
		public string AcknowledgementVersion { get; set; }

		[MaxLength(128)]
		[ProtoMember(11)]
		public string AcknowledgedByUserId { get; set; }

		[ProtoMember(12)]
		public DateTime? AcknowledgedOn { get; set; }

		[ProtoMember(13)]
		public DateTime CreatedOn { get; set; }

		[ProtoMember(14)]
		public DateTime? UpdatedOn { get; set; }

		[MaxLength(128)]
		[ProtoMember(15)]
		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentProtectedDataEgressPolicyId; }
			set { DepartmentProtectedDataEgressPolicyId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentProtectedDataEgressPolicies";

		[NotMapped]
		public string IdName => "DepartmentProtectedDataEgressPolicyId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
