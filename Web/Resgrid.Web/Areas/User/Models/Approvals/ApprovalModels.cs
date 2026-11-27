using System;
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Approvals;
using Resgrid.Web.Areas.User.Models.Signatures;

namespace Resgrid.Web.Areas.User.Models.Approvals
{
	/// <summary>"My approvals": the requests waiting for the member, and their own submissions.</summary>
	public class ApprovalsInboxView
	{
		public List<ApprovalInboxItem> Items { get; set; } = new List<ApprovalInboxItem>();
		public List<ApprovalRequest> Pending { get; set; } = new List<ApprovalRequest>();
		public List<SignatureSubjectKinds> Kinds { get; set; } = new List<SignatureSubjectKinds>();
		public bool IsAdmin { get; set; }
		public string Message { get; set; }
	}

	/// <summary>The department's requests in one status (department admins).</summary>
	public class ApprovalRequestsView
	{
		public ApprovalRequestStatuses Status { get; set; }
		public List<ApprovalRequest> Requests { get; set; } = new List<ApprovalRequest>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>One request: the subject's document, the steps, the shared signature panel and the reject and withdraw forms.</summary>
	public class ApprovalDetailView
	{
		public ApprovalView View { get; set; }
		public SignaturePanelModel Panel { get; set; }
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public string SubjectUrl { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
	}

	/// <summary>Subjects of one kind that could be submitted, and a subject's own approval state.</summary>
	public class ApprovalSubmitView
	{
		public SignatureSubjectKinds Kind { get; set; }
		public bool Available { get; set; }
		public bool ChainActive { get; set; }
		public List<ApprovalSubjectInfo> Candidates { get; set; } = new List<ApprovalSubjectInfo>();
		public ApprovalSubjectStatus Status { get; set; }
		public string Error { get; set; }
	}

	public class ApprovalChainsView
	{
		public List<ApprovalChainDefinition> Chains { get; set; } = new List<ApprovalChainDefinition>();
		public List<SignatureSubjectKinds> Kinds { get; set; } = new List<SignatureSubjectKinds>();
		public Dictionary<SignatureSubjectKinds, bool> Available { get; set; } = new Dictionary<SignatureSubjectKinds, bool>();
		public string Message { get; set; }
	}

	/// <summary>The chain editor's posted form: the chain and up to <see cref="ApprovalChain.MaxSteps"/> step rows.</summary>
	public class ApprovalChainEditView
	{
		public int ApprovalChainId { get; set; }
		public SignatureSubjectKinds Kind { get; set; }
		public string Name { get; set; }
		public bool IsActive { get; set; } = true;
		public bool AllowSelfApproval { get; set; }
		public int ExpiresInDays { get; set; } = 14;
		public List<ApprovalChainStepRow> Steps { get; set; } = new List<ApprovalChainStepRow>();

		public List<SelectOption> Roles { get; set; } = new List<SelectOption>();
		public List<SelectOption> Members { get; set; } = new List<SelectOption>();
		public List<SelectOption> Groups { get; set; } = new List<SelectOption>();
		public List<SelectOption> CertificationTypes { get; set; } = new List<SelectOption>();
		public bool HasAmount { get; set; }
		public string Error { get; set; }
	}

	public class ApprovalChainStepRow
	{
		public bool Include { get; set; }
		public int StepOrder { get; set; } = 1;
		public ApprovalApproverKinds ApproverKind { get; set; }
		public int? PersonnelRoleId { get; set; }
		public string UserId { get; set; }
		public int? DepartmentGroupId { get; set; }
		public int? CertificationTypeId { get; set; }
		public decimal? MinimumAmount { get; set; }
		public string Label { get; set; }
		public int? EscalateAfterHours { get; set; }
		public ApprovalEscalationTargets EscalateTo { get; set; }
		public int? EscalateToRoleId { get; set; }
		public string EscalateToUserId { get; set; }
	}

	public class SelectOption
	{
		public string Value { get; set; }
		public string Text { get; set; }
	}

	public class ApprovalDelegatesView
	{
		public List<ApprovalDelegate> Delegates { get; set; } = new List<ApprovalDelegate>();
		public List<SelectOption> Members { get; set; } = new List<SelectOption>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public bool IsAdmin { get; set; }
		public string CurrentUserId { get; set; }
		public string Message { get; set; }
	}

	public class MutualAidAgreementsView
	{
		public List<MutualAidAgreement> Agreements { get; set; } = new List<MutualAidAgreement>();
		public Dictionary<int, ApprovalRequest> Latest { get; set; } = new Dictionary<int, ApprovalRequest>();
		public string Message { get; set; }
	}

	public class MutualAidAgreementEditView
	{
		public int MutualAidAgreementId { get; set; }
		public string Title { get; set; }
		public int? DepartmentLinkId { get; set; }
		public string PartnerName { get; set; }
		public string Terms { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public int Status { get; set; }
		public List<SelectOption> Links { get; set; } = new List<SelectOption>();
		public ApprovalSubjectStatus Approval { get; set; }
		public string Error { get; set; }
		/// <summary>Round 10 A3: the partner department countersigns over the link once the chain approved.</summary>
		public bool RequiresCountersignature { get; set; }
		public int CountersignStatus { get; set; }
		public DateTime? CountersignRequestedOn { get; set; }
		public DateTime? CountersignedOn { get; set; }
		public string CountersignNote { get; set; }
	}
}
