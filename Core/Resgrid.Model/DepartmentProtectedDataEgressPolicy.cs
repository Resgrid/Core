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
	/// pending protected deliveries where possible. Egress policy can never relax BigBoard
	/// restrictions. Workflow restrictions are relaxed only through approved Protected Workflow
	/// releases (WorkflowProtectedRelease), which additionally require ProtectedWorkflowsEnabled here;
	/// every other workflow keeps receiving REDACTED values.
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

		/// <summary>
		/// Department opt-in to Protected Workflows. Off by default; while off every workflow receives REDACTED
		/// values and every release in the department is Suspended (department_disabled). Changing it bumps
		/// the PolicyEpoch like any other egress change.
		/// </summary>
		[ProtoMember(16)]
		public bool ProtectedWorkflowsEnabled { get; set; }

		/// <summary>Version of the Protected Workflows warning text the enabling administrator acknowledged.</summary>
		[MaxLength(64)]
		[ProtoMember(17)]
		public string ProtectedWorkflowsAckVersion { get; set; }

		[MaxLength(128)]
		[ProtoMember(18)]
		public string ProtectedWorkflowsAckByUserId { get; set; }

		[ProtoMember(19)]
		public DateTime? ProtectedWorkflowsAckOn { get; set; }

		/// <summary>Two-person rule: a release request must be approved by a second administrator.</summary>
		[ProtoMember(20)]
		public bool ProtectedWorkflowsRequireSecondApprover { get; set; }

		/// <summary>
		/// Relaxing the two-person rule is itself two-person: the first administrator's request is parked here and only a
		/// DIFFERENT administrator can confirm it. Tightening (turning the rule on) is immediate.
		/// </summary>
		[MaxLength(128)]
		[ProtoMember(21)]
		public string ProtectedWorkflowsRelaxRequestedByUserId { get; set; }

		[ProtoMember(22)]
		public DateTime? ProtectedWorkflowsRelaxRequestedOn { get; set; }

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
