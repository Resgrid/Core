using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// Stores compliance and security policy settings for a department.
	/// Supports enterprise/government requirements such as mandatory MFA,
	/// SSO-only login, session limits, and IP-range restrictions.
	/// </summary>
	[Table("DepartmentSecurityPolicies")]
	[ProtoContract]
	public class DepartmentSecurityPolicy : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int DepartmentSecurityPolicyId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		/// <summary>When true, all department members must enroll in and use MFA.</summary>
		[ProtoMember(3)]
		public bool RequireMfa { get; set; }

		/// <summary>
		/// When true, password-based login is disabled — all users must authenticate
		/// exclusively through the department's configured SSO provider.
		/// </summary>
		[ProtoMember(4)]
		public bool RequireSso { get; set; }

		/// <summary>Idle session timeout in minutes (0 = use system default).</summary>
		[ProtoMember(5)]
		public int SessionTimeoutMinutes { get; set; }

		/// <summary>Maximum concurrent authenticated sessions per user (0 = unlimited).</summary>
		[ProtoMember(6)]
		public int MaxConcurrentSessions { get; set; }

		/// <summary>
		/// Comma-separated list of CIDR blocks from which login is permitted
		/// (e.g. "10.0.0.0/8,192.168.1.0/24"). Empty = no restriction.
		/// </summary>
		[MaxLength(2048)]
		[ProtoMember(7)]
		public string AllowedIpRanges { get; set; }

		/// <summary>Number of days before a password expires (0 = disabled).</summary>
		[ProtoMember(8)]
		public int PasswordExpirationDays { get; set; }

		/// <summary>Minimum required password length.</summary>
		[ProtoMember(9)]
		public int MinPasswordLength { get; set; }

		/// <summary>Whether passwords must contain mixed-case letters, digits, and symbols.</summary>
		[ProtoMember(10)]
		public bool RequirePasswordComplexity { get; set; }

		/// <summary>Data classification level for the department. Maps to <see cref="Resgrid.Model.DataClassificationLevel"/>.</summary>
		[ProtoMember(11)]
		public int DataClassificationLevel { get; set; }

		[Required]
		[ProtoMember(12)]
		public DateTime CreatedOn { get; set; }

		[ProtoMember(13)]
		public DateTime? UpdatedOn { get; set; }

		// ── Second-factor methods (passkey plan section 10.1) ────────────────
		// These choose which verification is acceptable; RequireMfa and Require2FAForAdmins still decide whether MFA
		// is required. TOTP is always accepted. A department with no policy row behaves as these defaults.

		/// <summary>A passkey bound to the requesting app counts as MFA for sign-in, department entry and step-up here.</summary>
		[ProtoMember(14)]
		public bool AllowPasskeysForLoginMfa { get; set; } = true;

		/// <summary>A passkey counts for this department's Protected Data Grants, ADP management and protected workflows.</summary>
		[ProtoMember(15)]
		public bool AllowPasskeysForAdp { get; set; } = true;

		/// <summary>Provider step-up (plan section 7.8) for the scope of <see cref="AllowPasskeysForLoginMfa"/>. Needs a tested mapping.</summary>
		[ProtoMember(16)]
		public bool AllowFederatedMfaForLoginMfa { get; set; }

		/// <summary>Provider step-up for the scope of <see cref="AllowPasskeysForAdp"/>. Needs a tested mapping.</summary>
		[ProtoMember(17)]
		public bool AllowFederatedMfaForAdp { get; set; }

		/// <summary>Responder approval (plan section 7.9) wherever the row's passkey switch is also on; never security changes or account factors.</summary>
		[ProtoMember(18)]
		public bool AllowResponderApproval { get; set; } = true;

		/// <summary>Same-session login evidence may serve an ADP grant instead of another verification (plan section 9.1).</summary>
		[ProtoMember(19)]
		public bool AcceptRecentLoginMfaForAdp { get; set; } = true;

		/// <summary>Fresh same-operator unlock evidence may serve an ADP reveal on a shared session.</summary>
		[ProtoMember(20)]
		public bool AcceptRecentUnlockMfaForAdp { get; set; } = true;

		/// <summary>
		/// Advanced by the server whenever RequireMfa or a sign-in method switch changes, in the same transaction as the
		/// change. Never written from this entity: saves leave it alone, and the save service advances it with one guarded
		/// statement so concurrent changes cannot lose an increment.
		/// </summary>
		[ProtoMember(21)]
		public long MfaPolicyVersion { get; set; }

		// ── Shared vehicle and workstation sessions (passkey plan section 10.5) ─────
		// The server's effective policy for shared sessions. An installation may ask for stricter behavior, never looser, and
		// a stricter value here applies to sessions already running.

		/// <summary>Minutes without operator activity before a shared session locks (1-15).</summary>
		[ProtoMember(22)]
		public int SharedIdleLockMinutes { get; set; } = SharedSessionRules.DefaultIdleLockMinutes;

		/// <summary>Hours after sign-in when a shared session ends, whatever the activity (1-24).</summary>
		[ProtoMember(23)]
		public int SharedShiftHours { get; set; } = SharedSessionRules.DefaultShiftHours;

		/// <summary>
		/// The apps (<see cref="SharedModeApps"/> flags) whose sessions in this department are always shared. Sessions that do
		/// not say which app they are count as required too, so leaving the app header out never relaxes this.
		/// </summary>
		[ProtoMember(24)]
		public int SharedModeRequiredApps { get; set; }

		// ── IEntity ──────────────────────────────────────────────────────────

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentSecurityPolicyId; }
			set { DepartmentSecurityPolicyId = (int)value; }
		}

		[NotMapped] public string TableName => "DepartmentSecurityPolicies";
		[NotMapped] public string IdName => "DepartmentSecurityPolicyId";
		[NotMapped] public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName", "Department", "MfaPolicyVersion" };
	}
}

