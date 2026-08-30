using Newtonsoft.Json;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// Durable Advanced Data Protection (ADP) policy for one department — the single data-safety truth
	/// for protection state (see DepartmentDataProtectionState). One row per department. Billing state
	/// and the enrollment feature flag are admission controls only and are never duplicated here as
	/// runtime authorization; this row records only the audit reference of the successful enrollment
	/// flag evaluation.
	/// </summary>
	[Table("DepartmentDataProtectionPolicies")]
	[ProtoContract]
	public class DepartmentDataProtectionPolicy : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int DepartmentDataProtectionPolicyId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		/// <summary>DepartmentDataProtectionState value.</summary>
		[ProtoMember(3)]
		public int State { get; set; }

		/// <summary>Protected-field catalog version this department is migrated to (0 = none).</summary>
		[ProtoMember(4)]
		public int CatalogVersion { get; set; }

		/// <summary>
		/// DepartmentDataProtectionMigrationKind value of the in-flight migration when State is a
		/// transitional one (EnrollmentQueued..Verifying, Rotating, DisableRequested, Decrypting,
		/// Failed); null when no migration is active. Disambiguates the shared Verifying state.
		/// </summary>
		[ProtoMember(5)]
		public int? ActiveMigrationKind { get; set; }

		/// <summary>
		/// Absolute lifetime of a Protected Data Grant in minutes. Default 15; values above the warning
		/// threshold (60) require a recorded reason; the platform enforces an operator ceiling
		/// (initially 480). Never sliding.
		/// </summary>
		[ProtoMember(6)]
		public int StepUpWindowMinutes { get; set; }

		/// <summary>Recorded administrator reason required when StepUpWindowMinutes exceeds 60.</summary>
		[ProtoMember(7)]
		public string StepUpWindowReason { get; set; }

		/// <summary>
		/// Monotonically increasing policy version. Any change to policy, egress, membership rules or
		/// catalog increments it and revokes previously issued grants (grants carry policy_epoch).
		/// </summary>
		[ProtoMember(8)]
		public long PolicyEpoch { get; set; }

		/// <summary>JSON map of application -> minimum client version allowed protected operations.</summary>
		[ProtoMember(9)]
		public string MinimumClientVersionsJson { get; set; }

		/// <summary>
		/// JSON record of every versioned Enrollment Wizard acknowledgement (section 12 disclosure
		/// items), including the persisted sizing scan results and shown estimate.
		/// </summary>
		[ProtoMember(10)]
		public string AcknowledgementsJson { get; set; }

		[MaxLength(128)]
		[ProtoMember(11)]
		public string AcknowledgedByUserId { get; set; }

		[ProtoMember(12)]
		public DateTime? AcknowledgedOn { get; set; }

		/// <summary>
		/// Value-free audit reference (flag key, evaluation result/source, correlation ID) of the fresh
		/// authoritative feature-flag evaluation performed immediately before the enrollment commit.
		/// </summary>
		[ProtoMember(13)]
		public string EnrollmentFlagEvaluationJson { get; set; }

		/// <summary>External billing reference (provider subscription/addon id) for the ADP addon.</summary>
		[MaxLength(256)]
		[ProtoMember(14)]
		public string AddonBillingReference { get; set; }

		/// <summary>
		/// The provider event id of the last ADP billing event applied (M0142). Payment providers
		/// retry and duplicate webhooks, so this is what lets the handler recognise an event it has
		/// already acted on and refuse to act twice.
		/// </summary>
		[ProtoMember(24)]
		public string LastBillingEventId { get; set; }

		/// <summary>
		/// When the last applied ADP billing event occurred at the provider (M0143). The id above
		/// only remembers ONE event, so it cannot recognise a redelivery that arrived after some
		/// other event overwrote it: cancel, renew, then a redelivery of the cancel would pass the id
		/// check and re-schedule an offboarding the renewal had just withdrawn. Comparing the
		/// provider's own timestamp makes any event older than the last applied one a no-op.
		/// </summary>
		[ProtoMember(25)]
		public DateTime? LastBillingEventOccurredOn { get; set; }

		/// <summary>Department-local overnight migration window start, "HH:mm" (default 22:00).</summary>
		[MaxLength(5)]
		[ProtoMember(15)]
		public string MigrationWindowStartLocal { get; set; }

		/// <summary>Department-local overnight migration window end, "HH:mm" (default 06:00).</summary>
		[MaxLength(5)]
		[ProtoMember(16)]
		public string MigrationWindowEndLocal { get; set; }

		/// <summary>IANA/Windows time zone id the migration window is evaluated in.</summary>
		[MaxLength(128)]
		[ProtoMember(17)]
		public string MigrationWindowTimeZone { get; set; }

		/// <summary>UTC instant offboarding becomes due (end of paid cycle plus any dunning grace).</summary>
		[ProtoMember(18)]
		public DateTime? OffboardingEffectiveOn { get; set; }

		/// <summary>DepartmentDataProtectionOffboardingSource value; null when no offboarding scheduled.</summary>
		[ProtoMember(19)]
		public int? OffboardingSource { get; set; }

		[ProtoMember(20)]
		public DateTime CreatedOn { get; set; }

		[MaxLength(128)]
		[ProtoMember(21)]
		public string CreatedByUserId { get; set; }

		[ProtoMember(22)]
		public DateTime? UpdatedOn { get; set; }

		[MaxLength(128)]
		[ProtoMember(23)]
		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentDataProtectionPolicyId; }
			set { DepartmentDataProtectionPolicyId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentDataProtectionPolicies";

		[NotMapped]
		public string IdName => "DepartmentDataProtectionPolicyId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
