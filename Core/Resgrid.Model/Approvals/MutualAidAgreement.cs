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

	/// <summary>
	/// A mutual-aid agreement document between the department and a partner (S4, plan §20 Department Links row: "L4 + L3 for a
	/// mutual-aid agreement document"). The department's own approval chain signs it at L2 and the completed chain seals it. The
	/// partner department's countersignature across a Department Link needs the Department Links sharing helper, which is not on this
	/// branch: the agreement records the partner (link id and name) and that their signature is owed. No foreign keys.
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

		[NotMapped] [JsonIgnore] public object IdValue { get => MutualAidAgreementId; set => MutualAidAgreementId = (int)value; }
		[NotMapped] public string TableName => "MutualAidAgreements";
		[NotMapped] public string IdName => "MutualAidAgreementId";
		[NotMapped] public int IdType => 0;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
