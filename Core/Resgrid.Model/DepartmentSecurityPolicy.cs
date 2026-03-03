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
			new[] { "IdValue", "IdType", "TableName", "IdName", "Department" };
	}
}

