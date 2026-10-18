using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using ProtoBuf;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("DepartmentMembers")]
	[ProtoContract]
	public class DepartmentMember : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int DepartmentMemberId { get; set; }

		[Required]
		[ProtoMember(2)]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		//[ForeignKey("DepartmentId")]
		[ProtoMember(3)]
		public virtual Department Department { get; set; }

		[Required]
		[ProtoMember(4)]
		[ForeignKey("User"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string UserId { get; set; }

		//[ForeignKey("UserId")]
		[ProtoMember(5)]
		public virtual IdentityUser User { get; set; }

		[ProtoMember(6)]
		public bool? IsAdmin { get; set; }

		[ProtoMember(7)]
		public bool? IsDisabled { get; set; }

		[ProtoMember(8)]
		public bool? IsHidden { get; set; }

		[ProtoMember(9)]
		public bool IsDefault { get; set; }

		[ProtoMember(10)]
		public bool IsActive { get; set; }

		[ProtoMember(11)]
		public bool IsDeleted { get; set; }

		[ProtoMember(12)]
		[ForeignKey("Rank"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int? RankId { get; set; }

		public virtual Rank Rank { get; set; }

		/// <summary>
		/// The subject identifier from the external SSO identity provider (IdP) that is linked
		/// to this department membership. Null when the user has not been linked via SSO.
		/// </summary>
		[MaxLength(512)]
		public string ExternalSsoId { get; set; }

		/// <summary>Date/time when the external SSO identity was first linked to this membership.</summary>
		public DateTime? SsoLinkedOn { get; set; }

		/// <summary>Date/time of the most recent successful SSO login for this membership.</summary>
		public DateTime? LastSsoLoginOn { get; set; }

		/// <summary>
		/// Date/time (UTC) when the member last set or changed their password.
		/// Used to enforce the department's <see cref="DepartmentSecurityPolicy.PasswordExpirationDays"/> policy.
		/// Null means the password has never been explicitly changed since the column was added (treated as never-expired unless a policy is active).
		/// </summary>
		public DateTime? PasswordLastSetOn { get; set; }

		/// <summary>
		/// When true the user must change their password on next login.
		/// Set by admins when creating accounts or resetting passwords.
		/// Cleared automatically after the user completes the forced password change.
		/// </summary>
		[ProtoMember(13)]
		public bool MustChangePassword { get; set; }

		/// <summary>
		/// Department Import activation state (<see cref="Onboarding.ImportActivationState"/>; wizard plan section 15).
		/// Null for every member not created by an import, and once an imported member is active. A Dormant member
		/// cannot sign in and receives nothing until an administrator invites them.
		/// </summary>
		[DefaultValue(null)]
		[ProtoMember(14)]
		public int? ImportActivationState { get; set; }

		/// <summary>True while this membership is a dormant (imported, not yet activated) one.</summary>
		[NotMapped]
		[JsonIgnore]
		public bool IsImportDormant => ImportActivationState == (int)Onboarding.ImportActivationState.Dormant
			|| ImportActivationState == (int)Onboarding.ImportActivationState.Invited;

		[NotMapped]
		public string TableName => "DepartmentMembers";

		[NotMapped]
		public string IdName => "DepartmentMemberId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "User", "Rank" };

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentMemberId; }
			set { DepartmentMemberId = (int)value; }
		}
	}
}
