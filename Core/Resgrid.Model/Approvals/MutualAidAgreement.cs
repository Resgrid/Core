using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Approvals
{
	/// <summary>MutualAidAgreements.Status. Persisted: append only.</summary>
	public enum MutualAidAgreementStatuses
	{
		Draft = 0,
		/// <summary>Submitted to the department's approval chain.</summary>
		InApproval = 1,
		/// <summary>The department's chain approved it (sealed); the partner department's countersignature is a later step.</summary>
		Approved = 2,
		Withdrawn = 3
	}

	/// <summary>Where the partner department's countersignature stands (MutualAidAgreements.CountersignStatus, M0417). Persisted: append only.</summary>
	public enum MutualAidCountersignStatuses
	{
		/// <summary>No countersignature asked for (not required, or the chain has not approved yet).</summary>
		None = 0,
		/// <summary>The partner's request is open in the partner department, routed over the link.</summary>
		Requested = 1,
		Countersigned = 2,
		/// <summary>The partner declined (or let the request expire); copy the agreement into a new draft to try again.</summary>
		Declined = 3,
		/// <summary>The link was disabled or deleted before the partner signed: the request was voided.</summary>
		Lapsed = 4
	}

	/// <summary>
	/// A mutual-aid agreement document between the department and a partner (S4, plan §20 Department Links row: "L4 + L3 for a
	/// mutual-aid agreement document"). The department's own approval chain signs it at L2 and the completed chain seals it. When the
	/// agreement is made over a Department Link and <see cref="RequiresCountersignature"/> is set, the approved agreement is then routed
	/// over the link to the partner department (M0417): any of the partner's department admins countersigns it at L2 against the same
	/// content hash, through the multi-signer request machinery, and the completed request is sealed. No foreign keys.
	/// </summary>
	[Table("MutualAidAgreements")]
	public class MutualAidAgreement : IEntity
	{
		public const int TermsMaxLength = 20000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int MutualAidAgreementId { get; set; }

		public int DepartmentId { get; set; }

		[MaxLength(200)]
		public string Title { get; set; }

		/// <summary>The Department Link the agreement is made over, when there is one.</summary>
		public int? DepartmentLinkId { get; set; }

		public int? PartnerDepartmentId { get; set; }

		[MaxLength(200)]
		public string PartnerName { get; set; }

		/// <summary>The agreement's terms as plain text (rendered encoded; part of the content hash).</summary>
		public string Terms { get; set; }

		public DateTime? EffectiveOn { get; set; }

		public DateTime? ExpiresOn { get; set; }

		/// <summary><see cref="MutualAidAgreementStatuses"/>.</summary>
		public int Status { get; set; }

		public bool IsDeleted { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		// --- Partner countersignature over the Department Link (M0417) ---

		/// <summary>The partner department countersigns once the chain approved (needs <see cref="DepartmentLinkId"/>).</summary>
		public bool RequiresCountersignature { get; set; }

		/// <summary><see cref="MutualAidCountersignStatuses"/>.</summary>
		public int CountersignStatus { get; set; }

		/// <summary>The SignatureRequests row in the partner department.</summary>
		public int? CountersignRequestId { get; set; }

		public DateTime? CountersignRequestedOn { get; set; }

		public DateTime? CountersignedOn { get; set; }

		/// <summary>The partner's admin who countersigned (a member of the partner department).</summary>
		public string CountersignedByUserId { get; set; }

		/// <summary>Why the partner declined, or why the request lapsed.</summary>
		[MaxLength(1000)]
		public string CountersignNote { get; set; }

		[NotMapped] [JsonIgnore] public object IdValue { get => MutualAidAgreementId; set => MutualAidAgreementId = (int)value; }
		[NotMapped] public string TableName => "MutualAidAgreements";
		[NotMapped] public string IdName => "MutualAidAgreementId";
		[NotMapped] public int IdType => 0;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
