using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model.Approvals
{
	/// <summary>Who approves a chain step (ApprovalChainSteps.ApproverKind). Persisted: append only.</summary>
	public enum ApprovalApproverKinds
	{
		/// <summary>Any active member holding the personnel role.</summary>
		Role = 0,

		/// <summary>One named member (their delegate while they are on leave or have delegated).</summary>
		NamedMember = 1,

		/// <summary>The admins of a group: the step's group, or the subject's own group when the step names none.</summary>
		GroupAdmin = 2,

		/// <summary>A certifying official: any active member holding a valid certification of the type (or the role), signing as such.</summary>
		CertifyingOfficial = 3,

		/// <summary>Any active department admin.</summary>
		DepartmentAdmins = 4
	}

	/// <summary>Who a stalled step escalates to (ApprovalChainSteps.EscalateTo). Persisted: append only.</summary>
	public enum ApprovalEscalationTargets
	{
		DepartmentAdmins = 0,
		Role = 1,
		NamedMember = 2
	}

	/// <summary>An approval request's state (ApprovalRequests.Status). Persisted: append only.</summary>
	public enum ApprovalRequestStatuses
	{
		Pending = 0,
		Approved = 1,
		Rejected = 2,
		/// <summary>The submitter (or an admin) withdrew it before it was decided.</summary>
		Withdrawn = 3,
		/// <summary>A re-submission after a change replaced it; its signatures stand only for the old content.</summary>
		Superseded = 4,
		/// <summary>The signature request expired before every step approved.</summary>
		Expired = 5
	}

	/// <summary>Why the approval layer refused something (the web layer localizes the code).</summary>
	public enum ApprovalRefusals
	{
		None = 0,
		InvalidRequest = 1,
		/// <summary>The subject does not exist (or the caller may not see it).</summary>
		SubjectNotFound = 2,
		/// <summary>The module or add-on that owns the subject is not active for the department.</summary>
		ModuleUnavailable = 3,
		/// <summary>No active chain is defined for the subject kind.</summary>
		NoChain = 4,
		/// <summary>An approval is already pending for the subject at this content.</summary>
		AlreadyPending = 5,
		/// <summary>A re-submission needs a change: the content is what was rejected (or already approved).</summary>
		Unchanged = 6,
		/// <summary>The subject is not in a state that can be submitted (a draft requisition, a sent invoice).</summary>
		NotSubmittable = 7,
		/// <summary>The request is no longer pending.</summary>
		Closed = 8,
		/// <summary>The subject changed since it was submitted: the approval is void and must be submitted again.</summary>
		SubjectChanged = 9,
		NotAnApprover = 10,
		NotYourTurn = 11,
		StepUpRequired = 12,
		PasskeyRequired = 13,
		/// <summary>A rejection needs a reason.</summary>
		ReasonRequired = 14,
		/// <summary>A chain step names nobody who could approve it.</summary>
		NoApprover = 15,
		/// <summary>Only the submitter or a department admin may do this.</summary>
		NotAllowed = 16,
		SignatureFailed = 17,
		/// <summary>No step of the chain applies to the subject's amount: it needs no approval.</summary>
		NotRequired = 18
	}

	/// <summary>
	/// A department's approval chain for one subject kind (S4, plan §23): ordered steps, amount thresholds, parallel steps (equal
	/// order), delegation while on leave and escalation after N hours. One active chain per subject kind. Built on the S3 multi-signer
	/// request: every approval is an L2 signature bound to the subject's content hash. No foreign keys (UnlinkedDepartmentCleanup).
	/// </summary>
	[Table("ApprovalChains")]
	public class ApprovalChain : IEntity
	{
		public const int MaxSteps = 12;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ApprovalChainId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="SignatureSubjectKinds"/> (an approval subject kind).</summary>
		public int SubjectKind { get; set; }

		[MaxLength(200)]
		public string Name { get; set; }

		public bool IsActive { get; set; }

		/// <summary>The submitter may approve their own submission (off by default: separation of duties).</summary>
		public bool AllowSelfApproval { get; set; }

		/// <summary>Days before an undecided request expires (1-90).</summary>
		public int ExpiresInDays { get; set; } = 14;

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped] [JsonIgnore] public object IdValue { get => ApprovalChainId; set => ApprovalChainId = (int)value; }
		[NotMapped] public string TableName => "ApprovalChains";
		[NotMapped] public string IdName => "ApprovalChainId";
		[NotMapped] public int IdType => 0;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One step of a chain definition. Steps with the same <see cref="StepOrder"/> are parallel: every one must approve.</summary>
	[Table("ApprovalChainSteps")]
	public class ApprovalChainStep : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ApprovalChainStepId { get; set; }

		public int ApprovalChainId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>1-based; equal orders form a parallel group.</summary>
		public int StepOrder { get; set; } = 1;

		/// <summary><see cref="ApprovalApproverKinds"/>.</summary>
		public int ApproverKind { get; set; }

		public int? PersonnelRoleId { get; set; }

		public string UserId { get; set; }

		/// <summary>GroupAdmin: the group; null means the subject's own group.</summary>
		public int? DepartmentGroupId { get; set; }

		/// <summary>CertifyingOfficial: the certification type that qualifies.</summary>
		public int? CertificationTypeId { get; set; }

		/// <summary>Threshold: the step applies only when the subject's amount is above this (null: always).</summary>
		public decimal? MinimumAmount { get; set; }

		[MaxLength(200)]
		public string Label { get; set; }

		/// <summary>Hours after the step's turn starts before it escalates (null or 0: never).</summary>
		public int? EscalateAfterHours { get; set; }

		/// <summary><see cref="ApprovalEscalationTargets"/>.</summary>
		public int EscalateTo { get; set; }

		public int? EscalateToRoleId { get; set; }

		public string EscalateToUserId { get; set; }

		[NotMapped] [JsonIgnore] public object IdValue { get => ApprovalChainStepId; set => ApprovalChainStepId = (int)value; }
		[NotMapped] public string TableName => "ApprovalChainSteps";
		[NotMapped] public string IdName => "ApprovalChainStepId";
		[NotMapped] public int IdType => 0;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// One submission of a subject for approval: the chain resolved for its amount, the S3 signature request that carries the steps and
	/// the content hash every approval binds to. A change to the subject voids it (the gate compares the hash); a re-submission
	/// supersedes it.
	/// </summary>
	[Table("ApprovalRequests")]
	public class ApprovalRequest : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ApprovalRequestId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="SignatureSubjectKinds"/>.</summary>
		public int SubjectKind { get; set; }

		[MaxLength(128)]
		public string SubjectId { get; set; }

		public int ApprovalChainId { get; set; }

		/// <summary>The S3 multi-signer request whose member steps are the approvals.</summary>
		public int? SignatureRequestId { get; set; }

		[MaxLength(256)]
		public string Title { get; set; }

		public decimal? Amount { get; set; }

		[MaxLength(8)]
		public string Currency { get; set; }

		/// <summary>The subject's content hash at submission (64 lower-case hex): every approval binds to it.</summary>
		[MaxLength(64)]
		public string ContentSha256 { get; set; }

		[MaxLength(128)]
		public string RevisionId { get; set; }

		/// <summary><see cref="ApprovalRequestStatuses"/>.</summary>
		public int Status { get; set; }

		public string SubmittedByUserId { get; set; }

		public DateTime SubmittedOn { get; set; }

		public DateTime? DecidedOn { get; set; }

		public string DecidedByUserId { get; set; }

		public string RejectReason { get; set; }

		public int? SupersedesApprovalRequestId { get; set; }

		/// <summary>The sealed evidence document of the completed chain (S1; "sealing pending" until it seals).</summary>
		public int? SealedDocumentId { get; set; }

		/// <summary>When the owning module applied the decision (the requisition moved to Approved, the plan approved).</summary>
		public DateTime? AppliedOn { get; set; }

		/// <summary>Why the module could not apply the decision (its own rule refused); the approval itself stands.</summary>
		[MaxLength(500)]
		public string ApplyError { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped] [JsonIgnore] public bool IsPending => Status == (int)ApprovalRequestStatuses.Pending;

		[NotMapped] [JsonIgnore] public object IdValue { get => ApprovalRequestId; set => ApprovalRequestId = (int)value; }
		[NotMapped] public string TableName => "ApprovalRequests";
		[NotMapped] public string IdName => "ApprovalRequestId";
		[NotMapped] public int IdType => 0;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "IsPending" };
	}

	/// <summary>
	/// The runtime state of one resolved step: which S3 step carries it, when its approvers were told, when it escalates and to whom it
	/// was delegated. Worker 97 reads it with due-work queries (never a sweep of departments).
	/// </summary>
	[Table("ApprovalRequestSteps")]
	public class ApprovalRequestStep : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ApprovalRequestStepId { get; set; }

		public int ApprovalRequestId { get; set; }

		public int DepartmentId { get; set; }

		public int SignatureRequestSignerId { get; set; }

		public int ApprovalChainStepId { get; set; }

		public int StepOrder { get; set; }

		[MaxLength(200)]
		public string Label { get; set; }

		/// <summary>Claimed when the step's turn was announced to its approvers (once).</summary>
		public DateTime? TurnNotifiedOn { get; set; }

		/// <summary>When the step escalates if nobody approved it (turn start + the step's hours).</summary>
		public DateTime? EscalateDueOn { get; set; }

		public DateTime? EscalatedOn { get; set; }

		/// <summary>Members the step was delegated to (comma separated), because a named approver was away.</summary>
		[MaxLength(400)]
		public string DelegatedToUserIds { get; set; }

		[NotMapped] [JsonIgnore] public object IdValue { get => ApprovalRequestStepId; set => ApprovalRequestStepId = (int)value; }
		[NotMapped] public string TableName => "ApprovalRequestSteps";
		[NotMapped] public string IdName => "ApprovalRequestStepId";
		[NotMapped] public int IdType => 0;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A member's approval delegate. With no window, the delegate stands in only while the member is on leave (T0 MemberLeavePeriods);
	/// with a window, also while it is open.
	/// </summary>
	[Table("ApprovalDelegates")]
	public class ApprovalDelegate : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ApprovalDelegateId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		public string DelegateUserId { get; set; }

		public DateTime? StartsOn { get; set; }

		public DateTime? EndsOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped] [JsonIgnore] public object IdValue { get => ApprovalDelegateId; set => ApprovalDelegateId = (int)value; }
		[NotMapped] public string TableName => "ApprovalDelegates";
		[NotMapped] public string IdName => "ApprovalDelegateId";
		[NotMapped] public int IdType => 0;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>The approval uses of S4 (plan §23): which subject kinds have approval chains, and what owns each.</summary>
	public static class ApprovalUses
	{
		public static readonly SignatureSubjectKinds[] Kinds =
		{
			SignatureSubjectKinds.PurchaseRequisitionApproval,
			SignatureSubjectKinds.IncidentPlanApproval,
			SignatureSubjectKinds.InvoiceApproval,
			SignatureSubjectKinds.PayrollRunApproval,
			SignatureSubjectKinds.CalOesMarsPackageApproval,
			SignatureSubjectKinds.MutualAidAgreementApproval
		};

		public static bool IsApprovalKind(SignatureSubjectKinds kind) => Kinds.Contains(kind);

		/// <summary>Whether amount thresholds mean anything for the kind (the IAP and agreements carry no amount).</summary>
		public static bool HasAmount(SignatureSubjectKinds kind) =>
			kind is SignatureSubjectKinds.PurchaseRequisitionApproval or SignatureSubjectKinds.InvoiceApproval or SignatureSubjectKinds.PayrollRunApproval
				or SignatureSubjectKinds.CalOesMarsPackageApproval;

		public const string StatementVersion = "approval/1";
	}

	/// <summary>A chain and its steps, as saved and edited.</summary>
	public sealed class ApprovalChainDefinition
	{
		public ApprovalChain Chain { get; set; }
		public List<ApprovalChainStep> Steps { get; set; } = new List<ApprovalChainStep>();
	}

	/// <summary>What the approval layer knows of a subject, from its module's own reads.</summary>
	public sealed class ApprovalSubjectInfo
	{
		public SignatureSubjectKinds Kind { get; set; }
		public string SubjectId { get; set; }
		public string Title { get; set; }
		public decimal? Amount { get; set; }
		public string Currency { get; set; }
		/// <summary>SHA-256 (64 hex) of the subject's canonical approvable content; status, approval stamps and edit times are not part of it.</summary>
		public string ContentSha256 { get; set; }
		public string RevisionId { get; set; }
		/// <summary>The document the approvers read (HTML, encoded values only); the completed chain seals it.</summary>
		public string DocumentHtml { get; set; }
		/// <summary>The subject's group (GroupAdmin steps that name no group).</summary>
		public int? GroupId { get; set; }
		/// <summary>The member who owns the subject (the requisition's requester): may submit it.</summary>
		public string OwnerUserId { get; set; }
		/// <summary>The subject is in a state that can be submitted for approval.</summary>
		public bool Submittable { get; set; } = true;
		public int? RetentionYears { get; set; }
	}

	/// <summary>A new submission (or a re-submission after a change).</summary>
	public sealed class ApprovalSubmitInput
	{
		public int DepartmentId { get; set; }
		public SignatureSubjectKinds Kind { get; set; }
		public string SubjectId { get; set; }
		public string UserId { get; set; }
		public bool IsDepartmentAdmin { get; set; }
		public string Culture { get; set; }
		public string IpAddress { get; set; }
		public string UserAgent { get; set; }
	}

	public sealed class ApprovalResult
	{
		public ApprovalRefusals Refusal { get; set; }
		public ApprovalRequest Request { get; set; }
		public bool Succeeded => Refusal == ApprovalRefusals.None;
		public static ApprovalResult Refused(ApprovalRefusals refusal) => new ApprovalResult { Refusal = refusal };
		public static ApprovalResult Of(ApprovalRequest request) => new ApprovalResult { Request = request };
	}

	/// <summary>What a module asks before acting on a subject ("approve before send").</summary>
	public sealed class ApprovalGateResult
	{
		/// <summary>An active chain exists for the kind and the module is available: the action needs an approval.</summary>
		public bool Required { get; set; }
		/// <summary>The latest approval is granted and the subject still hashes to what was approved.</summary>
		public bool Approved { get; set; }
		/// <summary>An approval was granted for different content (the subject changed after approval): it no longer counts.</summary>
		public bool ChangedSinceApproval { get; set; }
		public ApprovalRequest Latest { get; set; }
		public bool Allows => !Required || Approved;
	}

	/// <summary>A subject's approval state for its module page and the approvals tab.</summary>
	public sealed class ApprovalSubjectStatus
	{
		public SignatureSubjectKinds Kind { get; set; }
		public string SubjectId { get; set; }
		public bool ModuleAvailable { get; set; }
		public bool ChainActive { get; set; }
		public ApprovalSubjectInfo Subject { get; set; }
		public ApprovalRequest Latest { get; set; }
		public List<ApprovalRequest> History { get; set; } = new List<ApprovalRequest>();
		/// <summary>The subject's content no longer matches the latest request's hash.</summary>
		public bool Changed { get; set; }
		public bool CanSubmit { get; set; }
	}

	/// <summary>One step of a request, as the approvals page shows it.</summary>
	public sealed class ApprovalStepState
	{
		public ApprovalRequestStep Step { get; set; }
		public SignatureRequestSigner Signer { get; set; }
		public bool IsTurn { get; set; }
	}

	/// <summary>One request with its steps and what the viewing member may do.</summary>
	public sealed class ApprovalView
	{
		public ApprovalRequest Request { get; set; }
		public SignatureRequestRecord SignatureRequest { get; set; }
		public SignatureStepView StepView { get; set; }
		public List<ApprovalStepState> Steps { get; set; } = new List<ApprovalStepState>();
		public ApprovalSubjectInfo Subject { get; set; }
		public bool Changed { get; set; }
		public bool CanApprove { get; set; }
		public bool CanWithdraw { get; set; }
		public SealedDocument Sealed { get; set; }
	}

	/// <summary>A request waiting for the member ("My approvals").</summary>
	public sealed class ApprovalInboxItem
	{
		public int ApprovalRequestId { get; set; }
		public int SignatureRequestId { get; set; }
		public int SubjectKind { get; set; }
		public string SubjectId { get; set; }
		public string Title { get; set; }
		public decimal? Amount { get; set; }
		public string Currency { get; set; }
		public string StepLabel { get; set; }
		public DateTime SubmittedOn { get; set; }
		public DateTime ExpiresOn { get; set; }
		public bool Escalated { get; set; }
		public string ContentCode { get; set; }
	}

	/// <summary>The pure routing rules of an approval chain: thresholds, parallel groups, escalation timing, delegation.</summary>
	public static class ApprovalRouting
	{
		public const int MaxExpiresInDays = 90;
		/// <summary>Named and group approvers resolved onto one step (keeps the stored eligibility within its column).</summary>
		public const int MaxNamedApprovers = 12;

		/// <summary>A step applies when it has no threshold, or the subject's amount is above it.</summary>
		public static bool Applies(ApprovalChainStep step, decimal? amount) =>
			step != null && (!step.MinimumAmount.HasValue || (amount.HasValue && amount.Value > step.MinimumAmount.Value));

		/// <summary>
		/// The steps that apply to an amount with their run order: thresholds filter first, then the remaining orders are compacted to
		/// 1..n so a skipped group leaves no gap. Equal orders stay equal (a parallel group).
		/// </summary>
		public static List<(ApprovalChainStep Step, int Order)> Resolve(IEnumerable<ApprovalChainStep> steps, decimal? amount)
		{
			var applicable = (steps ?? Enumerable.Empty<ApprovalChainStep>()).Where(s => Applies(s, amount))
				.OrderBy(s => s.StepOrder).ThenBy(s => s.ApprovalChainStepId).ToList();
			var orders = applicable.Select(s => s.StepOrder).Distinct().OrderBy(o => o).ToList();
			return applicable.Select(s => (s, orders.IndexOf(s.StepOrder) + 1)).ToList();
		}

		/// <summary>When a step escalates: its turn start plus its hours (null when it never escalates or has not started).</summary>
		public static DateTime? EscalationDue(DateTime? turnStartedOnUtc, int? escalateAfterHours)
		{
			if (!turnStartedOnUtc.HasValue || !escalateAfterHours.HasValue || escalateAfterHours.Value <= 0)
				return null;
			return turnStartedOnUtc.Value.AddHours(Math.Min(escalateAfterHours.Value, 24 * MaxExpiresInDays));
		}

		/// <summary>Whether the member is away now: on a leave period, or inside an explicit delegation window.</summary>
		public static bool IsAway(string userId, DateTime utcNow, IEnumerable<MemberLeavePeriod> leave, IEnumerable<ApprovalDelegate> delegates)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return false;
			if ((leave ?? Enumerable.Empty<MemberLeavePeriod>()).Any(l => Same(l.UserId, userId) && l.StartsOn <= utcNow && l.EndsOn >= utcNow))
				return true;
			return (delegates ?? Enumerable.Empty<ApprovalDelegate>()).Any(d => Same(d.UserId, userId) && (d.StartsOn.HasValue || d.EndsOn.HasValue) && InWindow(d, utcNow));
		}

		/// <summary>The delegates who stand in for an away member now (none when the member is not away).</summary>
		public static List<string> DelegatesFor(string userId, DateTime utcNow, IEnumerable<MemberLeavePeriod> leave, IEnumerable<ApprovalDelegate> delegates)
		{
			var list = (delegates ?? Enumerable.Empty<ApprovalDelegate>()).ToList();
			if (!IsAway(userId, utcNow, leave, list))
				return new List<string>();
			return list.Where(d => Same(d.UserId, userId) && !string.IsNullOrWhiteSpace(d.DelegateUserId) && !Same(d.DelegateUserId, userId) && InWindow(d, utcNow))
				.Select(d => d.DelegateUserId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		private static bool InWindow(ApprovalDelegate d, DateTime utcNow) => (!d.StartsOn.HasValue || d.StartsOn.Value <= utcNow) && (!d.EndsOn.HasValue || d.EndsOn.Value >= utcNow);

		/// <summary>Checks a chain before it is saved.</summary>
		public static ApprovalRefusals Validate(ApprovalChainDefinition definition)
		{
			var chain = definition?.Chain;
			var steps = definition?.Steps ?? new List<ApprovalChainStep>();
			if (chain == null || chain.DepartmentId <= 0 || !ApprovalUses.IsApprovalKind((SignatureSubjectKinds)chain.SubjectKind) || string.IsNullOrWhiteSpace(chain.Name))
				return ApprovalRefusals.InvalidRequest;
			if (steps.Count == 0 || steps.Count > ApprovalChain.MaxSteps)
				return ApprovalRefusals.InvalidRequest;
			if (chain.ExpiresInDays < 1 || chain.ExpiresInDays > MaxExpiresInDays)
				return ApprovalRefusals.InvalidRequest;

			foreach (var step in steps)
			{
				if (step == null || step.StepOrder < 1 || step.StepOrder > ApprovalChain.MaxSteps || !Enum.IsDefined(typeof(ApprovalApproverKinds), step.ApproverKind))
					return ApprovalRefusals.InvalidRequest;
				if (step.MinimumAmount.HasValue && (step.MinimumAmount.Value < 0 || !ApprovalUses.HasAmount((SignatureSubjectKinds)chain.SubjectKind)))
					return ApprovalRefusals.InvalidRequest;
				switch ((ApprovalApproverKinds)step.ApproverKind)
				{
					case ApprovalApproverKinds.Role when !(step.PersonnelRoleId > 0):
					case ApprovalApproverKinds.NamedMember when string.IsNullOrWhiteSpace(step.UserId):
					case ApprovalApproverKinds.CertifyingOfficial when !(step.CertificationTypeId > 0) && !(step.PersonnelRoleId > 0):
						return ApprovalRefusals.NoApprover;
				}
				if (step.EscalateAfterHours.HasValue && (step.EscalateAfterHours.Value < 0 || step.EscalateAfterHours.Value > 24 * MaxExpiresInDays))
					return ApprovalRefusals.InvalidRequest;
				if (step.EscalateAfterHours > 0)
				{
					if (!Enum.IsDefined(typeof(ApprovalEscalationTargets), step.EscalateTo))
						return ApprovalRefusals.InvalidRequest;
					if ((step.EscalateTo == (int)ApprovalEscalationTargets.Role && !(step.EscalateToRoleId > 0))
						|| (step.EscalateTo == (int)ApprovalEscalationTargets.NamedMember && string.IsNullOrWhiteSpace(step.EscalateToUserId)))
						return ApprovalRefusals.NoApprover;
				}
			}

			return ApprovalRefusals.None;
		}

		/// <summary>The short form of an amount for titles and messages ("1,250.00 USD").</summary>
		public static string AmountText(decimal? amount, string currency) =>
			amount.HasValue ? amount.Value.ToString("N2", CultureInfo.InvariantCulture) + (string.IsNullOrWhiteSpace(currency) ? string.Empty : " " + currency.Trim().ToUpperInvariant()) : string.Empty;

		public static bool Same(string a, string b) =>
			!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
	}
}
